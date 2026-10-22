using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public sealed class MusicSonicAnalyzer(MusicPcmCache pcm, MediaAnalysisService analysis, MediaTools tools)
{
    public async Task<MusicSonicAnalysis> AnalyzeAsync(string id, CancellationToken token)
    {
        var prepared = await pcm.PrepareAsync(id, token); var media = await analysis.InspectAsync(id, token);
        var spectrum = await SpectrumAsync(id, prepared, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = MediaProcess.Start(tools.FfmpegExecutable, ["-hide_banner", "-nostdin", "-threads", "2", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
            "-i", media.Path, "-map", "0:a:0", "-vn", "-sn", "-af", "loudnorm=I=-18:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"]);
        var error = MediaProcess.ReadErrorTailAsync(process.StandardError); var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        string report;
        try
        {
            await process.WaitForExitAsync(timeout.Token); report = await error; await output;
            if (process.ExitCode != 0) throw new DomainException("Ses loudness analizi tamamlanamadı.", 422);
        }
        finally { MediaProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); await error; try { await output; } catch (OperationCanceledException) { } }
        var begin = report.LastIndexOf('{'); var end = report.LastIndexOf('}');
        if (begin < 0 || end < begin) throw new DomainException("FFmpeg loudness ölçümü eksik.", 422);
        using var json = JsonDocument.Parse(report[begin..(end + 1)]);
        double? Value(string name) => json.RootElement.TryGetProperty(name, out var field) && double.TryParse(field.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
        return new(id, prepared.Fingerprint, 1, Value("input_i"), Value("input_tp"), spectrum.Centroid, spectrum.Crossing, spectrum.Bands, DateTimeOffset.UtcNow.ToString("O"));
    }
    private async Task<(double Centroid, double Crossing, double[] Bands)> SpectrumAsync(string id, MusicPcmInfo info, CancellationToken token)
    {
        const int size = 2048; const int bandsCount = 16;
        await using var file = pcm.Open(id, info.Fingerprint);
        var bytes = new byte[size * 8]; var bins = new Complex[size]; var bands = new double[bandsCount]; var centroid = 0d; var crossing = 0d; var windows = 0;
        var step = Math.Max(size, info.Frames / 1200);
        for (long offset = 0; offset + size <= info.Frames; offset += step)
        {
            token.ThrowIfCancellationRequested(); file.Position = offset * 8; await file.ReadExactlyAsync(bytes, token);
            double last = 0, crosses = 0;
            for (var index = 0; index < size; index++)
            {
                var value = (BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * 8, 4)) + BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * 8 + 4, 4))) * .5;
                if (!double.IsFinite(value)) throw new DomainException("Ses örnekleri geçersiz.", 422);
                if (index > 0 && value * last < 0) crosses++;
                last = value; bins[index] = new Complex(value * (.5 - .5 * Math.Cos(2 * Math.PI * index / (size - 1))), 0);
            }
            Fft(bins); double energy = 0, moment = 0; var values = new double[bandsCount];
            for (var index = 1; index <= size / 2; index++)
            {
                var frequency = index * (double)MusicPcmCache.SampleRate / size; var power = bins[index].Magnitude * bins[index].Magnitude;
                var band = Math.Clamp((int)(Math.Log2(Math.Max(30, frequency) / 30) / Math.Log2(24000d / 30) * bandsCount), 0, bandsCount - 1);
                values[band] += power; energy += power; moment += power * frequency;
            }
            if (energy <= 1e-10) continue;
            for (var index = 0; index < bandsCount; index++) bands[index] += values[index] / energy;
            centroid += moment / energy; crossing += crosses / (size - 1); windows++;
        }
        if (windows == 0) return (0, 0, bands);
        return (centroid / windows, crossing / windows, bands.Select(value => value / windows).ToArray());
    }
    private static void Fft(Complex[] values)
    {
        for (int index = 1, reversed = 0; index < values.Length; index++)
        {
            var bit = values.Length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1) reversed ^= bit;
            reversed ^= bit; if (index < reversed) (values[index], values[reversed]) = (values[reversed], values[index]);
        }
        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var rotation = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < values.Length; start += length)
            {
                var factor = Complex.One;
                for (var index = 0; index < length / 2; index++)
                {
                    var a = values[start + index]; var b = values[start + index + length / 2] * factor;
                    values[start + index] = a + b; values[start + index + length / 2] = a - b; factor *= rotation;
                }
            }
        }
    }
    public static double Similarity(MusicSonicAnalysis a, MusicSonicAnalysis b)
    {
        if (a.Version != b.Version || a.Bands.Length != b.Bands.Length || a.Bands.Sum() <= 0 || b.Bands.Sum() <= 0) return 0;
        var overlap = a.Bands.Zip(b.Bands).Sum(pair => Math.Sqrt(Math.Max(0, pair.First * pair.Second)));
        var color = Math.Abs(Math.Log((a.CentroidHz + 100) / (b.CentroidHz + 100)));
        var texture = Math.Abs(a.ZeroCrossingRate - b.ZeroCrossingRate);
        return Math.Round(Math.Clamp(overlap * Math.Exp(-.15 * color - texture), 0, 1), 4);
    }
}
