using HomeServer.Core;
using HomeServer.Music;

namespace HomeServer.Infrastructure;

public sealed record MusicRadioSession(string Id, string UserId, string LoginId, string DeviceId, string ClientId, long Revision, string StationId, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastSeen);

public sealed partial class MediaStore
{
    // Radio relays own no files or recoverable timeline. Process-local leases deliberately expire on restart.
    private readonly Dictionary<string, MusicRadioSession> _radioSessions = new(StringComparer.Ordinal);
    public IReadOnlyList<MusicRadioSession> ActiveMusicRadioSessions()
    { lock (_writeGate) return _radioSessions.Values.Where(s=>DateTimeOffset.UtcNow-s.LastSeen<TimeSpan.FromSeconds(90)).ToArray(); }
    public MusicRadioSession AdmitMusicRadio(RadioStation station, PlaybackActor actor, string clientId, long revision)
    {
        _ = AppFiles.Id(clientId); if (revision is <1 or >9_007_199_254_740_991) throw new DomainException("Radyo komutu geçersiz.");
        lock (_writeGate)
        {
            var user=RequirePlaybackActor(actor); if(!MusicAccess.Online(user)) throw new DomainException("Bu profil radyo yayınlarına erişemiyor.",403);
            var now=DateTimeOffset.UtcNow;
            foreach(var old in _radioSessions.Values.Where(s=>now-s.LastSeen>=TimeSpan.FromSeconds(90)).ToArray()) _radioSessions.Remove(old.Id);
            using var db=Open();var previous=Scalar(db,"SELECT revision FROM playback_intents WHERE login_id=$login AND client_id=$client",("$login",actor.LoginId),("$client",clientId));
            if(previous is not null&&Convert.ToInt64(previous)>=revision)throw new DomainException("Daha yeni radyo komutu zaten uygulandı.",409);
            // The same browser revision fence applies when switching between local music and radio.
            ClosePlaybackClient(actor,clientId,revision,now);
            RequireTransientSlot(actor,clientId);
            foreach(var live in ActiveLiveSessions().Where(s=>s.LoginId==actor.LoginId&&s.ClientId==clientId))SetLiveSession(live.Id,"closed");
            foreach(var old in _radioSessions.Values.Where(s=>s.LoginId==actor.LoginId&&s.ClientId==clientId).ToArray())_radioSessions.Remove(old.Id);
            var session=new MusicRadioSession(Guid.NewGuid().ToString("N"),actor.UserId,actor.LoginId,actor.DeviceId,clientId,revision,station.Id,station.Name,now,now);
            _radioSessions[session.Id]=session;return session;
        }
    }
    public MusicRadioSession RequireMusicRadio(string id, PlaybackActor actor, bool touch=false)
    {
        lock(_writeGate)
        {
            if(!_radioSessions.TryGetValue(id,out var session)||session.UserId!=actor.UserId||session.LoginId!=actor.LoginId||session.DeviceId!=actor.DeviceId||DateTimeOffset.UtcNow-session.LastSeen>=TimeSpan.FromSeconds(90))throw new DomainException("Radyo oturumu sona erdi.",410);
            if(!MusicAccess.Online(RequirePlaybackActor(actor)))throw new DomainException("Bu profil radyo yayınlarına erişemiyor.",403);
            CheckTransientLimits(actor);if(touch)_radioSessions[id]=session with{LastSeen=DateTimeOffset.UtcNow};return session;
        }
    }
    public void CloseMusicRadio(string id) { lock(_writeGate) _radioSessions.Remove(id); }
}
