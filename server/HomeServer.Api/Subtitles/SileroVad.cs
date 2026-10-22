using System.Buffers.Binary;
using Microsoft.ML.OnnxRuntime;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Subtitles;

public sealed class SileroVad(MediaTools tools) : IDisposable
{
    public const string ModelName = "Silero VAD 6.2.3 · 16 kHz · CPU";
    public const string ModelHash = "7ed98ddbad84ccac4cd0aeb3099049280713df825c610a8ed34543318f1b2c49";
    private readonly Lazy<InferenceSession> _session = new(CreateSession, LazyThreadSafetyMode.ExecutionAndPublication);
    private static InferenceSession CreateSession()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "silero-vad-6.2.3-16k.onnx");
        if (!File.Exists(path)) throw new DomainException("Yerel ses etkinliği modeli bulunamadı.", 409);
        AppFiles.RejectLink(path);
        if (StateFolders.Hash(path) != ModelHash) throw new DomainException("Ses etkinliği modelinin hash'i doğrulanamadı.", 409);
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        var session = new InferenceSession(path, options);
        if (!new[] { "input", "state", "sr" }.All(session.InputNames.Contains) || session.OutputNames.Count != 2)
        { session.Dispose(); throw new DomainException("Ses etkinliği modeli beklenen tensor sözleşmesinde değil.", 409); }
        return session;
    }
    public VoiceCapabilities Capabilities()
    {
        try { using var state = new State(_session.Value, CancellationToken.None); _ = state.Infer(new float[512]); return new(true, ModelName, "ONNX Runtime 1.31.0 · CPU"); }
        catch (Exception ex) when (ex is DomainException or OnnxRuntimeException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        { return new(false, ModelName, "ONNX Runtime 1.31.0 · CPU", ex is DomainException ? ex.Message : "Bu cihazda yerel VAD çalıştırıcısı başlatılamadı."); }
    }
    public async Task<VoiceActivity> AnalyzeAsync(AnalyzedMedia media, int stream, Action<int>? progress, CancellationToken token)
    {
        if (!tools.FfmpegAvailable || media.Probe.Duration is not > 0 or > 14400 || media.Probe.Streams?.Any(item => item.Index == stream && item.Kind == "audio") != true)
            throw new DomainException("VAD için en fazla dört saatlik, ses izi doğrulanmış medya gerekli.", 409);
        using var state = new State(_session.Value, token);
        using var process = MediaProcess.Start(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "2", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
            "-i", media.Path, "-map", "0:" + stream, "-vn", "-sn", "-dn", "-ac", "1", "-ar", "16000", "-t", "14401", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1"]);
        var errors = MediaProcess.ReadErrorTailAsync(process.StandardError);
        var probabilities = new List<float>(); var bytes = new byte[1024]; var samples = new float[512]; long totalSamples = 0; var lastProgress = -1;
        try
        {
            await using var output = process.StandardOutput.BaseStream;
            while (true)
            {
                token.ThrowIfCancellationRequested(); var read = 0;
                while (read < bytes.Length) { var count = await output.ReadAsync(bytes.AsMemory(read), token); if (count == 0) break; read += count; }
                if (read == 0) break;
                if (read % 2 != 0) throw new DomainException("Ses çözümleme örnekleri eksik.", 422);
                Array.Clear(samples); for (var index = 0; index < read / 2; index++) samples[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * 2, 2)) / 32768f;
                totalSamples += read / 2;
                if (totalSamples > 14400L * 16000) throw new DomainException("Ses çözümleme süre sınırı aşıldı.", 413);
                probabilities.Add(state.Infer(samples));
                var current = Math.Min(95, (int)(totalSamples / 16000d / media.Probe.Duration.Value * 95));
                if (current > lastProgress) { progress?.Invoke(current); lastProgress = current; }
                if (read < bytes.Length) break;
            }
            await process.WaitForExitAsync(token); await errors;
            if (process.ExitCode != 0 || probabilities.Count == 0) throw new DomainException("Ses etkinliği için ses çözülemedi.", 422);
            return new(probabilities.ToArray(), 512d / 16000, totalSamples / 16000d);
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        finally { MediaProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); await errors; }
    }
    private sealed class State : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly float[] _input = new float[576]; private readonly float[] _state = new float[256];
        private readonly OrtValue _inputValue; private readonly OrtValue _stateValue; private readonly OrtValue _rateValue;
        private readonly RunOptions _options = new(); private readonly CancellationTokenRegistration _cancellation;
        public State(InferenceSession session, CancellationToken token)
        {
            _session = session;
            _inputValue = OrtValue.CreateTensorValueFromMemory(_input, [1, 576]);
            _stateValue = OrtValue.CreateTensorValueFromMemory(_state, [2, 1, 128]);
            _rateValue = OrtValue.CreateTensorValueFromMemory(new long[] { 16000 }, []);
            _cancellation = token.Register(() => _options.Terminate = true);
        }
        public float Infer(float[] samples)
        {
            samples.CopyTo(_input, 64);
            using var output = _session.Run(_options, new[] { "input", "state", "sr" }, new[] { _inputValue, _stateValue, _rateValue }, _session.OutputNames);
            var probability = output[0].GetTensorDataAsSpan<float>()[0];
            output[1].GetTensorDataAsSpan<float>().CopyTo(_state);
            Array.Copy(_input, 512, _input, 0, 64);
            if (!float.IsFinite(probability) || probability is < 0 or > 1) throw new DomainException("VAD çıktısı geçersiz.", 422);
            return probability;
        }
        public void Dispose() { _cancellation.Dispose(); _inputValue.Dispose(); _stateValue.Dispose(); _rateValue.Dispose(); _options.Dispose(); }
    }
    public void Dispose() { if (_session.IsValueCreated) _session.Value.Dispose(); }
}
