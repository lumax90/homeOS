using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public MusicPage<MusicLibraryTrack> GetMusicLibraryTracks(Account actor, int offset = 0, string? query = null, string view = "all")
    {
        if (offset is < 0 or > 1000000 || query?.Length > 120 || view is not ("all" or "liked" or "recent")) throw new DomainException("Müzik arama alanları geçersiz.");
        using var db = Open();
        var filter = $"""
            FROM music_tracks mt JOIN music_albums a ON a.id=mt.album_id
            LEFT JOIN music_album_matches am ON am.album_id=a.id LEFT JOIN music_track_matches mm ON mm.media_id=mt.media_id
            JOIN media m ON m.id=mt.media_id JOIN libraries l ON l.id=m.library_id
            LEFT JOIN music_favorites f ON f.media_id=m.id AND f.user_id=$viewer
            LEFT JOIN user_watch_state w ON w.media_id=m.id AND w.user_id=$viewer
            WHERE l.enabled=1 AND l.kind<>'audiobooks' AND {LibraryAccess} AND {MediaAccess}
            AND ($view<>'liked' OR f.media_id IS NOT NULL) AND ($view<>'recent' OR w.media_id IS NOT NULL)
            AND ($query='' OR mt.search_text LIKE $pattern ESCAPE '~' OR am.search_text LIKE $pattern ESCAPE '~' OR COALESCE(mm.title,'') LIKE $pattern ESCAPE '~')
            """;
        var key = MediaPaths.SearchKey(query?.Trim() ?? "");
        (string, object?)[] args = [("$offset", offset), ("$query", key), ("$pattern", "%" + key.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%"), ("$view", view), ..ViewerArgs(actor)];
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) " + filter, args));
        var order = view == "recent" ? "w.updated_at DESC,m.id" : view == "liked" ? "f.created_at DESC,m.id" : "COALESCE(mm.title,mt.title),m.id";
        var rows = Query(db, "SELECT m.id,mt.album_id,COALESCE(mm.title,mt.title),COALESCE(mm.artist,mt.artist),COALESCE(am.title,a.title),COALESCE(mm.disc_number,mt.disc_number),COALESCE(mm.track_number,mt.track_number),f.media_id IS NOT NULL " + filter + " ORDER BY " + order + " LIMIT 60 OFFSET $offset",
            r => (Id:r.GetString(0), AlbumId:r.GetString(1), Title:r.GetString(2), Artist:r.GetString(3), Album:r.GetString(4), Disc:r.GetInt32(5), Number:r.GetInt32(6), Favorite:r.GetBoolean(7)), args);
        var items = rows.Select(row => new MusicLibraryTrack(row.Id, row.AlbumId, row.Title, row.Artist, row.Album, row.Disc, row.Number, row.Favorite, GetMedia(row.Id, actor)!)).Where(row=>row.Media is not null).ToArray();
        return new(items, total, offset, 60);
    }
}
