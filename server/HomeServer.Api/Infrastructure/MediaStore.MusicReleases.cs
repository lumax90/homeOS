using System.Text.Json;
using HomeServer.Core;
using HomeServer.Music;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public MusicAlbum? GetMusicAlbum(string id, Account viewer) => GetMusicAlbums(viewer, 0, albumId: id).Items.FirstOrDefault();
    public MusicMatchPreview PreviewMusicMatch(string albumId, MusicRelease release, Account viewer, MusicTrackAssignment[]? assignments = null)
    {
        _ = GetMusicAlbum(albumId, viewer) ?? throw new DomainException("Albüm bulunamadı.", 404);
        var files = new List<MusicTrack>(); var offset = 0;
        do { var page = GetMusicTracks(albumId, viewer, offset); files.AddRange(page.Items); offset += page.Items.Count; if (page.Items.Count == 0 || offset >= page.Total) break; } while (files.Count < 2001);
        if (files.Count > 2000 || assignments?.Length > 2000 || assignments?.Select(item => item.MediaId).Distinct().Count() != assignments?.Length) throw new DomainException("Albüm eşleştirme boyutu geçersiz.");
        if (assignments?.Any(item => !files.Any(file => file.MediaId == item.MediaId)) == true) throw new DomainException("Eşleştirme başka albümün dosyasını içeriyor.");
        var result = new List<MusicTrackDecision>();
        foreach (var file in files)
        {
            var selected = assignments?.FirstOrDefault(item => item.MediaId == file.MediaId);
            var disc = selected?.Disc ?? file.Disc; var number = selected?.Position ?? file.Number;
            var track = release.Tracks.FirstOrDefault(item => item.Disc == disc && item.Position == number); var reasons = new List<string>();
            if (track is null) reasons.Add("Yayında aynı disc/parça konumu yok.");
            else if (selected is null)
            {
                if (MusicName(file.Title) != MusicName(track.Title)) reasons.Add("Dosya ve yayın parça başlığı farklı; açık eşleştirme seç.");
                if (file.Media.DurationSeconds is { } duration && track.LengthMs is { } length && Math.Abs(duration - length / 1000d) > Math.Max(10, duration * .08))
                    reasons.Add("Parça süreleri belirgin biçimde farklı.");
            }
            result.Add(new MusicTrackDecision(file.MediaId, file.Title, disc, number, track?.RecordingId, track?.Id, reasons.ToArray()));
        }
        var missing = release.Tracks.Count(track => !result.Any(file => file.ReleaseTrackId == track.Id && file.Reasons.Length == 0));
        return new MusicMatchPreview(albumId, release, result.ToArray(), missing, result.Count > 0 && result.All(file => file.Reasons.Length == 0));
    }
    public MusicMatchPreview MatchMusicAlbum(string albumId, MusicRelease release, Account viewer, MusicTrackAssignment[]? assignments)
    {
        lock (_writeGate)
        {
            var preview = PreviewMusicMatch(albumId, release, viewer, assignments);
            if (!preview.CanApply) throw new DomainException("Albümdeki belirsiz parçalara açık yayın konumu seç.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            using var cache = Command(db, """
                INSERT INTO music_release_cache(release_mbid,group_mbid,metadata_json,updated_at) VALUES($release,$group,$json,$now)
                ON CONFLICT(release_mbid) DO UPDATE SET metadata_json=$json,updated_at=$now;
                INSERT INTO music_album_matches(album_id,release_mbid,title,artist,release_date,group_mbid,search_text,matched_at)
                VALUES($album,$release,$title,$artist,$date,$group,$search,$now)
                ON CONFLICT(album_id) DO UPDATE SET release_mbid=$release,title=$title,artist=$artist,release_date=$date,group_mbid=$group,search_text=$search,matched_at=$now;
                DELETE FROM music_track_matches WHERE album_id=$album;
                """, ("$album", albumId), ("$release", release.Metadata.Id), ("$group", release.Metadata.GroupId), ("$json", JsonSerializer.Serialize(release, WebJson)), ("$now", Now()),
                ("$title", release.Metadata.Title), ("$artist", release.Metadata.Artist), ("$date", release.Metadata.Date), ("$search", MediaPaths.SearchKey(release.Metadata.Title + " " + release.Metadata.Artist)));
            cache.Transaction = tx; cache.ExecuteNonQuery();
            var albumCredits = MusicCredits.Normalize(release.Metadata.ArtistCredits, release.Metadata.Artist);
            WriteMusicCredits(db, tx, albumId, true, "match", albumCredits, "provider");
            MusicSql(db, tx, "DELETE FROM music_track_credits WHERE source='match' AND media_id IN (SELECT media_id FROM music_tracks WHERE album_id=$id)", ("$id", albumId));
            foreach (var file in preview.Files)
            {
                var track = release.Tracks.Single(track => track.Id == file.ReleaseTrackId);
                using var map = Command(db, "INSERT INTO music_track_matches(media_id,album_id,release_track_id,recording_mbid,disc_number,track_number,title,artist,search_text) VALUES($media,$album,$track,$recording,$disc,$number,$title,$artist,$search)",
                    ("$media", file.MediaId), ("$album", albumId), ("$track", track.Id), ("$recording", track.RecordingId), ("$disc", track.Disc), ("$number", track.Position), ("$title", track.Title), ("$artist", track.Artist), ("$search", MediaPaths.SearchKey(track.Title + " " + track.Artist + " " + release.Metadata.Title)));
                map.Transaction = tx; map.ExecuteNonQuery();
                WriteMusicCredits(db, tx, file.MediaId, false, "match", MusicCredits.Normalize(track.ArtistCredits, track.Artist), "provider");
            }
            tx.Commit(); return preview;
        }
    }
    public MusicRelease? MatchedMusicRelease(string albumId)
    {
        using var db = Open(); var json = Scalar(db, "SELECT c.metadata_json FROM music_album_matches a JOIN music_release_cache c ON c.release_mbid=a.release_mbid WHERE a.album_id=$id", ("$id", albumId)) as string;
        return json is null ? null : JsonSerializer.Deserialize<MusicRelease>(json, WebJson);
    }
    public void UnmatchMusicAlbum(string albumId)
    {
        lock (_writeGate) { using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db, "DELETE FROM music_track_credits WHERE source='match' AND media_id IN (SELECT media_id FROM music_tracks WHERE album_id=$id); DELETE FROM music_album_credits WHERE album_id=$id AND source='match'; DELETE FROM music_track_matches WHERE album_id=$id; DELETE FROM music_album_matches WHERE album_id=$id", ("$id", albumId)); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); }
    }
    public void FollowMusicAlbum(MusicBrainzAlbum album, Account viewer)
    {
        lock (_writeGate)
        {
            _ = RequireMusicListAccount(viewer.Id);
            using var db = Open();
            if (Scalar(db, "SELECT group_mbid FROM music_album_follows WHERE user_id=$user AND group_mbid=$id", ("$user", viewer.Id), ("$id", album.Id)) is null
                && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_album_follows WHERE user_id=$user", ("$user", viewer.Id))) >= 1000) throw new DomainException("Albüm listesi 1000 kayıtla sınırlı.");
            Execute(db, "INSERT INTO music_album_follows(user_id,group_mbid,metadata_json,created_at) VALUES($user,$id,$json,$now) ON CONFLICT(user_id,group_mbid) DO UPDATE SET metadata_json=$json",
                ("$user", viewer.Id), ("$id", album.Id), ("$json", JsonSerializer.Serialize(album, WebJson)), ("$now", Now()));
        }
    }
    public IReadOnlyList<MusicAlbumFollow> GetFollowedMusicAlbums(Account viewer)
    {
        if (!viewer.IsAdmin && viewer.MaximumAge is not null && !viewer.AllowUnrated) return [];
        using var db = Open(); return Query(db, "SELECT group_mbid,metadata_json,created_at FROM music_album_follows WHERE user_id=$user ORDER BY created_at DESC LIMIT 1000",
            r => new MusicAlbumFollow(r.GetString(0), JsonSerializer.Deserialize<MusicBrainzAlbum>(r.GetString(1), WebJson)!, r.GetString(2)), ("$user", viewer.Id));
    }
    public void UnfollowMusicAlbum(string id, string user) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM music_album_follows WHERE user_id=$user AND group_mbid=$id", ("$user", user), ("$id", id)); } }
    private static string MusicName(string value) => new(MediaPaths.SearchKey(value).Where(char.IsLetterOrDigit).ToArray());
}
