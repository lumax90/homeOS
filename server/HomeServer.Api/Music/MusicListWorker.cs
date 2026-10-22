using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Music;

public sealed class MusicListWorker(MediaStore store, MusicBrainzProvider provider, EventHub events, ILogger<MusicListWorker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken token) { store.RecoverMusicLists(); return base.StartAsync(token); }
    public async Task<bool> RunOnceAsync(CancellationToken token)
    {
        var work = store.ClaimMusicList(); if (work is null) return false;
        try
        {
            store.RequireMusicListWork(work);
            if (work.Job.Source == "collection")
            {
                if (work.EntityType is null) store.SetMusicListCollection(work, await provider.CollectionAsync(work.Job.SourceId, token));
                else
                {
                    var page = await provider.CollectionPageAsync(new(work.Job.SourceId, work.Job.Name, work.EntityType), work.Job.Kind, work.Job.Cursor, token);
                    var next = page.Offset + page.EntityIds.Length; store.SaveMusicListStep(work, page.Items, page.EntityIds, page.Total, next, complete: next == page.Total);
                }
            }
            else
            {
                MusicListCandidate[] items = [];
                if (work.Job.Cursor < work.Seeds.Length)
                {
                    var id = work.Seeds[work.Job.Cursor];
                    if (work.Job.Kind == "artist") { var artist = await provider.ArtistAsync(id, token); items = [new("artist", id, artist.Name, Artist: artist)]; }
                    else { var album = await provider.AlbumAsync(id, token); items = [new("album", id, album.Title, Album: album)]; }
                }
                var next = Math.Min(work.Job.Cursor + 1, work.Seeds.Length); store.SaveMusicListStep(work, items, items.Select(item => item.Mbid).ToArray(), work.Seeds.Length, next, complete: next == work.Seeds.Length);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            store.FailMusicList(work, ex is DomainException domain ? domain.Message : ex is OperationCanceledException ? "Liste işi veya hesap değişti." : "Müzik listesi sağlayıcıdan okunamadı.", ex is DomainException { StatusCode: 429 or 502 or 504 });
        }
        finally { events.Publish("music.lists.changed"); }
        return true;
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var ran = await RunOnceAsync(token); await Task.Delay(ran ? 1100 : 3000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Müzik listesi işi: {Type}", ex.GetType().Name); try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; } }
        }
    }
}
