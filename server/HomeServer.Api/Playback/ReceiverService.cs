using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed partial class ReceiverService(MediaStore store, PlaybackSessions playback, DlnaController dlna, MediaAnalysisService analysis, MediaTools tools, HomePaths paths, EventHub events) : IProductWorkHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim[] _controls = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private SemaphoreSlim ControlGate(string key) => _controls[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _controls.Length];
    public string[] Kinds => ["receiver-prepare"];
    public ReceiverSettings Settings => store.GetSetting("receivers.settings") is { } json ? JsonSerializer.Deserialize<ReceiverSettings>(json, Json) ?? new() : new();
    public void SaveSettings(ReceiverSettings input)
    {
        if (input.PublicBaseUrl is { Length: > 0 } text && !ValidBase(text)) throw new DomainException("Alıcıların erişebileceği HTTP/HTTPS Home adresini gir; localhost kullanılamaz.");
        store.SetSetting("receivers.settings", JsonSerializer.Serialize(input with { PublicBaseUrl = string.IsNullOrWhiteSpace(input.PublicBaseUrl) ? null : input.PublicBaseUrl.TrimEnd('/') }, Json));
    }
    private static bool ValidBase(string value) => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !uri.IsLoopback && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    public async Task<ReceiverView> StartAsync(ReceiverStart input, PlaybackActor actor, string requestBase, CancellationToken token)
    {
        if (input.Kind is not ("cast" or "airplay" or "dlna") || !double.IsFinite(input.PositionSeconds) || input.PositionSeconds < 0 || input.Name?.Length > 160 || input.Name?.Any(char.IsControl) == true) throw new DomainException("Alıcı seçimi geçersiz.");
        var baseText = Settings.PublicBaseUrl ?? requestBase;
        if (!ValidBase(baseText)) throw new DomainException("Alıcı ayarlarında Home'un yerel ağ veya HTTPS adresini tanımla.", 409);
        var viewer = store.GetAccount(actor.UserId) ?? throw new DomainException("Hesap bulunamadı.", 401); var source = store.PlaybackSource(input.MediaId, viewer);
        var device = input.Kind == "dlna" ? store.GetRenderDevice(input.TargetId ?? "") ?? throw new DomainException("DLNA alıcısı bulunamadı.", 404) : null;
        var gate = ControlGate(device?.Id ?? actor.LoginId); await gate.WaitAsync(token);
        PlaybackSession? session = null; ReceiverGrant? grant = null;
        try
        {
            var name = device?.Name ?? input.Name ?? (input.Kind == "cast" ? "Cast alıcısı" : "AirPlay alıcısı"); string mode, mime; ReceiverCopy? copy = null;
            if (device is not null && !device.Protocols.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
            {
                if (input.SubtitleId is not null) throw new DomainException("Altyazılı gönderim için HLS destekleyen bir alıcı gerekli.", 409);
                var media = await analysis.InspectAsync(input.MediaId, token); source = store.PlaybackSource(input.MediaId, viewer);
                var original = source.Media.MimeType is "video/mp4" or "audio/mpeg" or "audio/mp4" && media.Probe.VideoCodec is null or "h264" && media.Probe.AudioCodec is null or "aac" or "mp3";
                if (!original)
                {
                    copy = store.GetReceiverCopy(source.Media.Id);
                    if (copy is null || copy.Fingerprint != source.Fingerprint || copy.MatchRevision != source.Media.MatchRevision) throw new DomainException("Bu DLNA alıcısı HLS bildirmiyor. Önce alıcıya uyumlu MP4 kopyasını hazırla.", 409);
                    _ = CopyFile(copy);
                }
                mode = original ? "original" : "copy"; mime = original ? source.Media.MimeType : "video/mp4";
                session = await playback.StartReceiverFileAsync(actor, new(input.MediaId, Guid.NewGuid().ToString("N")), token);
            }
            else
            {
                session = await playback.StartPlaybackAsync(actor, new(input.MediaId, new(["mp4", "m4a", "mp3"], ["h264"], ["aac", "mp3"], true), input.PositionSeconds, MaxHeight: 1080, MaxBitrate: 8_000_000,
                    ClientId: Guid.NewGuid().ToString("N"), ClientRevision: 1, SubtitleId: input.SubtitleId, BurnSubtitle: input.SubtitleId is not null), token);
                mode = session.Mode == "direct" ? "direct" : "hls"; mime = mode == "direct" ? source.Media.MimeType : "application/vnd.apple.mpegurl";
            }
            if (session.DurationSeconds > 0 && input.PositionSeconds > session.DurationSeconds) throw new DomainException("Alıcı başlangıç konumu dosya süresini aşıyor.");
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            grant = store.CreateReceiver(actor, session.Id!, secret, mode, name, input.Kind, device, copy);
            var url = baseText.TrimEnd('/') + "/receiver/" + secret + (mode == "hls" ? "/index.m3u8" : "/media");
            if (device is not null)
            {
                await dlna.LoadAsync(device, url, mime, source.Media.Title, token);
                if (input.PositionSeconds > session.StartSeconds) await dlna.CommandAsync(device, new("seek", input.PositionSeconds - session.StartSeconds), token);
            }
            events.Publish("receivers.changed");
            return new(grant.Id, session.Id!, url, mime, name, mode, input.PositionSeconds, session.DurationSeconds, device?.Id, session.StartSeconds, input.Kind, grant.Revision, input.MediaId);
        }
        catch
        {
            if (grant is not null) store.CloseReceiver(grant.Id);
            if (session?.Id is { } id) await playback.CloseAsync(id, actor);
            throw;
        }
        finally { gate.Release(); }
    }
    public IReadOnlyList<ReceiverView> Sessions(PlaybackActor actor) => store.Receivers(actor.UserId).Where(grant => grant.LoginId == actor.LoginId && !store.ReceiverStatus(grant).Closed).Select(grant =>
    {
        var lease = store.GetPlaybackLease(grant.PlaybackId); var status = store.ReceiverStatus(grant);
        return new ReceiverView(grant.Id, grant.PlaybackId, "", grant.Mode == "hls" ? "application/vnd.apple.mpegurl" : "", grant.TargetName, grant.Mode, status.PositionSeconds, status.DurationSeconds, grant.TargetId, lease?.Session?.StartSeconds ?? 0, grant.Kind, grant.Revision, lease?.MediaId);
    }).ToArray();
    public ReceiverStatus Status(string id, PlaybackActor actor) => store.ReceiverStatus(store.OwnReceiver(id, actor));
    private static void ValidateControl(ReceiverControl input)
    {
        if (input.Action is not ("heartbeat" or "status" or "stop" or "play" or "pause" or "seek" or "volume") || input.PositionSeconds is { } position && (!double.IsFinite(position) || position is < 0 or > 86400)
            || input.Volume is { } volume && (!double.IsFinite(volume) || volume is < 0 or > 1) || input.Action == "seek" && input.PositionSeconds is null || input.Action == "volume" && input.Volume is null) throw new DomainException("Alıcı komutu veya değeri geçersiz.");
    }
    public async Task<ReceiverStatus> ControlAsync(string id, ReceiverControl input, PlaybackActor actor, CancellationToken token)
    {
        ValidateControl(input); var before = store.OwnReceiver(id, actor); var gate = ControlGate(before.TargetId ?? id); await gate.WaitAsync(token);
        try
        {
            var grant = store.OwnReceiver(id, actor);
            if (input.Action == "status") return store.ReceiverStatus(grant);
            if (input.Action == "heartbeat")
            {
                grant = store.RequireReceiver(id, actor);
                if (grant.Kind == "dlna") return store.ReceiverStatus(grant); // DLNA observations come from its SOAP state, never a sender's guess.
                if (input.PositionSeconds is null || input.Paused is null) throw new DomainException("Göndericinin gerçek konum ve oynatma durumu gerekli.");
                var result = store.ObserveReceiver(grant, actor, new(input.PositionSeconds, null, input.Paused, input.Volume, input.Paused.Value ? "paused" : "playing", ""));
                playback.Touch(grant.PlaybackId, actor); return result;
            }
            if (grant.Kind != "dlna" && input.Action != "stop") throw new DomainException("Bu alıcının kontrolü tarayıcı göndericisindedir.", 409);
            var admission = store.BeginReceiverCommand(id, input, actor); if (admission.Result is not null) return admission.Result; grant = admission.Grant;
            ReceiverStatus status;
            if (input.Action == "stop")
            {
                try
                {
                    if (!grant.Closed && grant.Device is { } device)
                    {
                        var observed = await dlna.ReadAsync(device, token);
                        if (store.ReceiverOwnsUrl(grant, observed.TrackUrl)) await dlna.CommandAsync(device, input, token);
                    }
                }
                catch (Exception ex) when (ex is DomainException or HttpRequestException or OperationCanceledException) { }
                finally { await CloseAsync(grant); }
                status = store.ReceiverStatus(store.GetReceiver(id)!);
            }
            else
            {
                var device = grant.Device ?? throw new DomainException("Alıcının cihaz kaydı bulunamadı.", 410);
                var observed = await dlna.ReadAsync(device, token);
                if (!store.ReceiverOwnsUrl(grant, observed.TrackUrl)) { await CloseAsync(grant); throw new DomainException("Alıcı başka bir içeriğe geçti; Home kumandası kapatıldı.", 410); }
                _ = store.RequireReceiver(id, actor);
                var start = store.GetPlaybackLease(grant.PlaybackId)?.Session?.StartSeconds ?? 0;
                if (input.Action == "seek" && input.PositionSeconds < start) throw new DomainException("Bu HLS oturumunun başlangıcından önceye gitmek için medyayı o konumdan yeniden gönder.", 409);
                var duration = store.GetPlaybackLease(grant.PlaybackId)?.Session?.DurationSeconds ?? 0;
                if (input.Action == "seek" && duration > 0 && input.PositionSeconds > duration) throw new DomainException("Alıcı konumu dosya süresini aşıyor.");
                await dlna.CommandAsync(device, input.Action == "seek" ? input with { PositionSeconds = input.PositionSeconds - start } : input, token);
                status = await ObserveAsync(grant, actor, token);
            }
            store.CompleteReceiverCommand(id, input.CommandId!, status); events.Publish("receivers.changed"); return status;
        }
        finally { gate.Release(); }
    }
    private async Task<ReceiverStatus> ObserveAsync(ReceiverGrant grant, PlaybackActor actor, CancellationToken token)
    {
        _ = store.RequireReceiver(grant.Id, actor);
        var observed = await dlna.ReadAsync(grant.Device ?? throw new DomainException("Alıcı cihazı yeniden seçilmeli.", 410), token);
        if (!store.ReceiverOwnsUrl(grant, observed.TrackUrl)) { await CloseAsync(grant); return store.ReceiverStatus(store.GetReceiver(grant.Id)!); }
        var terminal = observed.State is "STOPPED" or "NO_MEDIA_PRESENT";
        var lease = store.GetPlaybackLease(grant.PlaybackId)!; var position = observed.PositionSeconds is { } seconds ? seconds + (lease.Session?.StartSeconds ?? 0) : (double?)null;
        if (terminal && observed.PositionSeconds == 0) position = grant.Observation?.PositionSeconds; // Some renderers reset RelTime when the stream ends.
        if (position is { } actual && lease.Session?.DurationSeconds is > 0) position = Math.Min(actual, lease.Session.DurationSeconds);
        var state = observed.State switch { "PLAYING" => "playing", "PAUSED_PLAYBACK" => "paused", "TRANSITIONING" => "buffering", "STOPPED" or "NO_MEDIA_PRESENT" => "stopped", _ => "unknown" };
        var result = store.ObserveReceiver(grant, actor, new(position, observed.DurationSeconds, observed.Paused, observed.Volume, state, ""));
        if (terminal) { await CloseAsync(grant); return store.ReceiverStatus(store.GetReceiver(grant.Id)!); }
        playback.Touch(grant.PlaybackId, actor); return result;
    }
    public async Task RefreshAsync(ReceiverGrant expected, CancellationToken token)
    {
        var gate = ControlGate(expected.TargetId ?? expected.Id); if (!await gate.WaitAsync(0, token)) return;
        try
        {
            var grant = store.GetReceiver(expected.Id); if (grant is null || grant.Closed) return;
            try
            {
                var actor = store.GetPlaybackActor(grant.UserId, grant.LoginId); _ = store.RequireReceiver(grant.Id, actor);
                await ObserveAsync(grant, actor, token);
            }
            catch (DomainException ex) when (ex.StatusCode is 401 or 403 or 404 or 410) { await CloseAsync(grant); }
            catch (Exception ex) when (ex is DomainException or HttpRequestException or OperationCanceledException)
            {
                if (!token.IsCancellationRequested) store.ReceiverError(grant, "Alıcının güncel durumu okunamadı; bağlantı yeniden denenecek.");
            }
        }
        finally { gate.Release(); }
    }
    private async Task CloseAsync(ReceiverGrant grant)
    {
        store.CloseReceiver(grant.Id); await playback.ReconcileAsync(); events.Publish("receivers.changed");
    }
    private (string Path, string Mime, string Fingerprint) CopyFile(ReceiverCopy copy)
    {
        var root = AppFiles.DirectoryIn(paths.Data, "receiver-copies");
        var path = MediaPaths.ResolveFile(root, Path.GetRelativePath(root, copy.Path)); var info = new FileInfo(path);
        if (copy.ArtifactFingerprint is null || copy.Sha256?.Length != 64 || !info.Exists || info.Length != copy.SizeBytes || MediaAnalysisService.Fingerprint(info) != copy.ArtifactFingerprint) throw new DomainException("Alıcı kopyası değişti; yeniden hazırlanmalı.", 410);
        return (path, "video/mp4", copy.ArtifactFingerprint);
    }
    public IResult Media(string secret, string file)
    {
        var grant = store.RequireReceiverToken(secret); var actor = store.GetPlaybackActor(grant.UserId, grant.LoginId);
        if (grant.Mode == "hls") return playback.Artifact(grant.PlaybackId, file, actor);
        if (file != "media") throw new DomainException("Alıcı dosyası bulunamadı.", 404);
        var lease = store.GetPlaybackLease(grant.PlaybackId)!;
        if (grant.Mode == "direct") return playback.Direct(lease.MediaId, grant.PlaybackId, actor);
        return playback.ReceiverFile(lease.Id, actor, () =>
        {
            var current = store.RequireReceiverToken(secret);
            if (current.Mode == "copy") return CopyFile(current.Artifact ?? throw new DomainException("Alıcı kopyasının izni sona erdi.", 410));
            var source = store.PlaybackSource(lease.MediaId, store.GetAccount(grant.UserId)!); return (source.Path, source.Media.MimeType, source.Fingerprint);
        });
    }
}
