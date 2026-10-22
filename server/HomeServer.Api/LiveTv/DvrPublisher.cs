using System.Globalization;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;

public sealed class DvrPublisher(MediaStore store, HomePaths paths, EventHub events)
{
    public async Task<DvrRecording> PrepareAsync(DvrRecording work, string payload, ProbeResult probe, int parts, CancellationToken token)
    {
        _ = store.RequireDvr(work);
        var root = AppFiles.DirectoryIn(paths.Data, "downloads", "live-library");
        var library = store.GetLibraries(true).FirstOrDefault(value => MediaPaths.Key(value.Path) == MediaPaths.Key(root)) ?? store.AddLibrary(new("Canlı kayıtlar", root, "movies"), true);
        var title = new string(work.Title.Select(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '_' : character).Take(90).ToArray()).Trim().TrimEnd('.');
        var fileName = $"{title} {DateTimeOffset.Parse(work.StartsAt).UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}-{work.Id[..8]}.ts";
        var expected = (DateTimeOffset.Parse(work.EndsAt) - DateTimeOffset.Parse(work.StartsAt)).TotalSeconds;
        var plan = new DvrPublication(work.Id, library.Id, store.GetLibraryConfiguration(library.Id).Revision, Guid.NewGuid().ToString("N"), fileName, Path.GetFileName(payload), new FileInfo(payload).Length,
            await VerifiedFilePublication.HashAsync(payload, token), probe, parts > 1 || probe.Duration < expected - 15);
        while (!store.BeginDvrPublication(work, plan)) { await Task.Delay(500, token); _ = store.RequireDvr(work); }
        return await PublishAsync(store.GetDvr(work.Id)!, token);
    }
    public async Task<DvrRecording> PublishAsync(DvrRecording work, CancellationToken token)
    {
        var plan = store.GetDvrPublication(work.Id) ?? throw new DomainException("Kayıt yayın günlüğü bulunamadı.", 409);
        _ = VerifiedFilePublication.FileName(plan.PayloadName);
        var staging = AppFiles.DirectoryIn(paths.Data, "downloads", "staging", "dvr", work.Id);
        var library = store.GetLibraries(true).FirstOrDefault(value => value.Id == plan.LibraryId) ?? throw new DomainException("Kayıt kütüphanesi bulunamadı.", 404);
        var expectedRoot = AppFiles.DirectoryIn(paths.Data, "downloads", "live-library");
        if (MediaPaths.Key(expectedRoot) != MediaPaths.Key(library.Path)) throw new DomainException("Kayıt yayın kökü değişti.", 409);
        await VerifiedFilePublication.StageAsync(Path.Combine(staging, plan.PayloadName), expectedRoot, plan.FileName, work.Id, plan.Length, plan.Sha256, token);
        token.ThrowIfCancellationRequested();
        var result = store.CommitDvrPublication(work, () => VerifiedFilePublication.Promote(expectedRoot, plan.FileName, work.Id, plan.Length, plan.Sha256));
        events.Publish("catalog.changed"); events.Publish("live.changed");
        try
        {
            var temporary = Path.Combine(expectedRoot, ".home-publish-" + work.Id + ".partial");
            if (File.Exists(temporary)) { AppFiles.RejectLink(temporary); File.Delete(temporary); }
            var marker = Path.Combine(staging, ".home-dvr");
            if (File.Exists(marker) && File.ReadAllText(marker) == "Home.Dvr.v1") AppFiles.DeleteOwnedDirectory(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The completed publication is authoritative; maintenance can clear owned leftovers. */ }
        return result;
    }
}
