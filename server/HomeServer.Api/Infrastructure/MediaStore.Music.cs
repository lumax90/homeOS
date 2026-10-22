using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Music;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string? GetMusicLyrics(string mediaId)
    {
        using var db = Open(); return Scalar(db, "SELECT lyrics FROM music_tracks WHERE media_id=$id", ("$id", mediaId)) as string;
    }
    public string? NextMusicIndex()
    {
        using var db = Open(); return Scalar(db, "SELECT m.id FROM media m JOIN libraries l ON l.id=m.library_id LEFT JOIN music_tracks mt ON mt.media_id=m.id LEFT JOIN music_index_state s ON s.media_id=m.id LEFT JOIN media_analysis a ON a.media_id=m.id WHERE m.kind='audio' AND l.kind<>'audiobooks' AND m.available=1 AND m.excluded=0 AND l.enabled=1 AND (mt.media_id IS NULL OR mt.credit_version=0 OR a.schema_version=0) AND (s.next_attempt IS NULL OR s.next_attempt<=$now) ORDER BY m.id LIMIT 1", ("$now", Now())) as string;
    }
    public void FailMusicIndex(string id)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO music_index_state(media_id,next_attempt,last_error) VALUES($id,$next,'Etiketler okunamadı.') ON CONFLICT(media_id) DO UPDATE SET next_attempt=$next,last_error='Etiketler okunamadı.'", ("$id", id), ("$next", DateTimeOffset.UtcNow.AddHours(1).ToString("O"))); }
    }
    public void SaveMusicTags(string mediaId, AudioTags tags)
    {
        lock (_writeGate)
        {
            var media = GetMedia(mediaId);
            if (media is null || media.Kind != "audio") return;
            if(GetLibrary(media.LibraryId)?.Kind=="audiobooks")return;
            var folder = Path.GetDirectoryName(media.RelativePath) ?? "";
            if (Regex.IsMatch(Path.GetFileName(folder), "^(cd|disc|disk)[ _-]?[0-9]{1,2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) folder = Path.GetDirectoryName(folder) ?? "";
            var artist = !string.IsNullOrWhiteSpace(tags.AlbumArtist) ? tags.AlbumArtist : !string.IsNullOrWhiteSpace(tags.Artist) ? tags.Artist : "Sanatçı etiketi yok";
            var title = string.IsNullOrWhiteSpace(tags.Title) ? media.SourceTitle : tags.Title;
            var album = string.IsNullOrWhiteSpace(tags.Album) ? Path.GetFileName(folder) : tags.Album;
            if (string.IsNullOrWhiteSpace(album)) album = "Albüm etiketi yok";
            var trackCredits = MusicCredits.Normalize(tags.ArtistCredits, string.IsNullOrWhiteSpace(tags.Artist) ? artist : tags.Artist, tags.ArtistMbid);
            var albumCredits = MusicCredits.Normalize(tags.AlbumArtistCredits, artist, tags.AlbumArtistMbid);
            if (string.IsNullOrWhiteSpace(tags.AlbumArtist) && tags.AlbumArtistCredits is not { Length: > 0 }) albumCredits = trackCredits;
            var artistKey = $"local:{media.LibraryId}:{MediaPaths.SearchKey(artist)}";
            var albumKey = tags.ReleaseMbid is not null ? "release:" + tags.ReleaseMbid : $"local:{media.LibraryId}:{MediaPaths.Key(folder)}:{MediaPaths.SearchKey(album)}:{artistKey}";
            using var db = Open(); using var tx = db.BeginTransaction();
            var artistId = UpsertMusicCreditArtist(db, tx, albumCredits[0], media.LibraryId, "tags");
            using var albumQuery = Command(db, "SELECT id FROM music_albums WHERE local_key=$key", ("$key", albumKey)); albumQuery.Transaction = tx;
            var albumId = albumQuery.ExecuteScalar() as string ?? Guid.NewGuid().ToString("N");
            var priorAlbum = MusicRows(db, tx, "SELECT a.id,a.local_key FROM music_tracks t JOIN music_albums a ON a.id=t.album_id JOIN music_artists artist ON artist.id=a.artist_id WHERE t.media_id=$id AND a.title=$title AND a.release_mbid IS $release AND (t.credit_version=0 OR COALESCE(NULLIF(a.artist_display,''),artist.name)=$artist)",
                r => (Id: r.GetString(0), Key: r.GetString(1)), ("$id", mediaId), ("$title", album), ("$release", tags.ReleaseMbid), ("$artist", artist)).FirstOrDefault();
            if (priorAlbum.Id is not null) { albumId = priorAlbum.Id; albumKey = priorAlbum.Key; }
            using var existingMatch = Command(db, "SELECT mt.album_id FROM music_tracks mt JOIN music_album_matches mm ON mm.album_id=mt.album_id WHERE mt.media_id=$id", ("$id", mediaId)); existingMatch.Transaction = tx;
            albumId = existingMatch.ExecuteScalar() as string ?? albumId;
            using var preservedKey = Command(db, "SELECT a.local_key FROM music_albums a JOIN music_album_matches mm ON mm.album_id=a.id WHERE a.id=$id", ("$id", albumId)); preservedKey.Transaction = tx;
            albumKey = preservedKey.ExecuteScalar() as string ?? albumKey;
            using var albumWrite = Command(db, """
                INSERT INTO music_albums(id,artist_id,title,release_date,release_group_mbid,release_mbid,local_key,source,artist_display) VALUES($id,$artist,$title,$date,$group,$release,$key,'tags',$display)
                ON CONFLICT(id) DO UPDATE SET artist_id=$artist,title=$title,release_date=$date,release_group_mbid=$group,release_mbid=$release,artist_display=$display
                """, ("$id", albumId), ("$artist", artistId), ("$title", album), ("$date", tags.Date), ("$group", tags.ReleaseGroupMbid), ("$release", tags.ReleaseMbid), ("$key", albumKey), ("$display", artist));
            albumWrite.Transaction = tx; albumWrite.ExecuteNonQuery();
            using var track = Command(db, """
                INSERT INTO music_tracks(media_id,album_id,title,artist,disc_number,track_number,recording_mbid,lyrics,replay_gain,bit_depth,search_text)
                VALUES($media,$album,$title,$artist,$disc,$track,$recording,$lyrics,$gain,$depth,$search)
                ON CONFLICT(media_id) DO UPDATE SET album_id=$album,title=$title,artist=$artist,disc_number=$disc,track_number=$track,recording_mbid=$recording,lyrics=$lyrics,replay_gain=$gain,bit_depth=$depth,search_text=$search
                """, ("$media", mediaId), ("$album", albumId), ("$title", title), ("$artist", string.IsNullOrWhiteSpace(tags.Artist) ? artist : tags.Artist), ("$disc", Math.Max(1, tags.Disc)),
                ("$track", tags.Track), ("$recording", tags.RecordingMbid), ("$lyrics", tags.Lyrics), ("$gain", tags.ReplayGainDb), ("$depth", tags.BitDepth), ("$search", MediaPaths.SearchKey(title + " " + artist + " " + album + " " + string.Join(' ', trackCredits.Select(credit => credit.Name)))));
            track.Transaction = tx; track.ExecuteNonQuery();
            WriteMusicCredits(db, tx, albumId, true, "tags", albumCredits, media.LibraryId); WriteMusicCredits(db, tx, mediaId, false, "tags", trackCredits, media.LibraryId);
            MusicSql(db, tx, "UPDATE music_tracks SET credit_version=1 WHERE media_id=$id; DELETE FROM music_index_state WHERE media_id=$id", ("$id", mediaId)); tx.Commit();
        }
    }
    public MusicPage<MusicAlbum> GetMusicAlbums(Account viewer, int offset, string? search = null, string? albumId = null, string? artistId = null)
    {
        offset = Math.Max(0, offset); using var db = Open();
        var select = $"""
            SELECT a.id,COALESCE(am.artist,NULLIF(a.artist_display,''),ar.name),COALESCE(am.title,a.title),COALESCE(am.release_date,a.release_date),COALESCE(am.group_mbid,a.release_group_mbid),COALESCE(am.release_mbid,a.release_mbid),COUNT(*),SUM(m.available),CASE WHEN am.album_id IS NULL THEN a.source ELSE 'musicbrainz' END,MIN(CASE WHEN m.has_thumbnail=1 THEN m.id END)
            FROM music_albums a JOIN music_artists ar ON ar.id=a.artist_id JOIN music_tracks mt ON mt.album_id=a.id LEFT JOIN music_album_matches am ON am.album_id=a.id
            JOIN media m ON m.id=mt.media_id JOIN libraries l ON l.id=m.library_id WHERE l.enabled=1 AND {LibraryAccess} AND {MediaAccess}
              AND ($album='' OR a.id=$album) AND ($artist='' OR EXISTS(SELECT 1 FROM ({MusicCreditMembership}) member JOIN media m ON m.id=member.media_id JOIN libraries l ON l.id=m.library_id WHERE member.artist_id=$artist AND member.album_id=a.id AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess}))
              AND ($search='' OR mt.search_text LIKE $pattern ESCAPE '~' OR am.search_text LIKE $pattern ESCAPE '~') GROUP BY a.id
            """;
        var key = MediaPaths.SearchKey(search ?? "");
        (string, object?)[] args = [("$search", key), ("$pattern", "%" + key.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%"), ("$offset", offset), ("$album", albumId ?? ""), ("$artist", artistId ?? ""), ..ViewerArgs(viewer)];
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM (" + select + ")", args));
        var rows = Query(db, select + " ORDER BY ar.name,a.release_date,a.title LIMIT 60 OFFSET $offset", r => new MusicAlbum(r.GetString(0), r.GetString(1), r.GetString(2), Text(r, 3), Text(r, 4), Text(r, 5), r.GetInt32(6), r.GetInt32(7), r.GetString(8), Text(r, 9)), args);
        return new MusicPage<MusicAlbum>(rows.Select(row => row with { ArtistCredits = ReadMusicCredits(row.Id, true, row.Source == "musicbrainz" ? "match" : "tags", row.Artist) }).ToArray(), total, offset, 60);
    }
    public MusicPage<MusicTrack> GetMusicTracks(string albumId, Account viewer, int offset = 0)
    {
        using var db = Open(); var filter = $" FROM music_tracks mt LEFT JOIN music_track_matches mm ON mm.media_id=mt.media_id JOIN media m ON m.id=mt.media_id JOIN libraries l ON l.id=m.library_id WHERE mt.album_id=$album AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess}";
        (string, object?)[] args = [("$album", albumId), ("$offset", Math.Max(0, offset)), ..ViewerArgs(viewer)];
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*)" + filter, args));
        var rows = Query(db, "SELECT mt.media_id,mt.album_id,COALESCE(mm.title,mt.title),COALESCE(mm.artist,mt.artist),COALESCE(mm.disc_number,mt.disc_number),COALESCE(mm.track_number,mt.track_number),COALESCE(mm.recording_mbid,mt.recording_mbid),mt.lyrics,mt.replay_gain,mt.bit_depth" + filter + " ORDER BY COALESCE(mm.disc_number,mt.disc_number),COALESCE(mm.track_number,mt.track_number),mt.title LIMIT 200 OFFSET $offset",
            r => (Id: r.GetString(0), Album: r.GetString(1), Title: r.GetString(2), Artist: r.GetString(3), Disc: r.GetInt32(4), Number: r.GetInt32(5), Mbid: Text(r, 6), Lyrics: Text(r, 7), Gain: r.IsDBNull(8) ? (double?)null : r.GetDouble(8), Depth: r.IsDBNull(9) ? (int?)null : r.GetInt32(9)), args);
        return new MusicPage<MusicTrack>(rows.Select(r => new MusicTrack(r.Id, r.Album, r.Title, r.Artist, r.Disc, r.Number, r.Mbid, r.Lyrics, r.Gain, r.Depth, GetMedia(r.Id, viewer)!, ReadMusicCredits(r.Id, false,
            Scalar(db, "SELECT media_id FROM music_track_matches WHERE media_id=$id", ("$id", r.Id)) is string ? "match" : "tags", r.Artist))).ToArray(), total, Math.Max(0, offset), 200);
    }
    public void FollowMusicArtist(string mbid, string name, string user)
    {
        mbid = MusicBrainzProvider.Id(mbid);
        lock (_writeGate)
        {
            _ = RequireMusicListAccount(user); using var db = Open();
            if (Scalar(db, "SELECT artist_mbid FROM music_follows WHERE user_id=$user AND artist_mbid=$id", ("$user", user), ("$id", mbid)) is null
                && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_follows WHERE user_id=$user", ("$user", user))) >= 1000) throw new DomainException("Sanatçı listesi 1000 kayıtla sınırlı.");
            Execute(db, "INSERT OR IGNORE INTO music_follows(user_id,artist_mbid,name,created_at) VALUES($user,$mbid,$name,$now)", ("$user", user), ("$mbid", mbid), ("$name", name), ("$now", Now()));
        }
    }
    public IReadOnlyList<MusicArtist> FollowedMusicArtists(string user)
    {
        using var db = Open(); return Query(db, "SELECT artist_mbid,name FROM music_follows WHERE user_id=$user ORDER BY name", r => new MusicArtist(r.GetString(0), r.GetString(1), null, null, null, "musicbrainz", r.GetString(0)), ("$user", user));
    }
    public void UnfollowMusicArtist(string user, string mbid) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM music_follows WHERE user_id=$user AND artist_mbid=$mbid", ("$user", user), ("$mbid", mbid)); } }
    public MusicPlaylist SavePlaylist(string? id, PlaylistInput input, Account viewer)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || input.MediaIds is null || input.MediaIds.Length > 500) throw new DomainException("Liste adı veya uzunluğu geçersiz; en fazla 500 medya eklenebilir.");
        foreach (var media in input.MediaIds) _ = GetMedia(media, viewer) ?? throw new DomainException("Listedeki bir medyaya erişim yok.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            if (id is not null && Scalar(db, "SELECT id FROM playlists WHERE id=$id AND user_id=$user", ("$id", id), ("$user", viewer.Id)) is not string) throw new DomainException("Liste bulunamadı.", 404);
            if (id is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM playlists WHERE user_id=$user", ("$user", viewer.Id))) >= 100) throw new DomainException("En fazla 100 liste saklanabilir.");
            id ??= Guid.NewGuid().ToString("N"); using var tx = db.BeginTransaction();
            using var command = Command(db, "INSERT INTO playlists(id,user_id,name,updated_at) VALUES($id,$user,$name,$now) ON CONFLICT(id) DO UPDATE SET name=$name,updated_at=$now; DELETE FROM playlist_items WHERE playlist_id=$id;",
                ("$id", id), ("$user", viewer.Id), ("$name", input.Name.Trim()), ("$now", Now())); command.Transaction = tx; command.ExecuteNonQuery();
            for (var index = 0; index < input.MediaIds.Length; index++)
            {
                using var item = Command(db, "INSERT INTO playlist_items(playlist_id,position,media_id) VALUES($id,$position,$media)", ("$id", id), ("$position", index), ("$media", input.MediaIds[index]));
                item.Transaction = tx; item.ExecuteNonQuery();
            }
            tx.Commit(); return new MusicPlaylist(id, input.Name.Trim(), input.MediaIds.Length, Now());
        }
    }
    public IReadOnlyList<MusicPlaylist> GetPlaylists(Account viewer)
    {
        using var db = Open(); return Query(db, $"""
            SELECT p.id,p.name,(SELECT COUNT(*) FROM playlist_items i JOIN media m ON m.id=i.media_id JOIN libraries l ON l.id=m.library_id WHERE i.playlist_id=p.id AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess}),p.updated_at
            FROM playlists p WHERE p.user_id=$viewer ORDER BY p.updated_at DESC
            """, r => new MusicPlaylist(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3)), ViewerArgs(viewer));
    }
    public IReadOnlyList<MediaItem> GetPlaylistItems(string id, Account viewer)
    {
        using var db = Open();
        if (Scalar(db, "SELECT id FROM playlists WHERE id=$id AND user_id=$user", ("$id", id), ("$user", viewer.Id)) is not string) throw new DomainException("Liste bulunamadı.", 404);
        var ids = Query(db, "SELECT media_id FROM playlist_items WHERE playlist_id=$id ORDER BY position", r => r.GetString(0), ("$id", id));
        return ids.Select(media => GetMedia(media, viewer)).Where(media => media is not null).Cast<MediaItem>().ToArray();
    }
    public void DeletePlaylist(string id, string user) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM playlists WHERE id=$id AND user_id=$user", ("$id", id), ("$user", user)); } }
    public void SaveQueue(QueueInput input, Account viewer)
    {
        if (input.ExpectedUserId is not null && input.ExpectedUserId != viewer.Id) throw new DomainException("Profil değişti; eski kuyruk uygulanmadı.", 409);
        if (input.MediaIds is null || input.MediaIds.Length > 500 || input.Index < 0 || input.Index >= Math.Max(1, input.MediaIds.Length) || input.Repeat is not ("off" or "one" or "all")
            || input.ClientId is not null && !Guid.TryParseExact(input.ClientId, "N", out _) || input.Sequence < 0) throw new DomainException("Oynatma kuyruğu geçersiz.");
        foreach (var id in input.MediaIds) _ = GetMedia(id, viewer) ?? throw new DomainException("Kuyruktaki medyaya erişim yok.", 404);
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO playback_queues(user_id,items_json,current_index,shuffle,repeat_mode,updated_at,event_client,event_sequence) VALUES($user,$items,$index,$shuffle,$repeat,$now,$client,$seq) ON CONFLICT(user_id) DO UPDATE SET items_json=$items,current_index=$index,shuffle=$shuffle,repeat_mode=$repeat,updated_at=$now,event_client=$client,event_sequence=$seq WHERE $client IS NULL OR playback_queues.event_client IS NOT $client OR playback_queues.event_sequence<$seq",
            ("$user", viewer.Id), ("$items", JsonSerializer.Serialize(input.MediaIds)), ("$index", input.Index), ("$shuffle", input.Shuffle), ("$repeat", input.Repeat), ("$now", Now()), ("$client", input.ClientId), ("$seq", input.Sequence)); }
    }
    public SavedQueue GetQueue(Account viewer)
    {
        using var db = Open();
        var row = Query(db, "SELECT items_json,current_index,shuffle,repeat_mode FROM playback_queues WHERE user_id=$user", r => (Ids: JsonSerializer.Deserialize<string[]>(r.GetString(0))!, Index: r.GetInt32(1), Shuffle: r.GetBoolean(2), Repeat: r.GetString(3)), ("$user", viewer.Id)).FirstOrDefault();
        if (row.Ids is null) return new SavedQueue([], 0, false, "off");
        var items = row.Ids.Select(id => GetMedia(id, viewer)).Where(item => item is not null).Cast<MediaItem>().ToList();
        var selected = row.Index < row.Ids.Length ? row.Ids[row.Index] : null;
        return new SavedQueue(items, Math.Max(0, items.FindIndex(item => item.Id == selected)), row.Shuffle, row.Repeat);
    }
}
