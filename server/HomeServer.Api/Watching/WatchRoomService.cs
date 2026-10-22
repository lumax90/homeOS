using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Watching;

public sealed class WatchRoomService(MediaStore store, MediaAnalysisService analysis, PlaybackSessions playback, TimeProvider clock,
    EventHub events, ILogger<WatchRoomService> logger) : BackgroundService
{
    public async Task<WatchRoomConnection> CreateAsync(PlaybackActor actor, WatchRoomCreate input)
    {
        var connection = store.CreateWatchRoom(actor, input, clock.GetUtcNow()); await ChangedAsync(); return connection;
    }
    public async Task<WatchRoomConnection> JoinAsync(string id, PlaybackActor actor, WatchRoomJoin input)
    {
        var connection = store.JoinWatchRoom(id, actor, input, clock.GetUtcNow()); await ChangedAsync(); return connection;
    }
    public async Task LeaveAsync(PlaybackActor actor, string client, WatchRoomLeave input)
    {
        store.LeaveWatchClient(actor, client, input.ClientRevision, clock.GetUtcNow()); await ChangedAsync();
    }
    public WatchRoomSnapshot Pulse(string id, string member, PlaybackActor actor, WatchRoomHeartbeat input)
    {
        var room = store.PulseWatchRoom(id, member, actor, input, clock.GetUtcNow());
        if (room.Revision != input.Revision) events.Publish("rooms.changed"); return room;
    }
    public async Task<WatchRoomCommandResult> CommandAsync(string id, PlaybackActor actor, WatchRoomCommand input, CancellationToken token)
    {
        AnalyzedMedia? selected = null;
        if (input.Action == "media")
        {
            var room = store.GetWatchRoom(id, actor, clock.GetUtcNow(), input.MemberId);
            if (!room.CanManage) throw new DomainException("Dosyayı ev sahibi seçebilir.", 403);
            if (input.MediaId is null || store.GetMedia(input.MediaId, store.GetAccount(actor.UserId)) is null) throw new DomainException("Paylaşılacak dosya bulunamadı.", 404);
            selected = await analysis.InspectAsync(input.MediaId, token);
        }
        token.ThrowIfCancellationRequested();
        var result = store.CommandWatchRoom(id, actor, input, clock.GetUtcNow(), selected); await ChangedAsync(); return result;
    }
    private async Task ChangedAsync() { events.Publish("rooms.changed"); await playback.ReconcileAsync(); }
    public override Task StartAsync(CancellationToken token) { store.ReconcileWatchRooms(clock.GetUtcNow(), restart: true); return base.StartAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (store.ReconcileWatchRooms(clock.GetUtcNow())) await ChangedAsync();
                await Task.Delay(TimeSpan.FromSeconds(1), clock, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Oda durumu uzlaştırılamadı: {Type}", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(3), clock, token); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
