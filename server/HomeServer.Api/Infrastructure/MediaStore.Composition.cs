using System.Globalization;
using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string MultipleEpisodeFile = "(SELECT COUNT(*) FROM media_episode_segments s WHERE s.media_id=m.id)>1";
    private const string MovieOffset = "COALESCE((SELECT SUM(prior.duration_seconds) FROM movie_parts current JOIN movie_parts p ON p.edition_id=current.edition_id AND p.part_number<current.part_number JOIN media prior ON prior.id=p.media_id WHERE current.media_id=m.id),0)";
    private static string MediaPosition => $"CASE WHEN EXISTS(SELECT 1 FROM movie_parts p WHERE p.media_id=m.id) THEN MAX(0,MIN(COALESCE(m.duration_seconds,31536000),CASE WHEN cw.position_seconds IS NULL THEN COALESCE(w.position_seconds,0) ELSE cw.position_seconds-{MovieOffset} END)) WHEN {MultipleEpisodeFile} THEN COALESCE(w.position_seconds,0) ELSE COALESCE(cw.position_seconds,w.position_seconds,0) END";
    private static string MediaWatched => $"CASE WHEN {MultipleEpisodeFile} THEN COALESCE(w.watched,0) ELSE COALESCE(cw.watched,w.watched,0) END";
    private static string WholeMovieReady => $"""
        (NOT EXISTS(SELECT 1 FROM movie_parts mp WHERE mp.media_id=m.id) OR EXISTS(
          SELECT 1 FROM movie_parts current JOIN movie_editions ed ON ed.id=current.edition_id WHERE current.media_id=m.id
          AND ed.part_count=(SELECT COUNT(*) FROM movie_parts p WHERE p.edition_id=ed.id)
          AND NOT EXISTS(SELECT 1 FROM movie_parts p JOIN media pm ON pm.id=p.media_id JOIN libraries pl ON pl.id=pm.library_id
            WHERE p.edition_id=ed.id AND (pm.available=0 OR pl.enabled=0 OR pm.duration_seconds IS NULL OR pm.duration_seconds<=0 OR NOT {MediaAccessFor("pm")}
              OR NOT ($all=1 OR EXISTS(SELECT 1 FROM user_libraries ul WHERE ul.user_id=$viewer AND ul.library_id=pl.id))))))
        """;
    public MediaItem MatchMediaEpisodes(string mediaId, ProviderContent metadata, IReadOnlyList<(ProviderEpisode Episode, double? Start, double? End)> segments, string edition, int expectedRevision)
    {
        if (metadata.Kind != "show" || segments.Count is < 1 or > 20 || segments.Select(segment => (segment.Episode.SeasonNumber, segment.Episode.EpisodeNumber)).Distinct().Count() != segments.Count) throw new DomainException("Bir dosyada 1–20 farklı bölüm seçilmeli.");
        lock (_writeGate)
        {
            var media = GetMedia(mediaId) ?? throw new DomainException("Dosya bulunamadı.", 404);
            if (media.Kind != "video" || media.MatchRevision != expectedRevision) throw new DomainException("Dosya eşleştirmesi değişti; tekrar yükle.", 409);
            if (edition.Length > 80) throw new DomainException("Sürüm etiketi en fazla 80 karakter olabilir.");
            using var db = Open(); using var tx = db.BeginTransaction(); var contentId = UpsertContent(db, tx, metadata);
            var resolved = new List<(string Id, double? Start, double? End)>();
            foreach (var segment in segments)
            {
                var targets = UpsertEpisodeTargets(db, tx, contentId, segment.Episode, metadata.Provider);
                foreach (var target in targets)
                {
                    if (targets.Length == 1) resolved.Add((target.Id, segment.Start, segment.End));
                    else if (target.Start is { } start && target.End is { } end && (segment.Start is not null || segments.Count == 1) && media.DurationSeconds is > 0)
                    {
                        var offset = segment.Start ?? 0;
                        if (offset + end > (segment.End ?? media.DurationSeconds) + .1) throw new DomainException("Sağlayıcı bölüm zamanları bu dosya aralığına sığmıyor.", 409);
                        resolved.Add((target.Id, offset + start, offset + end));
                    }
                    else resolved.Add((target.Id, null, null));
                }
            }
            if (resolved.Count is < 1 or > 20 || resolved.Select(row => row.Id).Distinct().Count() != resolved.Count) throw new DomainException("Genişleyen bölüm kapsamı 1–20 farklı kanonik bölüm içermeli.", 409);
            var timed = resolved.Any(segment => segment.Start is not null || segment.End is not null);
            if (timed)
            {
                if (media.DurationSeconds is not > 0 || resolved.Any(segment => segment.Start is null || !double.IsFinite(segment.Start.Value) || segment.Start < 0 || segment.End is { } end && !double.IsFinite(end))) throw new DomainException("Zaman eşlemesi için dosya süresi ve her bölümün başlangıcı gerekli.");
                double previous = 0;
                foreach (var segment in resolved)
                {
                    var end = segment.End ?? media.DurationSeconds.Value;
                    if (segment.Start < previous || end <= segment.Start || end > media.DurationSeconds + .1) throw new DomainException("Bölüm aralıkları çakışıyor veya dosya süresini aşıyor.");
                    previous = end;
                }
            }
            using var match = Command(db, """
                INSERT INTO media_matches(media_id,content_id,episode_id,edition,matched_at) VALUES($media,$content,$episode,$edition,$now)
                ON CONFLICT(media_id) DO UPDATE SET content_id=$content,episode_id=$episode,edition=$edition,matched_at=$now,revision=revision+1;
                DELETE FROM media_episode_segments WHERE media_id=$media;
                """, ("$media", mediaId), ("$content", contentId), ("$episode", resolved[0].Id), ("$edition", edition.Trim()), ("$now", Now())); match.Transaction = tx; match.ExecuteNonQuery();
            for (var index = 0; index < resolved.Count; index++)
            {
                var segment = resolved[index]; using var write = Command(db, "INSERT INTO media_episode_segments(media_id,episode_id,position,start_seconds,end_seconds) VALUES($media,$episode,$position,$start,$end)",
                    ("$media", mediaId), ("$episode", segment.Id), ("$position", index), ("$start", timed ? segment.Start : resolved.Count == 1 ? 0 : null), ("$end", timed ? segment.End ?? media.DurationSeconds : null)); write.Transaction = tx; write.ExecuteNonQuery();
            }
            tx.Commit(); ReconcileRequests(contentId); return GetMedia(mediaId)!;
        }
    }
    public MovieEdition SetMovieParts(string contentId, MoviePartsInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || input.MediaIds is null || input.MediaIds.Length is < 2 or > 20 || input.MediaIds.Distinct().Count() != input.MediaIds.Length) throw new DomainException("Film parça grubunda 2–20 farklı dosya ve bir sürüm adı gerekli.");
        lock (_writeGate)
        {
            var content = GetContent(contentId) ?? throw new DomainException("Film bulunamadı.", 404); contentId = content.Id;
            if (content.Metadata.Kind != "movie") throw new DomainException("Parça grubu bir film gerektirir.");
            var files = input.MediaIds.Select(id => GetMedia(id) ?? throw new DomainException("Parça dosyası bulunamadı.", 404)).ToArray();
            if (files.Any(file => file.Content?.Id != contentId || file.Kind != "video" || file.DurationSeconds is not > 0)) throw new DomainException("Tüm parçalar aynı filme eşlenmiş, süresi okunmuş videolar olmalı.");
            if (files.Any(file => file.MoviePart is { } part && part.EditionId != input.EditionId)) throw new DomainException("Bir dosya zaten başka bir film parça grubuna bağlı.", 409);
            var previous = input.EditionId is null ? null : GetMovieEdition(input.EditionId);
            if (previous is not null && previous.ContentId != contentId || (previous?.Revision ?? 0) != input.ExpectedRevision) throw new DomainException("Film parça grubu değişti.", 409);
            var id = previous?.Id ?? Guid.NewGuid().ToString("N"); using var db = Open(); using var tx = db.BeginTransaction();
            foreach (var removed in previous?.Files.Where(file => !input.MediaIds.Contains(file.Id)) ?? [])
            { using var remove = Command(db, "DELETE FROM media_matches WHERE media_id=$id", ("$id", removed.Id)); remove.Transaction = tx; remove.ExecuteNonQuery(); }
            using var write = Command(db, """
                INSERT INTO movie_editions(id,content_id,name,part_count) VALUES($id,$content,$name,$count)
                ON CONFLICT(id) DO UPDATE SET name=$name,part_count=$count,revision=revision+1;
                DELETE FROM movie_parts WHERE edition_id=$id;
                """, ("$id", id), ("$content", contentId), ("$name", input.Name.Trim()), ("$count", files.Length)); write.Transaction = tx; write.ExecuteNonQuery();
            for (var index = 0; index < files.Length; index++)
            {
                using var part = Command(db, "INSERT INTO movie_parts(edition_id,media_id,part_number) VALUES($id,$media,$number); UPDATE media_matches SET edition=$name,revision=revision+1 WHERE media_id=$media",
                    ("$id", id), ("$media", files[index].Id), ("$number", index + 1), ("$name", input.Name.Trim())); part.Transaction = tx; part.ExecuteNonQuery();
            }
            var offset = 0d;
            foreach (var file in files)
            {
                using var rebase = Command(db, """
                    UPDATE content_watch_state SET position_seconds=$offset+COALESCE((SELECT w.position_seconds FROM user_watch_state w WHERE w.user_id=content_watch_state.user_id AND w.media_id=$media),0),
                    watched=CASE WHEN (SELECT COUNT(*) FROM movie_parts p JOIN user_watch_state w ON w.media_id=p.media_id AND w.user_id=content_watch_state.user_id WHERE p.edition_id=$id AND w.watched=1)=$count THEN 1 ELSE 0 END
                    WHERE content_id=$content AND episode_key='' AND source_media_id=$media
                    """, ("$id", id), ("$media", file.Id), ("$content", contentId), ("$offset", offset), ("$count", files.Length)); rebase.Transaction = tx; rebase.ExecuteNonQuery(); offset += file.DurationSeconds!.Value;
            }
            tx.Commit(); ReconcileRequests(contentId); return GetMovieEdition(id)!;
        }
    }
    public MovieEdition? GetMovieEdition(string id, Account? viewer = null)
    {
        using var db = Open(); var row = Query(db, "SELECT content_id,name,revision,part_count FROM movie_editions WHERE id=$id", r => (Content: r.GetString(0), Name: r.GetString(1), Revision: r.GetInt32(2), Count: r.GetInt32(3)), ("$id", id)).FirstOrDefault();
        if (row.Content is null || GetContent(row.Content, viewer) is null) return null;
        var ids = Query(db, "SELECT media_id FROM movie_parts WHERE edition_id=$id ORDER BY part_number", r => r.GetString(0), ("$id", id));
        var files = ids.Select(media => GetMedia(media, viewer)).Where(file => file is not null).Cast<MediaItem>().ToArray();
        return new(id, row.Content, row.Name, row.Revision, row.Count, files, files.Length == row.Count && files.All(file => file.Available && file.DurationSeconds is > 0 && CompositionFileAccessible(file)));
    }
    private bool CompositionFileAccessible(MediaItem media)
    {
        try { _ = MediaPaths.ResolveFile(GetLibrary(media.LibraryId)!.Path, media.RelativePath); return true; }
        catch (Exception ex) when (ex is IOException or DomainException or UnauthorizedAccessException) { return false; }
    }
    public IReadOnlyList<MovieEdition> GetMovieEditions(string contentId, Account? viewer = null)
    {
        contentId = CanonicalContentId(contentId); using var db = Open();
        return Query(db, "SELECT id FROM movie_editions WHERE content_id=$id ORDER BY name,id", r => r.GetString(0), ("$id", contentId)).Select(id => GetMovieEdition(id, viewer)).Where(edition => edition is not null).Cast<MovieEdition>().ToArray();
    }
    public bool MovieFileReady(MediaItem media) => media.MoviePart is null || GetMovieEdition(media.MoviePart.EditionId)?.Complete == true;
    public void RemoveMovieEdition(string id, int revision)
    {
        lock (_writeGate)
        {
            var edition = GetMovieEdition(id) ?? throw new DomainException("Film parça grubu bulunamadı.", 404);
            if (edition.Revision != revision) throw new DomainException("Film parça grubu değişti.", 409);
            using var db = Open(); using var tx = db.BeginTransaction(); using var command = Command(db,
                "DELETE FROM media_matches WHERE media_id IN (SELECT media_id FROM movie_parts WHERE edition_id=$id); DELETE FROM movie_editions WHERE id=$id", ("$id", id)); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); ReconcileRequests(edition.ContentId);
        }
    }
    public VideoSequence GetVideoSequence(string id, Account actor, string? episodeId = null, bool selectedPart = false)
    {
        var media = GetMedia(id, actor) ?? throw new DomainException("Medya bulunamadı.", 404);
        if (media.MoviePart is { } part)
        {
            var edition = GetMovieEdition(part.EditionId, actor)!;
            if (!edition.Complete) throw new DomainException("Filmin bazı parçaları eksik, çevrimdışı veya bu profile kapalı.", 409);
            using var db = Open(); var state = Query(db, "SELECT position_seconds,watched FROM content_watch_state WHERE user_id=$user AND content_id=$content AND episode_key=''", r => (Position: r.GetDouble(0), Watched: r.GetBoolean(1)), ("$user", actor.Id), ("$content", edition.ContentId)).FirstOrDefault();
            var position = state.Watched ? 0 : Math.Min(state.Position, Math.Max(0, edition.Files.Sum(file => file.DurationSeconds!.Value) - .01)); var offset = 0d; var index = 0;
            var items = edition.Files.Select((file, i) =>
            {
                var length = file.DurationSeconds!.Value;
                var selected = position >= offset && position < offset + length; if (selected) index = i;
                var result = file with { PositionSeconds = selected ? position - offset : 0, IsWatched = false }; offset += length; return result;
            }).ToArray();
            if (selectedPart)
            {
                index = part.Number - 1;
                for (var i = 0; i < items.Length; i++) items[i] = items[i] with { PositionSeconds = i == index && !media.IsWatched ? media.PositionSeconds : 0 };
            }
            return new(items, index, edition.Id);
        }
        if (episodeId is not null)
        {
            var segment = media.Episodes?.FirstOrDefault(item => item.EpisodeId == episodeId) ?? throw new DomainException("Bu dosyada seçilen bölüm yok.", 404);
            if (segment.StartSeconds is null) throw new DomainException("Bu çok bölümlü dosyada bölüm başlangıç zamanlarını eşleştir.", 409);
            using var db = Open(); var state = Query(db, "SELECT position_seconds,watched FROM content_watch_state WHERE user_id=$user AND content_id=$content AND episode_key=$episode", r => (Position: r.GetDouble(0), Watched: r.GetBoolean(1)), ("$user", actor.Id), ("$content", media.Content!.Id), ("$episode", episodeId)).FirstOrDefault();
            var position = segment.StartSeconds.Value + (state.Watched ? 0 : state.Position);
            media = media with { PositionSeconds = Math.Min(position, Math.Max(0, (segment.EndSeconds ?? media.DurationSeconds ?? position) - .01)), IsWatched = false };
        }
        return new([media], 0);
    }
    private void SaveComposedProgress(SqliteConnection db, SqliteTransaction tx, MediaItem media, PlaybackProgress input, string user, double position, double? duration)
    {
        if (media.Content is null) return;
        void Save(string episode, double seconds, bool watched)
        {
            using var command = Command(db, """
                INSERT INTO content_watch_state(user_id,content_id,episode_key,position_seconds,watched,updated_at,event_client,event_sequence,source_media_id)
                VALUES($user,$content,$episode,$position,$watched,$now,$client,$sequence,$media)
                ON CONFLICT(user_id,content_id,episode_key) DO UPDATE SET position_seconds=$position,watched=$watched,updated_at=$now,event_client=$client,event_sequence=$sequence,source_media_id=$media
                WHERE $client IS NULL OR content_watch_state.event_client IS NOT $client OR content_watch_state.event_sequence<$sequence
                """, ("$user", user), ("$content", media.Content.Id), ("$episode", episode), ("$position", seconds), ("$watched", watched), ("$now", Now()), ("$client", input.ClientId), ("$sequence", input.Sequence), ("$media", media.Id)); command.Transaction = tx; command.ExecuteNonQuery();
        }
        if (media.MoviePart is { } part)
        {
            var edition = GetMovieEdition(part.EditionId); if (edition is null || edition.Files.Count != edition.PartCount || edition.Files.Any(file => file.DurationSeconds is not > 0)) return;
            var offset = edition.Files.TakeWhile(file => file.Id != media.Id).Sum(file => file.DurationSeconds!.Value);
            Save("", offset + position, part.Number == part.Count && position >= (duration ?? 0) * .95 && duration is > 0); return;
        }
        var segments = media.Episodes ?? [];
        if (segments.Length == 0) { Save("", position, duration is > 0 && position >= duration * .95); return; }
        foreach (var segment in segments)
        {
            if (segment.StartSeconds is null)
            {
                if (duration is > 0 && position >= Math.Max(duration.Value * .995, duration.Value - .5)) Save(segment.EpisodeId, 0, true);
                continue;
            }
            var end = segment.EndSeconds ?? duration;
            if (position < segment.StartSeconds || end is null || end <= segment.StartSeconds) continue;
            var relative = Math.Min(position - segment.StartSeconds.Value, end.Value - segment.StartSeconds.Value);
            Save(segment.EpisodeId, relative, relative >= (end.Value - segment.StartSeconds.Value) * .95);
        }
    }
}
