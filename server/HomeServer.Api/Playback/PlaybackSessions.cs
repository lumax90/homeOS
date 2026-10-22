using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace HomeServer.Playback;

/// <summary>One authenticated lease covers direct files, HLS or a bounded gapless music queue.</summary>
public sealed class PlaybackSessions(MediaStore store, PlaybackManager processes, MusicPcmCache pcm, TimeProvider clock, EventHub events, ILogger<PlaybackSessions> logger) : BackgroundService
{
    private sealed class Live
    {
        public CancellationTokenSource Lifetime { get; }
        public CancellationToken Token { get; }
        public Task<PlaybackSession>? Startup;
        public object Gate { get; } = new();
        public Task? Cleanup;
        public int Requests;
        public Live(CancellationToken shutdown) { Lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown); Token = Lifetime.Token; }
    }
    private readonly ConcurrentDictionary<string, Live> _live = new();
    private readonly SemaphoreSlim _admissions = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<PlaybackSession> StartPlaybackAsync(PlaybackActor actor, PlaybackStart input, CancellationToken token)
        => StartAsync(actor, input.MediaId, input.ClientId, input.ClientRevision == 0 ? 1 : input.ClientRevision, "preparing", input, input.ReplaceSessionId, token);
    public Task<PlaybackSession> StartMusicAsync(PlaybackActor actor, MusicPlaybackStart input, CancellationToken token)
        => StartAsync(actor, input.MediaId, input.ClientId, input.ClientRevision, "pcm", input, null, token);
    public Task<PlaybackSession> StartReceiverFileAsync(PlaybackActor actor,ReceiverLeaseStart input,CancellationToken token)
        => StartAsync(actor,input.MediaId,input.ClientId,input.ClientRevision,"preparing",input,null,token);
    private async Task<PlaybackSession> StartAsync(PlaybackActor actor, string mediaId, string? client, long revision, string mode, object input, string? replace, CancellationToken token)
    {
        var clientId = client is null ? Guid.NewGuid().ToString("N") : AppFiles.Id(client);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, input.GetType(), Json))));
        Task<PlaybackSession> work;
        await _admissions.WaitAsync(token);
        try
        {
            if (_shutdown.IsCancellationRequested) throw new DomainException("Sunucu kapanıyor; biraz sonra yeniden dene.", 503);
            token.ThrowIfCancellationRequested();
            var admission = store.AdmitPlayback(actor, clientId, revision, mediaId, mode, hash, clock.GetUtcNow(), replace, (input as PlaybackStart)?.Watch);
            if (admission.Existing)
            {
                if (!_live.TryGetValue(admission.Lease.Id, out var existing) || existing.Startup is null) throw new DomainException("Oynatma yeniden başlatılmalı.", 410);
                work = existing.Startup;
            }
            else
            {
                var live = new Live(_shutdown.Token); _live[admission.Lease.Id] = live;
                work = live.Startup = ProduceAsync(admission, actor, input, live, token);
            }
        }
        finally { _admissions.Release(); }
        return await work.WaitAsync(token);
    }
    private async Task<PlaybackSession> ProduceAsync(PlaybackAdmission admission, PlaybackActor actor, object rawInput, Live live, CancellationToken request)
    {
        var lease = admission.Lease;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(request, live.Token);
        budget.CancelAfter(TimeSpan.FromMinutes(1));
        try
        {
            foreach (var previous in admission.Replaced) await CloseRuntimeAsync(previous);
            await ReconcileAsync(); budget.Token.ThrowIfCancellationRequested();
            PlaybackSession session;
            if (rawInput is PlaybackStart input)
                session = await processes.CreateAsync(lease.Id, input with { ClientId = lease.ClientId, ClientRevision = lease.ClientRevision, ReplaceSessionId = null }, budget.Token);
            else
            {
                var media = store.PlaybackSource(lease.MediaId, store.GetAccount(actor.UserId)!).Media;
                session = new(null, media.Id, rawInput is ReceiverLeaseStart?"receiver-file":"pcm", "", 0, media.DurationSeconds ?? 0, [rawInput is ReceiverLeaseStart?"Alıcı için süreli dosya izni.":"Kesintisiz müzik sırası tek oynatma oturumu kullanır."], null, "ready");
            }
            budget.Token.ThrowIfCancellationRequested(); lease = store.BindPlayback(lease, session, clock.GetUtcNow());
            events.Publish("playback.changed"); return View(lease, session);
        }
        catch (Exception ex)
        {
            var revoked = live.Token.IsCancellationRequested;
            store.EndPlaybackLease(lease.Id, budget.IsCancellationRequested ? "cancelled" : "failed", clock.GetUtcNow());
            await CloseRuntimeAsync(lease.Id); events.Publish("playback.changed");
            if (ex is OperationCanceledException && !request.IsCancellationRequested) throw new DomainException(revoked ? "Oynatma oturumu iptal edildi." : "Oynatma hazırlığı zaman aşımına uğradı.", revoked ? 410 : 504);
            throw;
        }
    }
    private static PlaybackSession View(PlaybackLease lease, PlaybackSession info) => info with
    {
        Id = lease.Id, MediaId = lease.MediaId,
        Url = info.Mode == "direct" ? $"/api/media/{lease.MediaId}/stream?playbackId={lease.Id}" : info.Mode == "pcm" ? "" : $"/api/playback/{lease.Id}/index.m3u8"
    };
    public PlaybackSession Touch(string id, PlaybackActor actor, PlaybackHeartbeat? heartbeat = null)
    {
        var lease = store.TouchPlayback(id, actor, heartbeat, clock.GetUtcNow());
        if (!_live.ContainsKey(id) || lease.Session is null) throw new DomainException("Oynatma henüz hazır değil veya yeniden başlatılmalı.", 409);
        var info = lease.Session.Id is { } process ? processes.Touch(process, lease.Id, heartbeat) : lease.Session;
        if (lease.Mode == "pcm")
        {
            var grant = store.GetPlaybackMedia(id, lease.MediaId);
            info = info with { DurationSeconds = grant?.Frames / 48000d ?? store.GetMedia(lease.MediaId)?.DurationSeconds ?? 0 };
        }
        return View(lease, info);
    }
    public async Task CloseAsync(string id, PlaybackActor actor, bool manage = false)
    {
        _ = AppFiles.Id(id); var lease = store.GetPlaybackLease(id); if (lease is null) return;
        var user = store.GetAccount(actor.UserId) ?? throw new DomainException("Hesap bulunamadı.", 401);
        if (manage ? lease.UserId != actor.UserId && !user.IsAdmin : lease.LoginId != actor.LoginId || lease.UserId != actor.UserId) throw new DomainException("Bu oynatma oturumunu kapatamazsın.", 403);
        store.EndPlaybackLease(id, manage && lease.UserId != actor.UserId ? "administrator" : "closed", clock.GetUtcNow());
        await CloseRuntimeAsync(id); events.Publish("playback.changed");
    }
    public async Task CloseClientAsync(string clientId, PlaybackActor actor, long revision)
    {
        foreach (var id in store.ClosePlaybackClient(actor, clientId, revision, clock.GetUtcNow())) await CloseRuntimeAsync(id);
        events.Publish("playback.changed");
    }
    private Task CloseRuntimeAsync(string id)
    {
        if (!_live.TryGetValue(id, out var live)) return Task.CompletedTask;
        TaskCompletionSource completion;
        lock (live.Gate)
        {
            if (live.Cleanup is not null) return live.Cleanup;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); live.Cleanup = completion.Task;
        }
        _ = CleanupAsync(); return completion.Task;
        async Task CleanupAsync()
        {
            try { live.Lifetime.Cancel(); await processes.CloseOwnerAsync(id); completion.TrySetResult(); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { _live.TryRemove(id, out _); live.Lifetime.Dispose(); }
        }
    }
    public async Task ReconcileAsync()
    {
        var closed = store.ReconcilePlaybackLeases(clock.GetUtcNow());
        foreach (var id in _live.Keys)
            if (store.GetPlaybackLease(id)?.Status is null or "closed") await CloseRuntimeAsync(id);
        if (closed.Length > 0) events.Publish("playback.changed");
    }
    public PlaybackUsage Usage(PlaybackActor actor, bool all)
    {
        if (all && store.GetAccount(actor.UserId)?.IsAdmin != true) throw new DomainException("Yönetici yetkisi gerekli.", 403);
        var server = store.GetPlaybackLimits(); var device = store.GetPlaybackDevice(actor.DeviceId)!;
        return new(server, store.GetUserPlaybackLimits(actor.UserId), device.MaximumStreams ?? server.MaximumStreamsPerDevice,
            store.GetPlaybackActivity(all ? null : actor.UserId, actor.DeviceId, false).Concat(store.TransientPlaybackActivity(all?null:actor.UserId,actor.DeviceId)).ToArray(), store.GetPlaybackActivity(all ? null : actor.UserId, actor.DeviceId, true));
    }
    private sealed class RequestLease(PlaybackLease lease, Live live) : IDisposable
    {
        public PlaybackLease Lease { get; } = lease;
        public CancellationToken Token => live.Token;
        public void Dispose() => Interlocked.Decrement(ref live.Requests);
    }
    private RequestLease OpenRequest(string? id, PlaybackActor actor, string? mode, string mediaId)
    {
        var lease = store.RequirePlaybackLease(id, actor, clock.GetUtcNow(), mode, mediaId);
        if (lease.Status != "active" || !_live.TryGetValue(lease.Id, out var live)) throw new DomainException("Oynatma henüz hazır değil.", 409);
        if (Interlocked.Increment(ref live.Requests) > 6) { Interlocked.Decrement(ref live.Requests); throw new DomainException("Bu oynatıcıda çok fazla eşzamanlı parça isteği var.", 429); }
        try { if (live.Token.IsCancellationRequested) throw new DomainException("Oynatma oturumu kapatıldı.", 410); store.TouchPlayback(lease.Id, actor, null, clock.GetUtcNow()); return new(lease, live); }
        catch { Interlocked.Decrement(ref live.Requests); throw; }
    }
    private sealed class LeaseResult(PlaybackSessions sessions, string? id, PlaybackActor actor, string? mode, string mediaId,
        Func<RequestLease, CancellationToken, Task<IResult>> result) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            using var access = sessions.OpenRequest(id, actor, mode, mediaId);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(access.Token, context.RequestAborted);
            using var abort = access.Token.Register(context.Abort);
            var response = await result(access, lifetime.Token);
            try { lifetime.Token.ThrowIfCancellationRequested(); await response.ExecuteAsync(context); }
            finally { if (response is FileStreamHttpResult file) await file.FileStream.DisposeAsync(); }
        }
    }
    public IResult Direct(string mediaId, string? id, PlaybackActor actor) => new LeaseResult(this, id, actor, "direct", mediaId, (access, token) =>
    {
        token.ThrowIfCancellationRequested(); var source = store.PlaybackSource(mediaId, store.GetAccount(actor.UserId)!);
        if (source.Fingerprint != access.Lease.Fingerprint) throw new DomainException("Medya dosyası değişti.", 409);
        var info = new FileInfo(source.Path);
        IResult result = Results.Stream(new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess), source.Media.MimeType,
            enableRangeProcessing: true, lastModified: new DateTimeOffset(info.LastWriteTimeUtc), entityTag: new EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\""));
        return Task.FromResult(result);
    });
    public IResult Artifact(string id, string file, PlaybackActor actor)
    {
        var lease = store.RequirePlaybackLease(id, actor, clock.GetUtcNow());
        return new LeaseResult(this, id, actor, null, lease.MediaId, (access, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (access.Lease.Session?.Id is not { } process) throw new DomainException("Bu oynatmanın HLS parçası yok.", 404);
            var artifact = processes.OpenArtifact(process, file, access.Lease.Id);
            return Task.FromResult(Results.Stream(artifact.Stream, artifact.Mime, enableRangeProcessing: true));
        });
    }
    public IResult ReceiverFile(string id, PlaybackActor actor, Func<(string Path, string Mime, string Fingerprint)> resolve) => new LeaseResult(this, id, actor, "receiver-file", store.GetPlaybackLease(id)?.MediaId ?? "", (access, token) =>
    {
        token.ThrowIfCancellationRequested(); var source = resolve(); var info = new FileInfo(source.Path); AppFiles.RejectLink(source.Path);
        if (MediaAnalysisService.Fingerprint(info) != source.Fingerprint) throw new DomainException("Alıcı dosyası değişti.", 410);
        return Task.FromResult<IResult>(Results.Stream(new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess), source.Mime,
            enableRangeProcessing: true, lastModified: new DateTimeOffset(info.LastWriteTimeUtc), entityTag: new EntityTagHeaderValue($"\"{source.Fingerprint}\"")));
    });
    public IResult PrepareMusic(string mediaId, string? id, PlaybackActor actor) => new LeaseResult(this, id, actor, "pcm", mediaId, async (access, token) =>
    {
        store.AuthorizePlaybackMedia(access.Lease.Id, actor, mediaId, clock.GetUtcNow());
        var info = await pcm.PrepareAsync(mediaId, token); token.ThrowIfCancellationRequested();
        store.CompletePlaybackMedia(access.Lease.Id, actor, info, clock.GetUtcNow()); return Results.Ok(info);
    });
    public IResult MusicBytes(string mediaId, string? id, string fingerprint, long start, int frames, PlaybackActor actor) => new LeaseResult(this, id, actor, "pcm", mediaId, async (access, token) =>
    {
        store.RequirePlaybackMedia(access.Lease.Id, actor, mediaId, fingerprint, clock.GetUtcNow());
        var bytes = await pcm.ReadAsync(mediaId, fingerprint, start, frames, token); token.ThrowIfCancellationRequested();
        store.RequirePlaybackMedia(access.Lease.Id, actor, mediaId, fingerprint, clock.GetUtcNow()); return Results.Bytes(bytes, "application/octet-stream");
    });
    public override Task StartAsync(CancellationToken token) { store.RecoverPlaybackLeases(clock.GetUtcNow()); return base.StartAsync(token); }
    public override async Task StopAsync(CancellationToken token) { _shutdown.Cancel(); await base.StopAsync(token); }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var lastPrune = clock.GetUtcNow();
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ReconcileAsync();
                    if (clock.GetUtcNow() - lastPrune >= TimeSpan.FromMinutes(5)) { store.PrunePlaybackHistory(clock.GetUtcNow()); lastPrune = clock.GetUtcNow(); }
                }
                catch (Exception ex) { logger.LogWarning("Oynatma oturumları uzlaştırılamadı: {Type}", ex.GetType().Name); }
                await Task.Delay(TimeSpan.FromSeconds(2), clock, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            foreach (var lease in store.ActivePlaybackLeases()) { store.EndPlaybackLease(lease.Id, "server-stopped", clock.GetUtcNow()); await CloseRuntimeAsync(lease.Id); }
            foreach (var id in _live.Keys) await CloseRuntimeAsync(id);
        }
    }
    public override void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _shutdown.Cancel(); _shutdown.Dispose(); base.Dispose(); }
}
