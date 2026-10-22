using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string? GetSetting(string key)
    {
        using var db = Open();
        return Scalar(db, "SELECT value FROM settings WHERE key=$key", ("$key", key)) as string;
    }

    public void SetSetting(string key, string value)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value",
                ("$key", key), ("$value", value));
        }
    }

    public MediaItem MatchMedia(string mediaId, ProviderContent content, ProviderEpisode? episode, string edition, int? expectedRevision = null)
    {
        var media = GetMedia(mediaId) ?? throw new DomainException("İçerik bulunamadı.", 404);
        if (media.Kind != "video") throw new DomainException("Film/dizi eşleştirmesi bir video dosyası gerektirir.");
        if (content.Kind == "show" && episode is null) throw new DomainException("Dizi dosyası için sezon ve bölüm bilgisi gerekli.");
        if (content.Kind == "movie" && episode is not null) throw new DomainException("Film bir dizi bölümüyle eşleştirilemez.");
        if (content.Kind == "show" && episode is not null) return MatchMediaEpisodes(mediaId, content, [(episode, null, null)], edition, expectedRevision ?? media.MatchRevision);
        if (edition.Length > 80) throw new DomainException("Sürüm adı en fazla 80 karakter olabilir.");
        lock (_writeGate)
        {
            if (expectedRevision is { } revision && GetMedia(mediaId)?.MatchRevision != revision) throw new DomainException("Dosya eşleştirmesi arada değişti.", 409);
            using var db = Open();
            using var tx = db.BeginTransaction();
            var id = UpsertContent(db, tx, content);
            var episodeId = episode is null ? null : UpsertEpisode(db, tx, id, episode, content.Provider);
            using var link = Command(db, """
                INSERT INTO media_matches(media_id,content_id,episode_id,edition,matched_at)
                VALUES($media,$content,$episode,$edition,$now)
                ON CONFLICT(media_id) DO UPDATE SET content_id=$content,episode_id=$episode,edition=$edition,matched_at=$now,revision=revision+1
                """, ("$media", mediaId), ("$content", id), ("$episode", episodeId), ("$edition", edition.Trim()), ("$now", Now()));
            link.Transaction = tx;
            link.ExecuteNonQuery();
            tx.Commit();
        }
        return GetMedia(mediaId)!;
    }

    public MediaItem UnmatchMedia(string mediaId)
    {
        _ = GetMedia(mediaId) ?? throw new DomainException("İçerik bulunamadı.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "DELETE FROM media_matches WHERE media_id=$id; UPDATE auto_match_jobs SET status='review',message='Kullanıcı eşleştirmeyi kaldırdı.' WHERE media_id=$id;", ("$id", mediaId));
        }
        return GetMedia(mediaId)!;
    }

    public IReadOnlyList<MediaItem> GetVersions(string mediaId, Account? viewer = null)
    {
        var item = GetMedia(mediaId, viewer) ?? throw new DomainException("İçerik bulunamadı.", 404);
        if (item.Content is null) return [item];
        using var db = Open();
        return Query(db, MediaSelect + $"""
             WHERE l.enabled=1 AND {LibraryAccess} AND {MediaAccess} AND mm.content_id=$content
               AND (mm.episode_id IS NULL AND (SELECT episode_id FROM media_matches WHERE media_id=$media) IS NULL
                 OR EXISTS(SELECT 1 FROM media_episode_segments a JOIN media_episode_segments b ON b.episode_id=a.episode_id WHERE a.media_id=$media AND b.media_id=m.id))
             ORDER BY m.available DESC,m.height DESC,m.size_bytes DESC,m.id
            """, ReadMedia, [("$content", item.Content.Id), ("$media", mediaId), ..ViewerArgs(viewer)]);
    }
}
