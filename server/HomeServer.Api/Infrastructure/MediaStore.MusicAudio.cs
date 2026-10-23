using System.Text.Json;
using HomeServer.Core;
using HomeServer.Music;
using HomeServer.Playback;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string? MusicAlbumForMedia(string id) { using var db = Open(); return Scalar(db, "SELECT album_id FROM music_tracks WHERE media_id=$id", ("$id", id)) as string; }
    public MusicPlaybackPreferences GetMusicPlaybackPreferences(string user)
    {
        using var db = Open(); var json = Scalar(db, "SELECT preferences_json FROM music_playback_preferences WHERE user_id=$user", ("$user", user)) as string;
        return json is null ? new() : JsonSerializer.Deserialize<MusicPlaybackPreferences>(json, WebJson)!;
    }
    public void SaveMusicPlaybackPreferences(string user, MusicPlaybackPreferences input)
    {
        if (input.ReplayGain is not ("off" or "track" or "album") || !double.IsFinite(input.PreampDb) || input.PreampDb is < -15 or > 15) throw new DomainException("Müzik ses tercihi geçersiz.");
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO music_playback_preferences(user_id,preferences_json) VALUES($user,$json) ON CONFLICT(user_id) DO UPDATE SET preferences_json=$json", ("$user", user), ("$json", JsonSerializer.Serialize(input, WebJson))); }
    }
    public bool IsMusicFavorite(string id, string user) { using var db = Open(); return Scalar(db, "SELECT media_id FROM music_favorites WHERE media_id=$id AND user_id=$user", ("$id", id), ("$user", user)) is string; }
    public void SetMusicFavorite(string id, Account actor, bool favorite)
    {
        if (GetMedia(id, actor)?.Kind != "audio") throw new DomainException("Ses dosyası bulunamadı.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            if (favorite)
            {
                if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_favorites WHERE user_id=$user", ("$user", actor.Id))) >= 5000 && !IsMusicFavorite(id, actor.Id)) throw new DomainException("En fazla 5000 favori parça saklanabilir.");
                Execute(db, "INSERT OR IGNORE INTO music_favorites(user_id,media_id,created_at) VALUES($user,$id,$now)", ("$user", actor.Id), ("$id", id), ("$now", Now()));
            }
            else Execute(db, "DELETE FROM music_favorites WHERE user_id=$user AND media_id=$id", ("$user", actor.Id), ("$id", id));
        }
    }
    public MusicSonicAnalysis? GetMusicSonic(string id, string? fingerprint = null)
    {
        using var db = Open(); var json = Scalar(db, "SELECT analysis_json FROM music_sonic_analysis WHERE media_id=$id AND ($fp IS NULL OR fingerprint=$fp)", ("$id", id), ("$fp", fingerprint)) as string;
        return json is null ? null : JsonSerializer.Deserialize<MusicSonicAnalysis>(json, WebJson);
    }
    public void SaveMusicSonic(MusicTask task, MusicSonicAnalysis result)
    {
        lock (_writeGate)
        {
            if (!MusicTaskCurrent(task)) throw new OperationCanceledException();
            var media = GetMedia(result.MediaId) ?? throw new DomainException("Ses dosyası bulunamadı.", 404);
            var path = MediaPaths.ResolveFile(GetLibrary(media.LibraryId)!.Path, media.RelativePath);
            if (result.Fingerprint != MediaAnalysisService.Fingerprint(new FileInfo(path))) throw new DomainException("Ses analiz sırasında değişti.", 409);
            using var db = Open(); Execute(db, "INSERT INTO music_sonic_analysis(media_id,fingerprint,analysis_json,updated_at) VALUES($id,$fp,$json,$now) ON CONFLICT(media_id) DO UPDATE SET fingerprint=$fp,analysis_json=$json,updated_at=$now",
                ("$id", result.MediaId), ("$fp", result.Fingerprint), ("$json", JsonSerializer.Serialize(result, WebJson)), ("$now", Now()));
        }
    }
    public IReadOnlyList<MusicSuggestion> MusicSuggestions(Account actor, string? seedId = null)
    {
        if (seedId is not null && GetMedia(seedId, actor)?.Kind != "audio") throw new DomainException("Ses dosyası bulunamadı.", 404);
        using var db = Open();
        var ids = seedId is not null ? [seedId] : Query(db, """
            SELECT media_id FROM (SELECT media_id,created_at AS at FROM music_favorites WHERE user_id=$user
                UNION ALL SELECT w.media_id,w.updated_at AS at FROM user_watch_state w JOIN music_tracks t ON t.media_id=w.media_id WHERE w.user_id=$user)
            GROUP BY media_id ORDER BY MAX(at) DESC LIMIT 10
            """, r => r.GetString(0), ("$user", actor.Id)).ToArray();
        var seeds = ids.Where(id => GetMedia(id, actor) is not null).Select(id => CurrentSonic(id)).Where(value => value is not null).Cast<MusicSonicAnalysis>().ToArray();
        if (seeds.Length == 0) return [];
        var candidates = Query(db, $"SELECT s.media_id,t.artist FROM music_sonic_analysis s JOIN music_tracks t ON t.media_id=s.media_id JOIN media m ON m.id=s.media_id JOIN libraries l ON l.id=m.library_id WHERE m.available=1 AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess} ORDER BY s.updated_at DESC LIMIT 5000", r => (Id: r.GetString(0), Artist: r.GetString(1)), ViewerArgs(actor));
        return candidates.Where(item => !ids.Contains(item.Id)).Select(item => (item, Analysis: CurrentSonic(item.Id))).Where(item => item.Analysis is not null)
            .Select(item => new MusicSuggestion(GetMedia(item.item.Id, actor)!, item.item.Artist, seeds.Max(seed => MusicSonicAnalyzer.Similarity(seed, item.Analysis!)), seedId is null ? "Favori ve son dinlediklerinle yerel ses benzerliği" : "Seçilen parçayla yerel ses benzerliği"))
            .OrderByDescending(item => item.Similarity).ThenBy(item => item.Media.Id, StringComparer.Ordinal).Take(20).ToArray();
    }
    private MusicSonicAnalysis? CurrentSonic(string id)
    {
        var result = GetMusicSonic(id); if (result is null) return null;
        var media = GetMedia(id); if (media is null) return null;
        try { return result.Fingerprint == MediaAnalysisService.Fingerprint(new FileInfo(MediaPaths.ResolveFile(GetLibrary(media.LibraryId)!.Path, media.RelativePath))) ? result : null; }
        catch (Exception ex) when (ex is IOException or DomainException or UnauthorizedAccessException) { return null; }
    }
    private const string MusicTaskSelect = "SELECT id,media_id,library_id,kind,status,revision,message,created_at,updated_at,plan_json,history_pruned FROM music_tasks";
    private static MusicTask ReadMusicTask(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6), r.GetString(7), r.GetString(8), Text(r, 9), r.GetString(3) != "sonic" && r.GetString(4) == "completed" && !r.GetBoolean(10));
    public MusicTask? GetMusicTask(string id) { using var db = Open(); return Query(db, MusicTaskSelect + " WHERE id=$id", ReadMusicTask, ("$id", id)).FirstOrDefault(); }
    public IReadOnlyList<MusicTask> GetMusicTasks() { using var db = Open(); return Query(db, MusicTaskSelect + " ORDER BY updated_at DESC LIMIT 100", ReadMusicTask); }
    public MusicTask QueueMusicTask(string mediaId, string kind, MusicTagPlan? plan = null)
    {
        if (kind is not ("sonic" or "tags" or "restore-tags")) throw new DomainException("Müzik işi türü geçersiz.");
        var media = GetMedia(mediaId) ?? throw new DomainException("Ses dosyası bulunamadı.", 404);
        if (media.Kind != "audio" || !media.Available) throw new DomainException("Erişilebilir bir ses dosyası seç.");
        lock (_writeGate)
        {
            using var db = Open();
            var existing = Query(db, MusicTaskSelect + " WHERE media_id=$media AND status IN ('queued','running','publishing')", ReadMusicTask, ("$media", mediaId)).FirstOrDefault();
            if (existing is not null) return existing.Kind == kind && (kind == "sonic" || existing.PlanJson == JsonSerializer.Serialize(plan, WebJson)) ? existing : throw new DomainException("Dosyanın başka bir müzik işlemi sürüyor.", 409);
            Execute(db, "DELETE FROM music_tasks WHERE kind='sonic' AND status IN ('completed','failed','cancelled') AND updated_at<$old", ("$old", DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_tasks WHERE status IN ('queued','running','publishing')")) >= 2000) throw new DomainException("Müzik iş kuyruğu dolu.", 429);
            if (kind != "sonic" && (plan is null || !IsManagedMusic(media))) throw new DomainException("Etiket yazımı yalnız Home'un yönettiği müzik dosyalarında kullanılabilir.", 403);
            var id = Guid.NewGuid().ToString("N"); Execute(db, "INSERT INTO music_tasks(id,media_id,library_id,kind,status,plan_json,created_at,updated_at) VALUES($id,$media,$library,$kind,'queued',$plan,$now,$now)",
                ("$id", id), ("$media", mediaId), ("$library", media.LibraryId), ("$kind", kind), ("$plan", plan is null ? null : JsonSerializer.Serialize(plan, WebJson)), ("$now", Now()));
            return GetMusicTask(id)!;
        }
    }
    public bool IsManagedMusic(MediaItem media) => media.Kind == "audio" && GetLibrary(media.LibraryId) is { } library && MediaPaths.Key(library.Path) == MediaPaths.Key(Path.Combine(_paths.Data, "downloads", "music-library"));
    public bool MusicTaskCurrent(MusicTask task) => GetMusicTask(task.Id) is { } current && current.Revision == task.Revision && current.Status is "running" or "publishing";
    public void RecoverMusicTasks() { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_tasks SET status='queued',message='Kesilen müzik işi toparlandı.' WHERE status='running'"); } }
    public MusicTask? ClaimMusicTask()
    {
        lock (_writeGate)
        {
            if (ContainerUpdateBlocked) return null;
            using var db = Open(); var task = Query(db, MusicTaskSelect + " WHERE status IN ('queued','publishing') ORDER BY CASE WHEN status='publishing' THEN 0 ELSE 1 END,created_at LIMIT 1", ReadMusicTask).FirstOrDefault();
            if (task is null) return null;
            Execute(db, "UPDATE music_tasks SET status=CASE WHEN status='publishing' THEN status ELSE 'running' END,updated_at=$now WHERE id=$id", ("$id", task.Id), ("$now", Now())); return GetMusicTask(task.Id);
        }
    }
    public void FinishMusicTask(MusicTask task, string status, string message)
    {
        if (status is not ("completed" or "failed")) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_tasks SET status=$status,message=$message,updated_at=$now WHERE id=$id AND revision=$revision AND status='running'", ("$id", task.Id), ("$revision", task.Revision), ("$status", status), ("$message", message), ("$now", Now())); }
    }
    public void CancelMusicTask(string id)
    {
        lock (_writeGate)
        {
            var task = GetMusicTask(id) ?? throw new DomainException("Müzik işi bulunamadı.", 404);
            if (task.Status == "publishing") throw new DomainException("Atomik etiket yayını tamamlanıyor; ardından geri alabilirsin.", 409);
            using var db = Open(); Execute(db, "UPDATE music_tasks SET status='cancelled',revision=revision+1,message='Kullanıcı iptal etti.',updated_at=$now WHERE id=$id AND status IN ('queued','running')", ("$id", id), ("$now", Now()));
        }
    }
}
