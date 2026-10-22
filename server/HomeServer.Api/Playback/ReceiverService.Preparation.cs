using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed partial class ReceiverService
{
    public async Task<ProductJob> PrepareAsync(string id, Account actor, CancellationToken token)
    {
        _ = store.PlaybackSource(id, actor); var media = await analysis.InspectAsync(id, token);
        if (media.Media.Kind != "video" || media.Probe.Duration is not > 0 or > 28800) throw new DomainException("Alıcı kopyası için sekiz saatten kısa bir video seç.");
        return store.QueueProductJob("receiver-prepare", id, new { fingerprint = media.Fingerprint, matchRevision = media.Media.MatchRevision }, actor);
    }
    public async Task RunAsync(ProductJob job, CancellationToken token)
    {
        var actor = store.RequireProductJob(job); var media = await analysis.InspectAsync(job.ScopeId!, token);
        var fingerprint = job.Input.GetProperty("fingerprint").GetString(); var revision = job.Input.GetProperty("matchRevision").GetInt32();
        if (media.Fingerprint != fingerprint || media.Media.MatchRevision != revision || store.GetMedia(media.Media.Id, actor) is null) throw new DomainException("Alıcı kaynağı değişti.", 409);
        var root = AppFiles.DirectoryIn(paths.Data, "receiver-copies"); var directory = AppFiles.DirectoryIn(root, job.Id);
        await OwnedNativeProcess.RecoverAsync(directory, token);
        var marker = Path.Combine(directory, ".home-receiver"); if (File.Exists(marker)) AppFiles.RejectLink(marker);
        await File.WriteAllTextAsync(marker, "Home.ReceiverCopy.v1", token);
        var output = Path.Combine(directory, "media.partial.mp4"); var target = Path.Combine(directory, "media.mp4"); var journal = Path.Combine(directory, "publication.json");
        ReceiverCopy copy;
        if (File.Exists(journal))
        {
            AppFiles.RejectLink(journal); copy = JsonSerializer.Deserialize<ReceiverCopy>(await File.ReadAllTextAsync(journal, token), Json) ?? throw new DomainException("Alıcı yayın günlüğü okunamadı.", 409);
            if (copy.Path != target || copy.Fingerprint != fingerprint || copy.MatchRevision != revision || copy.MediaId != media.Media.Id || copy.Sha256?.Length != 64) throw new DomainException("Alıcı yayın günlüğü kaynağı değişti.", 409);
        }
        else
        {
            if (File.Exists(target)) throw new DomainException("Günlüğü olmayan alıcı kopyası korundu; yeni hazırlık isteği gönder.", 409);
            if (File.Exists(output)) { AppFiles.RejectLink(output); File.Delete(output); }
            long occupied = 0;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                AppFiles.RejectLink(folder);
                var owned = Path.Combine(folder, ".home-receiver"); var final = Path.Combine(folder, "media.mp4");
                var work = store.GetProductJob(Path.GetFileName(folder));
                if (folder != directory && File.Exists(owned) && work?.Status is not ("queued" or "running" or "applying") && !store.ReceiverCopyReferenced(final))
                {
                    AppFiles.RejectLink(owned);
                    if (File.ReadAllText(owned) == "Home.ReceiverCopy.v1") { await OwnedNativeProcess.RecoverAsync(folder, token); AppFiles.DeleteOwnedDirectory(folder); continue; }
                }
                foreach (var file in Directory.EnumerateFiles(folder, "*.mp4")) { AppFiles.RejectLink(file); occupied += new FileInfo(file).Length; }
            }
            var maximum = Math.Min(10L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024 - occupied);
            if (maximum < 64 * 1024 * 1024) throw new DomainException("Alıcı kopya alanının 20 GiB sınırı doldu.", 409);
            await OwnedNativeProcess.RunAsync(directory, tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "2", "-protocol_whitelist", "file,pipe", "-format_whitelist", MediaProcess.MediaFormats, "-i", media.Path,
                "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-crf", "22", "-pix_fmt", "yuv420p", "-vf", "scale=-2:'min(ih,1080)'", "-c:a", "aac", "-ac", "2", "-b:a", "192k", "-movflags", "+faststart", "-fs", maximum.ToString(System.Globalization.CultureInfo.InvariantCulture), output], TimeSpan.FromHours(3), token);
            var probe = await tools.ProbeAsync(output, token);
            if (probe?.VideoCodec != "h264" || probe.Duration is not > 0 || Math.Abs(probe.Duration.Value - (media.Probe.Duration ?? 0)) > 5) throw new DomainException("Alıcı kopyası eksik veya doğrulanamadı.", 422);
            copy = new(media.Media.Id, media.Fingerprint, revision, target, new FileInfo(output).Length, MediaAnalysisService.Fingerprint(new FileInfo(output)), await VerifiedFilePublication.HashAsync(output, token));
            var temporary = journal + ".partial"; if (File.Exists(temporary)) { AppFiles.RejectLink(temporary); File.Delete(temporary); }
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(copy, Json), token);
            File.Move(temporary, journal, false);
        }
        var payload = File.Exists(target) ? target : output;
        if (new FileInfo(payload).Length != copy.SizeBytes || await VerifiedFilePublication.HashAsync(payload, token) != copy.Sha256) throw new DomainException("Alıcı kopyasının boyut/hash doğrulaması değişti.", 409);
        token.ThrowIfCancellationRequested();
        store.PublishReceiverCopy(job, copy, () =>
        {
            if (!File.Exists(target)) { AppFiles.RejectLink(output); File.Move(output, target, false); }
            else AppFiles.RejectLink(target);
        });
        events.Publish("receivers.changed");
    }
}
