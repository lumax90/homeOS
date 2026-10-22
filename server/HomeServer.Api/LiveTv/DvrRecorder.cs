using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;

public sealed class DvrRecorder(MediaStore store, LiveSources sources, LiveInputRelay relay, HomePaths paths, MediaTools tools, DvrPublisher publisher, EventHub events, ILogger<DvrRecorder> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Task> _active = new();
    public override Task StartAsync(CancellationToken token) { store.RecoverDvr(); return base.StartAsync(token); }
    private async Task RecordAsync(DvrRecording work, CancellationToken stopping)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var root = AppFiles.DirectoryIn(paths.Data, "downloads", "staging", "dvr", work.Id);
        var watcher = WatchAsync();
        try
        {
            await OwnedNativeProcess.RecoverAsync(root, cancel.Token);
            if (work.Status == "publishing") { await publisher.PublishAsync(work, cancel.Token); return; }
            _ = store.RequireDvr(work);
            await File.WriteAllTextAsync(Path.Combine(root, ".home-dvr"), "Home.Dvr.v1", cancel.Token);
            var end = DateTimeOffset.Parse(work.EndsAt);
            if (work.Status == "recording")
            {
                while (end > DateTimeOffset.UtcNow)
                {
                    var room = 20L * 1024 * 1024 * 1024 - Parts(root).Sum(path => new FileInfo(path).Length);
                    if (room <= 0) break;
                    _ = store.RequireDvr(work); var attempt = store.NextDvrCaptureAttempt(work); if (attempt == 0) break;
                    var channel = store.GetLiveChannel(work.ChannelId); var input = sources.Input(channel.SourceId);
                    var next = Parts(root).Select(path => int.Parse(Path.GetFileNameWithoutExtension(path)[4..], CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max() + 1;
                    if (next > 9999) throw new DomainException("Kayıt parça sınırı aşıldı.", 409);
                    var part = Path.Combine(root, $"part{next:0000}.ts");
                    var remaining = end - DateTimeOffset.UtcNow;
                    var link = relay.Register(sources.ChannelUrl(channel.Id), input.AllowLan, () => store.RequireDvr(work),sources.ChannelHeaders(channel.Id));
                    try
                    {
                        await OwnedNativeProcess.RunAsync(root, tools.FfmpegExecutable,
                            ["-hide_banner", "-loglevel", "error", "-nostdin", "-re", "-protocol_whitelist", "http,https,tcp,tls,crypto", "-i", link.Url,
                             "-t", Math.Max(1, Math.Ceiling(remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture), "-map", "0:v:0?", "-map", "0:a:0?", "-sn", "-c", "copy", "-f", "mpegts", "-fs", room.ToString(CultureInfo.InvariantCulture), part],
                            remaining + TimeSpan.FromMinutes(2), cancel.Token);
                    }
                    catch (DomainException) when (!cancel.IsCancellationRequested) { _ = store.RequireDvr(work); }
                    finally { relay.Remove(link.Token); }
                    if (DateTimeOffset.UtcNow >= end.AddSeconds(-1)) break;
                    var delay = Math.Min(Math.Min(30, Math.Pow(2, attempt)), Math.Max(0, (end - DateTimeOffset.UtcNow).TotalSeconds));
                    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), cancel.Token);
                }
                work = store.BeginDvrFinalization(work);
            }
            _ = store.RequireDvr(work);
            var captured = Parts(root).Where(path => new FileInfo(path).Length > 0).ToArray();
            var readable = new List<string>();
            foreach (var part in captured)
            {
                var metadata = await tools.ProbeAsync(part, cancel.Token);
                if (metadata?.Duration is > 0 && (metadata.VideoCodec is not null || metadata.AudioCodec is not null)) readable.Add(part);
            }
            var parts = readable.ToArray();
            if (parts.Length == 0) throw new DomainException("Kayıt zamanında veri alınamadı.", 422);
            var manifest = Path.Combine(root, "parts.txt");
            await File.WriteAllLinesAsync(manifest, parts.Select(path => "file '" + Path.GetFileName(path) + "'"), new UTF8Encoding(false), cancel.Token);
            var output = Path.Combine(root, "complete.partial.ts");
            if (File.Exists(output)) { AppFiles.RejectLink(output); File.Delete(output); }
            await OwnedNativeProcess.RunAsync(root, tools.FfmpegExecutable,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "file,pipe", "-f", "concat", "-safe", "1", "-i", manifest, "-map", "0", "-c", "copy", "-f", "mpegts", output],
                TimeSpan.FromMinutes(30), cancel.Token);
            var probe = await tools.ProbeAsync(output, cancel.Token);
            if (probe?.Duration is not > 0 || probe.VideoCodec is null && probe.AudioCodec is null) throw new DomainException("Kayıt medya bilgisi doğrulanamadı.", 422);
            await publisher.PrepareAsync(work, output, probe, parts.Length, cancel.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var message = ex is DomainException ? ex.Message : ex is OperationCanceledException ? "Kayıt iptal edildi." : "Kayıt işlemi tamamlanamadı; alınan parçalar korundu.";
            if (store.GetDvr(work.Id) is { Status: "publishing" } publishing) store.DvrPublicationError(publishing, message);
            else store.FinishDvr(work, "failed", message);
            logger.LogWarning("DVR {Id}: {Type}", work.Id, ex.GetType().Name);
        }
        finally { cancel.Cancel(); try { await watcher; } catch (OperationCanceledException) { } events.Publish("live.changed"); }
        async Task WatchAsync()
        {
            while (!cancel.IsCancellationRequested)
            {
                await Task.Delay(2000, cancel.Token);
                try
                {
                    var current = store.GetDvr(work.Id);
                    if (current?.Status is "completed" or "partial") return;
                    if (current is null || current.Revision != work.Revision || current.Lease != work.Lease) { cancel.Cancel(); return; }
                    if (current.Status == "publishing") continue;
                    _ = store.RequireDvr(work);
                    if (current.Status == "recording")
                    {
                        var size = Parts(root).Sum(path => new FileInfo(path).Length); store.DvrProgress(work, size);
                    }
                }
                catch { cancel.Cancel(); return; }
            }
        }
    }
    private static string[] Parts(string root) => Directory.EnumerateFiles(root, "part*.ts").Where(path =>
    {
        AppFiles.RejectLink(path); var name = Path.GetFileName(path); return name.Length == 11 && name[4..8].All(char.IsAsciiDigit);
    }).Order(StringComparer.Ordinal).ToArray();
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var nextRules = DateTimeOffset.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextRules) { store.ScheduleDvrRules(); nextRules = DateTimeOffset.UtcNow.AddMinutes(1); }
                foreach (var entry in _active.Where(entry => entry.Value.IsCompleted).ToArray())
                {
                    try { await entry.Value; }
                    catch (Exception ex)
                    {
                        if (store.GetDvr(entry.Key) is { } failed)
                        {
                            if (failed.Status == "publishing") store.DvrPublicationError(failed, "Kayıt yayın alanı açılamadı.");
                            else store.FinishDvr(failed, "failed", "Kayıt çalışma alanı açılamadı.");
                        }
                        logger.LogWarning("DVR çalışma alanı: {Type}", ex.GetType().Name);
                    }
                    finally { _active.TryRemove(entry.Key, out _); }
                }
                foreach (var due in store.DueDvr())
                {
                    if (_active.Count >= 4) break;
                    if (!tools.FfmpegAvailable && due.Status != "publishing") continue;
                    if (!_active.ContainsKey(due.Id) && store.ClaimDvr(due.Id) is { } work) _active[work.Id] = RecordAsync(work, token);
                }
            }
            catch (Exception ex) { logger.LogWarning("DVR planlayıcı: {Type}", ex.GetType().Name); }
            try { await Task.Delay(3000, token); } catch (OperationCanceledException) { break; }
        }
        await Task.WhenAll(_active.Values);
    }
}
