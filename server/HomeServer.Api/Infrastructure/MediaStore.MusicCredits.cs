using HomeServer.Core;
using HomeServer.Music;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static int MusicSql(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; return command.ExecuteNonQuery(); }
    private static object? MusicValue(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; var value = command.ExecuteScalar(); return value is DBNull ? null : value; }
    private static List<T> MusicRows<T>(SqliteConnection db, SqliteTransaction? tx, string sql, Func<SqliteDataReader, T> read, params (string, object?)[] args)
    { using var command = Command(db, sql, args); command.Transaction = tx; using var reader = command.ExecuteReader(); var values = new List<T>(); while (reader.Read()) values.Add(read(reader)); return values; }
    private static string UpsertMusicCreditArtist(SqliteConnection db, SqliteTransaction tx, MusicArtistCredit credit, string libraryId, string source)
    {
        var key = credit.Mbid is null ? $"credit:{libraryId}:{MediaPaths.SearchKey(credit.Name)}" : "mbid:" + credit.Mbid;
        var id = MusicValue(db, tx, "SELECT id FROM music_artists WHERE local_key=$key OR (mbid=$mbid AND $mbid IS NOT NULL)", ("$key", key), ("$mbid", credit.Mbid)) as string ?? Guid.NewGuid().ToString("N");
        MusicSql(db, tx, """
            INSERT INTO music_artists(id,mbid,name,sort_name,source,local_key) VALUES($id,$mbid,$name,$sort,$source,$key)
            ON CONFLICT(id) DO UPDATE SET name=CASE WHEN $source='musicbrainz' OR music_artists.source<>'musicbrainz' THEN $name ELSE name END,
              sort_name=COALESCE($sort,sort_name),source=CASE WHEN $source='musicbrainz' THEN $source ELSE source END
            """, ("$id", id), ("$mbid", credit.Mbid), ("$name", credit.Name), ("$sort", credit.SortName), ("$source", source), ("$key", key));
        return id;
    }
    private static void WriteMusicCredits(SqliteConnection db, SqliteTransaction tx, string id, bool album, string source, MusicArtistCredit[] credits, string library)
    {
        var table = album ? "music_album_credits" : "music_track_credits"; var column = album ? "album_id" : "media_id";
        MusicSql(db, tx, $"DELETE FROM {table} WHERE {column}=$id AND source=$source", ("$id", id), ("$source", source));
        for (var position = 0; position < credits.Length; position++)
        {
            var credit = credits[position]; var artist = UpsertMusicCreditArtist(db, tx, credit, library, source == "match" ? "musicbrainz" : "tags");
            MusicSql(db, tx, $"INSERT INTO {table}({column},source,position,artist_id,credited_name,join_phrase) VALUES($id,$source,$position,$artist,$name,$join)",
                ("$id", id), ("$source", source), ("$position", position), ("$artist", artist), ("$name", credit.CreditedName), ("$join", credit.JoinPhrase));
        }
    }
    private MusicArtistCredit[] ReadMusicCredits(string id, bool album, string source, string fallback)
    {
        using var db = Open(); var table = album ? "music_album_credits" : "music_track_credits"; var column = album ? "album_id" : "media_id";
        var credits = Query(db, $"SELECT a.mbid,a.name,c.credited_name,c.join_phrase,a.sort_name,a.id FROM {table} c JOIN music_artists a ON a.id=c.artist_id WHERE c.{column}=$id AND c.source=$source ORDER BY c.position",
            r => new MusicArtistCredit(Text(r, 0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r, 4), r.GetString(5)), ("$id", id), ("$source", source)).ToArray();
        return credits.Length > 0 ? credits : MusicCredits.Normalize(null, fallback);
    }
    private const string MusicCreditMembership = """
        SELECT c.artist_id,t.media_id,t.album_id FROM music_track_credits c JOIN music_tracks t ON t.media_id=c.media_id
          LEFT JOIN music_track_matches match ON match.media_id=t.media_id WHERE c.source=CASE WHEN match.media_id IS NULL THEN 'tags' ELSE 'match' END
        UNION SELECT c.artist_id,t.media_id,t.album_id FROM music_album_credits c JOIN music_tracks t ON t.album_id=c.album_id
          LEFT JOIN music_album_matches match ON match.album_id=c.album_id WHERE c.source=CASE WHEN match.album_id IS NULL THEN 'tags' ELSE 'match' END
        """;
    public MusicPage<LocalMusicArtist> GetLocalMusicArtists(Account viewer, int offset = 0, string? search = null)
    {
        offset = Math.Clamp(offset, 0, 100000); using var db = Open(); var key = MediaPaths.SearchKey(search ?? "");
        var select = $"""
            SELECT a.id,a.name,a.sort_name,a.country,a.disambiguation,a.source,a.mbid,COUNT(DISTINCT cm.album_id),COUNT(DISTINCT cm.media_id)
            FROM music_artists a JOIN ({MusicCreditMembership}) cm ON cm.artist_id=a.id JOIN media m ON m.id=cm.media_id JOIN libraries l ON l.id=m.library_id
            WHERE l.enabled=1 AND {LibraryAccess} AND {MediaAccess} AND ($search='' OR a.name LIKE $pattern ESCAPE '~' OR a.sort_name LIKE $pattern ESCAPE '~') GROUP BY a.id
            """;
        var pattern = "%" + (search ?? "").Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%";
        (string, object?)[] args = [("$search", key), ("$pattern", pattern), ("$offset", offset), ..ViewerArgs(viewer)];
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM (" + select + ")", args));
        var items = Query(db, select + " ORDER BY COALESCE(a.sort_name,a.name),a.id LIMIT 60 OFFSET $offset", r => new LocalMusicArtist(new(r.GetString(0), r.GetString(1), Text(r, 2), Text(r, 3), Text(r, 4), r.GetString(5), Text(r, 6)), r.GetInt32(7), r.GetInt32(8)), args);
        return new(items, total, offset, 60);
    }
}
