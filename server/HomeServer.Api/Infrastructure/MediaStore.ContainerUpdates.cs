using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private long _containerUpdateUntil;
    private string? _containerUpdateOwner;
    public bool ContainerUpdateBlocked => Volatile.Read(ref _containerUpdateUntil) > DateTimeOffset.UtcNow.UtcTicks;

    public ContainerUpdateActivity ContainerUpdateActivity()
    {
        lock (_writeGate)
        {
            using var db = Open();
            int Count(string sql) => Convert.ToInt32(Scalar(db, sql));
            var transfers = Count("SELECT COUNT(*) FROM downloads WHERE archived=0 AND desired_state='running' AND status NOT IN ('completed','seeding','paused','cancelled','failed','budget-waiting')");
            var recordings = Convert.ToInt32(Scalar(db, """
                SELECT COUNT(*) FROM dvr_recordings WHERE status IN ('recording','finalizing','publishing')
                OR (status IN ('scheduled','conflict') AND starts_at<=$soon AND ends_at>$now)
                """, ("$soon", DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")), ("$now", Now())));
            var jobs = 0;
            foreach (var table in new[] { "jobs", "system_jobs", "product_jobs", "auto_match_jobs", "automation_jobs", "music_automation_jobs", "music_tasks", "music_list_jobs", "offline_exports", "episode_boundary_jobs", "subtitle_sync_jobs" })
                jobs += Count($"SELECT COUNT(*) FROM {table} WHERE status IN ('running','applying','publishing')");
            jobs += Count("SELECT COUNT(*) FROM tracking WHERE sync_state='syncing'");
            jobs += Count("SELECT COUNT(*) FROM subtitle_wanted WHERE status='searching'");
            jobs += Count("SELECT COUNT(*) FROM notification_deliveries WHERE status='sending'");
            return new(GetActiveGameSessions().Count,
                ActivePlaybackLeases().Count + ActiveLiveSessions().Count + ActivePodcastSessions().Count + ActiveMusicRadioSessions().Count,
                transfers, recordings, jobs);
        }
    }
    public bool BeginContainerUpdate(string owner, TimeSpan duration)
    {
        _ = AppFiles.Id(owner);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked || !ContainerUpdateActivity().Idle) return false;
            _containerUpdateOwner = owner;
            Volatile.Write(ref _containerUpdateUntil, DateTimeOffset.UtcNow.Add(duration).UtcTicks);
            return true;
        }
    }
    public void EndContainerUpdate(string owner)
    {
        lock (_writeGate)
        {
            if (_containerUpdateOwner != owner) return;
            _containerUpdateOwner = null; Volatile.Write(ref _containerUpdateUntil, 0);
        }
    }
    private void RequireContainerWorkAllowed()
    {
        if (ContainerUpdateBlocked) throw new DomainException("Home güncellemeye hazırlanıyor. Biraz sonra yeniden bağlan.", 503);
    }
}
