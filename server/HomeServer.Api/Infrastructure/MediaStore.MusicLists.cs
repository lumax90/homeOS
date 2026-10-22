using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Music;

namespace HomeServer.Infrastructure;

public sealed record MusicListWork(MusicListJob Job, string UserId, int UserVersion, string Lease, string? EntityType, string? SourceFingerprint, string[] Seeds, string[] Seen, int Attempts);
public sealed partial class MediaStore
{
    private const string MusicListSelect = "SELECT id,source,source_id,kind,name,revision,status,cursor,total,skipped,message,created_at,expires_at,items_json,receipt_json FROM music_list_jobs";
    private static MusicListJob ReadMusicList(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6), r.GetInt32(7),
        r.IsDBNull(8) ? null : r.GetInt32(8), r.GetInt32(9), r.GetString(10), r.GetString(11), r.GetString(12), JsonSerializer.Deserialize<MusicListCandidate[]>(r.GetString(13), WebJson)!, r.IsDBNull(14) ? null : JsonSerializer.Deserialize<MusicListReceipt>(r.GetString(14), WebJson));
    private AccountSecret RequireMusicListAccount(string user)
    {
        var account = GetAccountSecret(user);
        return account is not null && MusicMonitorAllowed(account.Account) ? account : throw new DomainException("Müzik listesi için keşif ve sınıflandırılmamış içerik izni gerekli.", 403);
    }
    public MusicListJob GetMusicListJob(string id, Account actor)
    {
        var account = RequireMusicListAccount(actor.Id); using var db = Open();
        return Query(db, MusicListSelect + " WHERE id=$id AND user_id=$user AND user_version=$version", ReadMusicList, ("$id", id), ("$user", actor.Id), ("$version", account.Version)).FirstOrDefault() ?? throw new DomainException("Müzik listesi işi bulunamadı veya hesap izinleri değişti.", 404);
    }
    public IReadOnlyList<MusicListJob> GetMusicListJobs(Account actor)
    {
        var account = RequireMusicListAccount(actor.Id); using var db = Open(); return Query(db, MusicListSelect + " WHERE user_id=$user AND user_version=$version ORDER BY created_at DESC LIMIT 20", ReadMusicList, ("$user", actor.Id), ("$version", account.Version)).Select(job => job with { Items = [] }).ToArray();
    }
    private (string Name, string Fingerprint, string[] Seeds, int Skipped) PlaylistMusicSeeds(string id, string kind, Account actor)
    {
        var list = GetPlaylists(actor).FirstOrDefault(list => list.Id == id) ?? throw new DomainException("Çalma listesi bulunamadı.", 404);
        var seeds = new HashSet<string>(); var skipped = 0; var evidence = new List<object>(); using var db = Open();
        foreach (var media in GetPlaylistItems(id, actor))
        {
            if (media.Kind != "audio") { skipped++; continue; }
            var row = Query(db, """
                SELECT t.album_id,COALESCE(am.group_mbid,a.release_group_mbid),tm.media_id,am.album_id,t.artist,COALESCE(am.artist,NULLIF(a.artist_display,''),ar.name)
                FROM music_tracks t JOIN music_albums a ON a.id=t.album_id JOIN music_artists ar ON ar.id=a.artist_id
                LEFT JOIN music_album_matches am ON am.album_id=a.id LEFT JOIN music_track_matches tm ON tm.media_id=t.media_id WHERE t.media_id=$id
                """, r => (Album: r.GetString(0), Group: Text(r, 1), TrackMatch: Text(r, 2), AlbumMatch: Text(r, 3), Artist: r.GetString(4), AlbumArtist: r.GetString(5)), ("$id", media.Id)).FirstOrDefault();
            string[] keys = row.Album is null ? [] : kind == "album" ? MusicCredits.ValidId(row.Group) is { } group ? [group] : []
                : ReadMusicCredits(media.Id, false, row.TrackMatch is null ? "tags" : "match", row.Artist).Concat(ReadMusicCredits(row.Album, true, row.AlbumMatch is null ? "tags" : "match", row.AlbumArtist))
                    .Select(credit => credit.Mbid).Where(key => key is not null).Cast<string>().Distinct().Order(StringComparer.Ordinal).ToArray();
            if (keys.Length == 0) skipped++; foreach (var key in keys) seeds.Add(key);
            evidence.Add(new { media.Id, media.ModifiedAt, keys });
        }
        if (seeds.Count > 500) throw new DomainException("Listeden en fazla 500 farklı müzik kimliği aktarılabilir.", 422);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { list.Id, list.UpdatedAt, evidence }, WebJson)));
        return (list.Name, fingerprint, seeds.Order(StringComparer.Ordinal).ToArray(), skipped);
    }
    public MusicListJob QueueMusicList(MusicListInput input, Account actor)
    {
        if (input.Source is not ("collection" or "playlist") || input.Kind is not ("artist" or "album")) throw new DomainException("Müzik listesi kaynağı veya takip türü geçersiz.");
        var source = input.Source == "collection" ? MusicBrainzProvider.Id(input.Id) : AppFiles.Id(input.Id);
        lock (_writeGate)
        {
            var account = RequireMusicListAccount(actor.Id); var local = input.Source == "playlist" ? PlaylistMusicSeeds(source, input.Kind, account.Account) : default;
            using var db = Open(); PruneMusicLists(db);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_list_jobs WHERE status IN ('queued','running','ready')")) >= 32
                || Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_list_jobs WHERE user_id=$user AND status IN ('queued','running','ready')", ("$user", actor.Id))) >= 4) throw new DomainException("Toplu müzik takip kuyruğu dolu; mevcut planı uygula veya iptal et.", 429);
            var id = Guid.NewGuid().ToString("N");
            Execute(db, """
                INSERT INTO music_list_jobs(id,user_id,user_version,source,source_id,kind,name,source_fingerprint,seeds_json,total,skipped,status,created_at,expires_at,next_run,updated_at)
                VALUES($id,$user,$version,$source,$sourceId,$kind,$name,$fingerprint,$seeds,$total,$skipped,'queued',$now,$expires,$now,$now)
                """, ("$id", id), ("$user", actor.Id), ("$version", account.Version), ("$source", input.Source), ("$sourceId", source), ("$kind", input.Kind), ("$name", local.Name ?? "MusicBrainz koleksiyonu"),
                ("$fingerprint", local.Fingerprint), ("$seeds", local.Seeds is null ? null : JsonSerializer.Serialize(local.Seeds)), ("$total", local.Seeds?.Length), ("$skipped", local.Skipped), ("$now", Now()), ("$expires", DateTimeOffset.UtcNow.AddHours(24).ToString("O")));
            return GetMusicListJob(id, account.Account);
        }
    }
    private static void PruneMusicLists(Microsoft.Data.Sqlite.SqliteConnection db)
    {
        Execute(db, "UPDATE music_list_jobs SET status='cancelled',revision=revision+1,lease=NULL,message='Liste planının süresi doldu.' WHERE status IN ('queued','running','ready') AND expires_at<$now; DELETE FROM music_list_jobs WHERE updated_at<$old AND status NOT IN ('queued','running','ready')", ("$now", Now()), ("$old", DateTimeOffset.UtcNow.AddDays(-7).ToString("O")));
        Execute(db, "DELETE FROM music_list_jobs WHERE id IN (SELECT id FROM music_list_jobs WHERE status NOT IN ('queued','running','ready') ORDER BY updated_at DESC LIMIT -1 OFFSET 200)");
    }
    public MusicListWork? ClaimMusicList()
    {
        lock (_writeGate)
        {
            using var db = Open(); PruneMusicLists(db);
            var job = Query(db, MusicListSelect + " WHERE status='queued' AND next_run<=$now ORDER BY next_run,id LIMIT 1", ReadMusicList, ("$now", Now())).FirstOrDefault(); if (job is null) return null;
            var lease = Guid.NewGuid().ToString("N"); Execute(db, "UPDATE music_list_jobs SET status='running',revision=revision+1,lease=$lease WHERE id=$id", ("$id", job.Id), ("$lease", lease));
            var row = Query(db, "SELECT user_id,user_version,entity_type,source_fingerprint,seeds_json,seen_json,attempts FROM music_list_jobs WHERE id=$id",
                r => (User: r.GetString(0), Version: r.GetInt32(1), Type: Text(r, 2), Fingerprint: Text(r, 3), Seeds: r.IsDBNull(4) ? [] : JsonSerializer.Deserialize<string[]>(r.GetString(4))!, Seen: JsonSerializer.Deserialize<string[]>(r.GetString(5))!, Attempts: r.GetInt32(6)), ("$id", job.Id)).Single();
            return new(job with { Status = "running", Revision = job.Revision + 1 }, row.User, row.Version, lease, row.Type, row.Fingerprint, row.Seeds, row.Seen, row.Attempts);
        }
    }
    public Account RequireMusicListWork(MusicListWork work)
    {
        var user = RequireMusicListAccount(work.UserId); using var db = Open();
        if (user.Version != work.UserVersion || Scalar(db, "SELECT id FROM music_list_jobs WHERE id=$id AND status='running' AND revision=$revision AND lease=$lease AND expires_at>$now",
            ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$now", Now())) is not string) throw new OperationCanceledException();
        if (work.Job.Source == "playlist" && PlaylistMusicSeeds(work.Job.SourceId, work.Job.Kind, user.Account).Fingerprint != work.SourceFingerprint) throw new DomainException("Çalma listesi, parça kimlikleri veya erişim değişti; yeni plan oluştur.", 409);
        return user.Account;
    }
    public void SaveMusicListStep(MusicListWork work, MusicListCandidate[] added, string[] entityIds, int total, int next, MusicCollection? collection = null, bool complete = false)
    {
        lock (_writeGate)
        {
            _ = RequireMusicListWork(work);
            if (total is < 0 or > 500 || work.Job.Total is { } previous && previous != total || entityIds.Distinct().Count() != entityIds.Length || entityIds.Any(work.Seen.Contains)
                || next < work.Job.Cursor || next > total) throw new DomainException("Kaynak koleksiyonu arada değişti veya tekrarlı; yeniden incele.", 409);
            var items = work.Job.Items.Concat(added).DistinctBy(item => item.Mbid).ToArray(); if (items.Length > 500) throw new DomainException("Listeden en fazla 500 farklı kimlik aktarılabilir.", 422);
            foreach (var item in items) if (item.Kind != work.Job.Kind || MusicCredits.ValidId(item.Mbid) is null || item.Name.Length > 500 || item.Album?.Id != (item.Kind == "album" ? item.Mbid : null) || item.Artist?.Id != (item.Kind == "artist" ? item.Mbid : null)) throw new DomainException("Liste adayının sağlayıcı kimliği eksik.", 502);
            var itemJson = JsonSerializer.Serialize(items, WebJson);
            if (System.Text.Encoding.UTF8.GetByteCount(itemJson) > 4 * 1024 * 1024) throw new DomainException("Liste metadata snapshot'ı 4 MiB sınırını aşıyor.", 422);
            using var db = Open(); Execute(db, """
                UPDATE music_list_jobs SET name=COALESCE($name,name),entity_type=COALESCE($type,entity_type),total=$total,cursor=$cursor,items_json=$items,seen_json=$seen,status=$status,lease=NULL,attempts=0,updated_at=$now,next_run=$now,
                  message=$message WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'
                """, ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$name", collection?.Name), ("$type", collection?.EntityType), ("$total", total), ("$cursor", next),
                ("$items", itemJson), ("$seen", JsonSerializer.Serialize(work.Seen.Concat(entityIds))), ("$status", complete ? "ready" : "queued"), ("$now", Now()), ("$message", complete ? "Liste hazır; takip edilecek kayıtları seç." : "Gerçek sağlayıcı kimlikleri okunuyor."));
        }
    }
    public void SetMusicListCollection(MusicListWork work, MusicCollection collection)
    {
        lock (_writeGate)
        {
            _ = RequireMusicListWork(work); if (work.Job.Kind == "album" && collection.EntityType == "artist") throw new DomainException("Sanatçı koleksiyonundan sanatçı takibi seç.", 422);
            using var db = Open(); Execute(db, "UPDATE music_list_jobs SET name=$name,entity_type=$type,status='queued',lease=NULL,next_run=$now,updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'",
                ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$name", collection.Name), ("$type", collection.EntityType), ("$now", Now()));
        }
    }
    public void FailMusicList(MusicListWork work, string message, bool retry)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, "UPDATE music_list_jobs SET status=$status,lease=NULL,attempts=attempts+1,next_run=$next,updated_at=$now,message=$message WHERE id=$id AND revision=$revision AND lease=$lease AND status='running'",
                ("$id", work.Job.Id), ("$revision", work.Job.Revision), ("$lease", work.Lease), ("$status", retry && work.Attempts < 3 ? "queued" : "failed"), ("$next", DateTimeOffset.UtcNow.AddSeconds(60 * (work.Attempts + 1)).ToString("O")), ("$now", Now()), ("$message", message));
        }
    }
    public void CancelMusicList(string id, int revision, Account actor)
    {
        lock (_writeGate)
        {
            var job = GetMusicListJob(id, actor); if (job.Revision != revision) throw new DomainException("Liste işi değişti.", 409);
            using var db = Open(); Execute(db, "UPDATE music_list_jobs SET status='cancelled',revision=revision+1,lease=NULL,message='Liste aktarımı iptal edildi.',updated_at=$now WHERE id=$id AND status IN ('queued','running','ready','failed')", ("$id", id), ("$now", Now()));
        }
    }
    public void RecoverMusicLists()
    { lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE music_list_jobs SET status='queued',revision=revision+1,lease=NULL,next_run=$now,message='Kesilen liste aktarımı kaldığı yerden devam ediyor.' WHERE status='running'", ("$now", Now())); PruneMusicLists(db); } }
    public MusicListReceipt ApplyMusicList(string id, MusicListApply input, Account actor)
    {
        if (input.Mbids is not { Length: > 0 and <= 500 } || input.Mbids.Distinct().Count() != input.Mbids.Length) throw new DomainException("1–500 farklı liste kaydı seç.");
        lock (_writeGate)
        {
            var account = RequireMusicListAccount(actor.Id); var job = GetMusicListJob(id, account.Account); using var db = Open();
            var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input with { Mbids = input.Mbids.Order(StringComparer.Ordinal).ToArray() }, WebJson)));
            if (job.Status == "applied") return Scalar(db, "SELECT request_hash FROM music_list_jobs WHERE id=$id", ("$id", id)) as string == hash ? job.Receipt! : throw new DomainException("Uygulanan liste farklı seçimle tekrarlanamaz.", 409);
            if (job.Status != "ready" || job.Revision != input.Revision || DateTimeOffset.Parse(job.ExpiresAt) <= DateTimeOffset.UtcNow) throw new DomainException("Liste hazır değil veya önizleme eskidi.", 409);
            if (job.Source == "playlist" && PlaylistMusicSeeds(job.SourceId, job.Kind, account.Account).Fingerprint != Scalar(db, "SELECT source_fingerprint FROM music_list_jobs WHERE id=$id", ("$id", id)) as string) throw new DomainException("Çalma listesi veya kimlikler değişti; yeniden incele.", 409);
            var selected = input.Mbids.Select(mbid => job.Items.SingleOrDefault(item => item.Mbid == mbid) ?? throw new DomainException("Seçilen kayıt önizlemede yok.", 409)).ToArray();
            var follows = job.Kind == "artist" ? FollowedMusicArtists(actor.Id).Select(artist => artist.Mbid!).ToHashSet() : GetFollowedMusicAlbums(actor).Select(album => album.GroupId).ToHashSet();
            var monitors = GetMusicMonitors(actor.Id); var newItems = selected.Count(item => !follows.Contains(item.Mbid)); var newMonitors = input.Monitor ? selected.Count(item => !monitors.Any(monitor => monitor.Kind == item.Kind && monitor.Mbid == item.Mbid)) : 0;
            if (follows.Count + newItems > 1000 || monitors.Count + newMonitors > 100) throw new DomainException("Seçim 1000 liste kaydı veya 100 otomatik takip sınırını aşıyor; daha az kayıt seç.", 409);
            using var tx = db.BeginTransaction();
            foreach (var item in selected)
            {
                if (item.Kind == "artist") MusicSql(db, tx, "INSERT OR IGNORE INTO music_follows(user_id,artist_mbid,name,created_at) VALUES($user,$id,$name,$now)", ("$user", actor.Id), ("$id", item.Mbid), ("$name", item.Name), ("$now", Now()));
                else MusicSql(db, tx, "INSERT OR IGNORE INTO music_album_follows(user_id,group_mbid,metadata_json,created_at) VALUES($user,$id,$json,$now)", ("$user", actor.Id), ("$id", item.Mbid), ("$json", JsonSerializer.Serialize(item.Album!, WebJson)), ("$now", Now()));
                if (!input.Monitor || monitors.Any(monitor => monitor.Kind == item.Kind && monitor.Mbid == item.Mbid)) continue;
                var settings = MusicMonitorPolicy.Validate(new(Enabled: true, Mode: item.Kind == "artist" ? "future" : "all"));
                MusicSql(db, tx, "INSERT INTO music_monitors(id,user_id,user_version,kind,mbid,name,settings_json,enabled,created_at,next_run) VALUES($id,$user,$version,$kind,$mbid,$name,$settings,1,$now,$now)",
                    ("$id", Guid.NewGuid().ToString("N")), ("$user", actor.Id), ("$version", account.Version), ("$kind", item.Kind), ("$mbid", item.Mbid), ("$name", item.Name), ("$settings", JsonSerializer.Serialize(settings, WebJson)), ("$now", Now()));
            }
            var result = new MusicListReceipt(newItems, selected.Length - newItems, newMonitors, input.Monitor ? selected.Length - newMonitors : 0);
            MusicSql(db, tx, "UPDATE music_list_jobs SET status='applied',revision=revision+1,receipt_json=$result,request_hash=$hash,updated_at=$now,message='Seçilen müzik kayıtları takip listesine eklendi.' WHERE id=$id", ("$id", id), ("$result", JsonSerializer.Serialize(result, WebJson)), ("$hash", hash), ("$now", Now()));
            tx.Commit(); return result;
        }
    }
}
