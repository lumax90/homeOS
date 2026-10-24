using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace HomeServer.Playback;

public sealed partial class PlaybackManager(HomePaths paths, MediaStore store, MediaAnalysisService analysis, MediaTools tools,
    IConfiguration configuration, ILogger<PlaybackManager> logger, EncodingSupport? encoding = null, SubtitleService? subtitles = null) : BackgroundService
{
    private sealed record ProcessMarker(int Id, long StartedAt, string Executable);
    private sealed record ClientIntent(long Revision, bool Closed);
    private sealed class Active(string owner, string clientId, long revision, string directory, PlaybackSession info, Process process, long reservation, double ahead)
    {
        public string Owner { get; } = owner;
        public string ClientId { get; } = clientId;
        public long Revision { get; } = revision;
        public string Directory { get; } = directory;
        public PlaybackLifecycle Lifecycle { get; } = new(info);
        public PlaybackSession Info => Lifecycle.Current;
        public Process Process { get; } = process;
        public Task Work { get; set; } = Task.CompletedTask;
        public long LastSeen = DateTimeOffset.UtcNow.UtcTicks;
        public long Bytes;
        public bool Closing;
        public long Reservation { get; } = reservation;
        public PlaybackWindow Window { get; } = new(directory, reservation, info.StartSeconds, info.RequestedStartSeconds, aheadSeconds: ahead);
        public MediaProcessSuspension Production { get; } = new(process);
        public object Gate { get; } = new();
        public FileSystemWatcher? Changes;
        public int ControlPending;
    }
    private readonly ConcurrentDictionary<string, Active> _sessions = new();
    private readonly SemaphoreSlim _starts = new(1, 1);
    private readonly MemoryCache _intents = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly int _maximum = Math.Clamp(configuration.GetValue("Home:Playback:MaximumSessions", 2), 1, 8);
    private readonly long _budget = (long)(Math.Clamp(configuration.GetValue("Home:Playback:CacheGiBPerSession", 8d), .1, 64) * 1024 * 1024 * 1024);
    private readonly double _ahead = Math.Clamp(configuration.GetValue("Home:Playback:AheadSeconds", 90d), 12, 120);
    private string Root => AppFiles.DirectoryIn(paths.Data, "playback");

    public async Task<PlaybackSession> CreateAsync(string owner, PlaybackStart input, CancellationToken token)
    {
        var media = await analysis.InspectAsync(input.MediaId, token, allowUnprobed: true);
        var clientId = input.ClientId is null ? Guid.NewGuid().ToString("N") : AppFiles.Id(input.ClientId);
        if (input.ClientRevision < 0) throw new DomainException("Oynatma revizyonu geçersiz.");
        var selectedSubtitle = input.SubtitleId is null ? null : subtitles?.GetSource(media, input.SubtitleId).Track ?? throw new DomainException("Altyazı oynatma hizmeti kullanılamıyor.", 409);
        var burn = selectedSubtitle is not null && (selectedSubtitle.Image || input.BurnSubtitle);
        var plan = PlaybackPlanner.Decide(media, input, tools.FfmpegAvailable, _budget, toneMappingAvailable: true, selectedSubtitle);
        if (plan.ToneMap && (encoding is null || !(await encoding.InspectAsync(token)).ToneMapping))
            throw new DomainException("HDR renk bilgisi veya tone mapping filtresi bu profilde uygun değil.", 409);
        await _starts.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var key = owner + ":" + clientId;
            if (input.ClientRevision > 0 && _intents.TryGetValue<ClientIntent>(key, out var intent)
                && (intent!.Revision > input.ClientRevision || intent.Revision == input.ClientRevision && intent.Closed))
                throw new DomainException("Daha yeni oynatma komutu zaten uygulandı.", 409);
            if (input.ClientRevision > 0 && _sessions.Values.Any(session => session.Owner == owner && session.ClientId == clientId && session.Revision > input.ClientRevision))
                throw new DomainException("Daha yeni oynatma komutu zaten uygulandı.", 409);
            _intents.Set(key, new ClientIntent(input.ClientRevision, false), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
            foreach (var (oldId, _) in _sessions.Where(pair => pair.Value.Owner == owner && pair.Value.ClientId == clientId).ToArray())
                await CloseInternalAsync(oldId, owner);
            if (input.ReplaceSessionId is { } previous) await CloseInternalAsync(previous, owner);
            if (plan.Mode == "direct") return new PlaybackSession(null, input.MediaId, "direct", $"/api/media/{input.MediaId}/stream", 0,
                media.Probe.Duration ?? 0, plan.Reasons, plan.AudioStream, "ready", RequestedStartSeconds: input.StartSeconds, SubtitleId: input.SubtitleId,
                VideoCodec: plan.VideoCodec, AudioCodec: plan.AudioCodec, Container: plan.Container, Transport: "direct");
            if (_sessions.Count >= _maximum) throw new DomainException($"Sunucuda {_maximum} dönüştürme oturumu açık. Kullanılmayan oynatıcıyı kapat.", 429);
            var start = plan.CopyVideo && plan.VideoStream is { } video
                ? await tools.KeyframeBeforeAsync(media, video, input.StartSeconds, token) : input.StartSeconds;
            var duration = media.Probe.Duration!.Value - start;
            var rate = plan.VideoStream is null && !plan.CopyAudio ? 192000
                : plan.CopyVideo ? media.Probe.BitRate ?? (long)(media.Media.SizeBytes * 8 / media.Probe.Duration.Value) : plan.Bitrate;
            // Reserve a rolling production/back-buffer window, not the remaining size of the movie.
            var reserved = Math.Min(_budget, (long)(Math.Max(256000, rate) / 8d * Math.Min(duration, 300) * 1.25) + 16L * 1024 * 1024);
            var root = Root;
            var future = _sessions.Values.Sum(session => Math.Max(0, session.Reservation - Interlocked.Read(ref session.Bytes)));
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < reserved + future + 256L * 1024 * 1024)
                throw new DomainException("Dönüştürme için yeterli geçici disk alanı yok.", 409);
            var encoder = !plan.CopyVideo && encoding is not null ? await encoding.EncoderAsync(token) : "libx264";
            for (var attempt = 0; attempt < 2; attempt++)
            {
            var id = Guid.NewGuid().ToString("N");
            var directory = AppFiles.DirectoryIn(root, id);
            Active? active = null;
            try
            {
                await File.WriteAllTextAsync(Path.Combine(directory, ".home-playback"), "Home.Playback.v1", token);
                if (burn) plan = plan with { Subtitle = await subtitles!.PrepareBurnAsync(media, input.SubtitleId!, directory, input.SubtitleDelaySeconds, token) };
                token.ThrowIfCancellationRequested();
                var process = MediaProcess.Start(tools.FfmpegExecutable, Arguments(media.Path, directory, start, plan, encoder), true, directory);
                var info = new PlaybackSession(id, input.MediaId, plan.Mode, $"/api/playback/{id}/index.m3u8", start,
                    media.Probe.Duration.Value, plan.Reasons, plan.AudioStream, "starting", RequestedStartSeconds: input.StartSeconds, Encoder: plan.CopyVideo ? "copy" : encoder, SubtitleId: input.SubtitleId, SubtitleBurned: burn,
                    VideoCodec: plan.VideoCodec, AudioCodec: plan.AudioCodec, Container: plan.Container, Transport: plan.Transport);
                active = new Active(owner, clientId, input.ClientRevision, directory, info, process, reserved, _ahead);
                _sessions[id] = active;
                active.Work = WatchAsync(active);
                WatchProduction(active);
                await File.WriteAllTextAsync(Path.Combine(directory, ".home-process.json"), JsonSerializer.Serialize(
                    new ProcessMarker(process.Id, process.StartTime.ToUniversalTime().Ticks, tools.FfmpegExecutable)), token);
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
                startup.CancelAfter(TimeSpan.FromSeconds(35));
                while (!active.Window.Ready)
                {
                    if (active.Work.IsCompleted) throw new DomainException(active.Info.Error ?? "HLS akışı oluşturulamadı.", 422);
                    await Task.Delay(100, startup.Token);
                }
                if (active.Info.State == "failed") throw new DomainException(active.Info.Error!, 422);
                active.Lifecycle.Ready();
                return Describe(active);
            }
            catch
            {
                if (active is not null) await CloseInternalAsync(id, owner);
                else if (Directory.Exists(directory)) AppFiles.DeleteOwnedDirectory(directory);
                if (encoder == "h264_nvenc" && !token.IsCancellationRequested)
                {
                    encoding?.HardwareFailed(); encoder = "libx264";
                    plan = plan with { Reasons = [..plan.Reasons, "GPU kodlayıcı başlatılamadı; yazılım kodlayıcısına geçildi."] };
                    continue;
                }
                throw;
            }
            }
            throw new DomainException("Oynatma kodlayıcısı başlatılamadı.", 422);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Oynatma hazırlığı zaman aşımına uğradı.", 504); }
        finally { _starts.Release(); }
    }

    public PlaybackSession Touch(string id, string owner, PlaybackHeartbeat? heartbeat = null)
    {
        var session = Get(id, owner);
        if (heartbeat is not null && (!double.IsFinite(heartbeat.PositionSeconds) || heartbeat.PositionSeconds < 0 || heartbeat.PositionSeconds > session.Info.DurationSeconds + 5))
            throw new DomainException("Oynatma konumu geçersiz.");
        Interlocked.Exchange(ref session.LastSeen, DateTimeOffset.UtcNow.UtcTicks);
        if (heartbeat is not null) session.Window.Touch(heartbeat.PositionSeconds);
        return Describe(session);
    }
    public PlaybackSession Describe(string id, string owner) => Describe(Get(id, owner));
    private static PlaybackSession Describe(Active session) => session.Info with { Telemetry = session.Window.Telemetry(session.Production.Paused) };
    public async Task CloseOwnerAsync(string owner)
    {
        await _starts.WaitAsync();
        try { foreach (var (id, _) in _sessions.Where(pair => pair.Value.Owner == owner).ToArray()) await CloseInternalAsync(id, owner); }
        finally { _starts.Release(); }
    }

    public (Stream Stream, string Mime) OpenArtifact(string id, string name, string owner)
    {
        var session = Get(id, owner);
        lock (session.Gate)
        {
            if (session.Closing) throw new DomainException("Oynatma oturumu sona erdi. Yeniden oynat.", 410);
            if (!Artifact().IsMatch(name)) throw new DomainException("Oynatma parçası bulunamadı.", 404);
            if (store.GetMedia(session.Info.MediaId) is null) throw new DomainException("Medya artık bu kütüphanede etkin değil.", 404);
            if (session.Info.State == "failed") throw new DomainException(session.Info.Error ?? "Dönüştürme durdu.", 422);
            Interlocked.Exchange(ref session.LastSeen, DateTimeOffset.UtcNow.UtcTicks);
            if (name == "index.m3u8") return (session.Window.Playlist(), "application/vnd.apple.mpegurl");
            session.Window.RequireArtifact(name);
            var full = Path.Combine(session.Directory, name);
            if (!File.Exists(full)) throw new DomainException("Oynatma parçası henüz hazır değil.", 404);
            full = MediaPaths.ResolveFile(session.Directory, name);
            return (new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous),
                name.EndsWith(".ts", StringComparison.Ordinal) ? "video/mp2t" : "video/mp4");
        }
    }

    private Active Get(string id, string owner)
    {
        _ = AppFiles.Id(id);
        if (!_sessions.TryGetValue(id, out var session)) throw new DomainException("Oynatma oturumu sona erdi. Yeniden oynat.", 410);
        if (session.Owner != owner) throw new DomainException("Bu oynatma oturumuna erişilemiyor.", 403);
        return session;
    }

    public async Task CloseAsync(string id, string owner)
    {
        await _starts.WaitAsync();
        try { await CloseInternalAsync(id, owner); }
        finally { _starts.Release(); }
    }

    public async Task CloseClientAsync(string clientId, string owner, long? revision = null)
    {
        _ = AppFiles.Id(clientId);
        await _starts.WaitAsync();
        try
        {
            var key = owner + ":" + clientId;
            _intents.TryGetValue<ClientIntent>(key, out var intent);
            if (revision is null || intent is null || revision >= intent.Revision)
                _intents.Set(key, new ClientIntent(revision ?? intent?.Revision ?? 0, true), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
            foreach (var (id, _) in _sessions.Where(pair => pair.Value.Owner == owner && pair.Value.ClientId == clientId && (revision is null || pair.Value.Revision <= revision)).ToArray())
                await CloseInternalAsync(id, owner);
        }
        finally { _starts.Release(); }
    }

    private async Task CloseInternalAsync(string id, string owner)
    {
        _ = AppFiles.Id(id);
        if (!_sessions.TryGetValue(id, out var existing)) return;
        if (existing.Owner != owner) throw new DomainException("Bu oynatma oturumu sana ait değil.", 403);
        if (!_sessions.TryRemove(id, out var session)) return;
        lock (session.Gate) { session.Closing = true; session.Changes?.Dispose(); MediaProcess.Kill(session.Process); }
        await session.Work;
        session.Process.Dispose();
        try { if (Directory.Exists(session.Directory)) AppFiles.DeleteOwnedDirectory(session.Directory); }
        catch (Exception ex) when (ex is IOException or DomainException) { logger.LogWarning("Oynatma cache'i temizlenemedi: {Id} ({Type})", id, ex.GetType().Name); }
    }

    private async Task WatchAsync(Active session)
    {
        try
        {
            var errors = MediaProcess.ReadErrorTailAsync(session.Process.StandardError);
            var progress = ProgressAsync(session);
            await session.Process.WaitForExitAsync();
            await progress;
            var error = await errors;
            if (session.Closing) session.Lifecycle.Close();
            else if (session.Process.ExitCode == 0) session.Lifecycle.Complete();
            else
            {
                if (session.Info.Encoder == "h264_nvenc") encoding?.HardwareFailed();
                logger.LogWarning("FFmpeg oynatma {Id} kod {ExitCode}: {Error}", session.Info.Id, session.Process.ExitCode, error);
                session.Lifecycle.Fail("FFmpeg akışı tamamlayamadı. Farklı kalite veya ses iziyle yeniden dene.");
            }
        }
        catch (Exception ex)
        {
            MediaProcess.Kill(session.Process);
            session.Lifecycle.Fail("Oynatma işlemi izlenemedi. Yeniden dene.");
            logger.LogWarning("Oynatma işlemi {Id}: {Type}", session.Info.Id, ex.GetType().Name);
        }
    }

    private static async Task ProgressAsync(Active session)
    {
        double? seconds = null, speed = null;
        while (await session.Process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && double.TryParse(line.AsSpan(12), CultureInfo.InvariantCulture, out var micros)) seconds = micros / 1_000_000;
            if (line.StartsWith("speed=", StringComparison.Ordinal) && double.TryParse(line[6..].Trim().TrimEnd('x'), CultureInfo.InvariantCulture, out var rate)) speed = rate;
            if (line.StartsWith("progress=", StringComparison.Ordinal)) session.Window.Progress(seconds, speed);
        }
    }

    private static void WatchProduction(Active session)
    {
        var watcher = new FileSystemWatcher(session.Directory, "*.m3u8") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        void Changed(object sender, FileSystemEventArgs change)
        {
            if (Interlocked.Exchange(ref session.ControlPending, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(_ => { try { ControlProduction(session); } finally { Volatile.Write(ref session.ControlPending, 0); } });
        }
        watcher.Changed += Changed; watcher.Created += Changed; watcher.Renamed += Changed;
        session.Changes = watcher; watcher.EnableRaisingEvents = true;
    }
    private static void ControlProduction(Active session)
    {
        lock (session.Gate)
        {
            if (session.Closing || session.Info.State == "failed") return;
            try
            {
                var paused = session.Window.Control(session.Production.Paused);
                if (!session.Process.HasExited) session.Production.Set(paused);
                Interlocked.Exchange(ref session.Bytes, session.Window.Telemetry(paused).CacheBytes);
            }
            catch (Exception ex) when (ex is IOException or DomainException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            { MediaProcess.Kill(session.Process); session.Lifecycle.Fail(ex is DomainException ? ex.Message : "Akışın geçici üretimi yönetilemedi. Kaldığın yerden yeniden dene."); }
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var marker = Path.Combine(directory, ".home-playback");
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || !File.Exists(marker)) continue;
            AppFiles.RejectLink(directory);
            AppFiles.RejectLink(marker);
            if (await File.ReadAllTextAsync(marker, cancellationToken) == "Home.Playback.v1")
            {
                var processFile = Path.Combine(directory, ".home-process.json");
                if (File.Exists(processFile))
                {
                    AppFiles.RejectLink(processFile);
                    ProcessMarker? previous = null;
                    try { previous = JsonSerializer.Deserialize<ProcessMarker>(await File.ReadAllTextAsync(processFile, cancellationToken)); }
                    catch (JsonException) { logger.LogWarning("Kesilmiş oynatma süreç kaydı: {Id}", Path.GetFileName(directory)); }
                    if (previous is not null)
                    {
                        try
                        {
                            using var process = Process.GetProcessById(previous.Id);
                            if (process.StartTime.ToUniversalTime().Ticks == previous.StartedAt
                                && string.Equals(process.MainModule?.FileName, previous.Executable, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                            {
                                MediaProcess.Kill(process);
                                await process.WaitForExitAsync(cancellationToken);
                            }
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    }
                }
                AppFiles.DeleteOwnedDirectory(directory);
            }
        }
        var subtitleWork = AppFiles.DirectoryIn(paths.Data, "subtitle-work");
        foreach (var directory in Directory.EnumerateDirectories(subtitleWork))
        {
            var marker = Path.Combine(directory, ".home-subtitle");
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || !File.Exists(marker)) continue;
            AppFiles.RejectLink(directory);
            AppFiles.RejectLink(marker);
            if (await File.ReadAllTextAsync(marker, cancellationToken) == "Home.Subtitle.v1") AppFiles.DeleteOwnedDirectory(directory);
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var (id, session) in _sessions)
                {
                    var idle = DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref session.LastSeen) > TimeSpan.FromMinutes(2).Ticks;
                    if (idle) { await CloseAsync(id, session.Owner); continue; }
                    ControlProduction(session);
                }
                await Task.Delay(250, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { foreach (var (id, session) in _sessions.ToArray()) await CloseAsync(id, session.Owner); }
    }

    public static string[] Arguments(string input, string directory, double start, PlaybackDecision plan, string encoder = "libx264")
    {
        var result = EncodingArguments(input, start, plan, encoder);
        result.InsertRange(result.IndexOf("-i"), ["-readrate", "4", "-readrate_initial_burst", "4"]);
        result.AddRange(["-progress", "pipe:1", "-stats_period", "0.25", "-f", "hls", "-hls_time", "4", "-hls_list_size", "128", "-hls_flags", "independent_segments+temp_file"]);
        if (plan.Container == "mp4") result.AddRange(["-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4"]);
        result.AddRange(["-hls_segment_filename", Path.Combine(directory, plan.Container == "mp4" ? "segment%06d.m4s" : "segment%06d.ts"), Path.Combine(directory, "index.m3u8")]);
        return result.ToArray();
    }
    public static List<string> EncodingArguments(string input, double start, PlaybackDecision plan, string encoder = "libx264")
    {
        var result = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-nostdin", "-threads", "2",
            "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats,
            "-ss", start.ToString("0.######", CultureInfo.InvariantCulture), "-i", input, "-map_metadata", "-1", "-map_chapters", "-1", "-sn", "-dn" };
        if (plan.VideoStream is { } video)
        {
            var filter = (plan.ToneMap ? EncodingSupport.ToneMapFilter + "," : "") + $"scale=-2:'min(ih,{plan.Height ?? 1080})'";
            if (plan.Subtitle is { Image: true, Stream: { } subtitleStream } bitmap)
            {
                var tone = plan.ToneMap ? EncodingSupport.ToneMapFilter : "null";
                var delay = bitmap.DelaySeconds.ToString("0.######", CultureInfo.InvariantCulture);
                result.AddRange(["-filter_complex_threads", "1", "-filter_complex", $"[0:{video}]{tone}[base];[0:{subtitleStream}]setpts=PTS+({delay})/TB,scale={bitmap.Width}:{bitmap.Height}[sub];[base][sub]overlay=eof_action=pass:shortest=0,scale=-2:'min(ih,{plan.Height ?? 1080})'[burned]", "-map", "[burned]"]);
            }
            else
            {
                result.AddRange(["-map", "0:" + video]);
                if (plan.Subtitle is { Image: false, TextFile: "captions.ass" } text)
                {
                    var time = (start - text.DelaySeconds).ToString("0.######", CultureInfo.InvariantCulture);
                    filter = (plan.ToneMap ? EncodingSupport.ToneMapFilter + "," : "") + $"setpts=PTS+({time})/TB,subtitles=filename=captions.ass,setpts=PTS-({time})/TB,scale=-2:'min(ih,{plan.Height ?? 1080})'";
                }
                if (!plan.CopyVideo) result.AddRange(["-vf", filter]);
            }
            if (plan.CopyVideo) { result.AddRange(["-c:v", "copy"]); if (plan.Container == "mp4" && plan.VideoCodec == "hevc") result.AddRange(["-tag:v", "hvc1"]); }
            else result.AddRange(["-c:v", encoder, "-preset", encoder == "h264_nvenc" ? "p4" : "veryfast", "-pix_fmt", "yuv420p", "-threads", "2", "-filter_threads", "1",
                "-b:v", Math.Max(350000, plan.Bitrate - 192000).ToString(CultureInfo.InvariantCulture),
                "-maxrate", Math.Max(350000, plan.Bitrate - 192000).ToString(CultureInfo.InvariantCulture), "-bufsize", (plan.Bitrate * 2L).ToString(CultureInfo.InvariantCulture),
                "-force_key_frames", "expr:gte(t,n_forced*4)", "-flags", "+cgop", "-fpsmax", "60", "-profile:v", "high"]);
            if (!plan.CopyVideo && encoder == "h264_nvenc") result.AddRange(["-forced-idr", "1"]);
            if (plan.ToneMap) result.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        }
        if (plan.AudioStream is { } audio)
        {
            result.AddRange(["-map", "0:" + audio]);
            result.AddRange(plan.CopyAudio ? ["-c:a", "copy"] : ["-c:a", "aac", "-ac", "2", "-b:a", "192k", "-ar", "48000"]);
        }
        result.AddRange(["-avoid_negative_ts", "make_zero", "-max_muxing_queue_size", "1024"]);
        return result;
    }

    [GeneratedRegex(@"^(?:index\.m3u8|init\.mp4|segment\d{6}\.(?:ts|m4s))$", RegexOptions.NonBacktracking)] private static partial Regex Artifact();
    public override void Dispose() { _intents.Dispose(); base.Dispose(); }
}
