using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static void MergeCatalogEpisodeState(SqliteConnection db, SqliteTransaction tx, CatalogMergePreview preview, string actorId)
    {
        var source = preview.SourceId!; var target = preview.TargetId;
        var episodeMap = preview.Episodes.ToDictionary(episode => episode.Id, episode => episode.Targets!);
        var idMap = episodeMap.ToDictionary(pair => pair.Key, pair => pair.Value.Select(map => map.TargetEpisodeId).ToArray());
        var scopes = CaptureEpisodeRequestScopes(db, tx, source);
        foreach (var order in EpisodeRows(db, tx, "SELECT id,entries_json FROM catalog_episode_orders WHERE content_id=$source", r => (Id: r.GetString(0), Items: JsonSerializer.Deserialize<EpisodeOrderItem[]>(r.GetString(1), WebJson)!), ("$source", source)))
        {
            var items = order.Items.Select(item => item with { EpisodeIds = item.EpisodeIds.SelectMany(id => idMap[id]).ToArray(), Coordinates = null }).ToArray();
            EpisodeSql(db, tx, "UPDATE catalog_episode_orders SET content_id=$target,entries_json=$items,revision=revision+1,updated_at=$now WHERE id=$id", ("$target", target), ("$items", JsonSerializer.Serialize(items, WebJson)), ("$id", order.Id), ("$now", Now()));
        }
        var media = EpisodeRows(db, tx, "SELECT media_id,episode_id FROM media_matches WHERE content_id=$source", r => (Id: r.GetString(0), Episode: Text(r, 1)), ("$source", source));
        var segments = EpisodeRows(db, tx, "SELECT s.media_id,s.episode_id,s.position,s.start_seconds,s.end_seconds FROM media_episode_segments s JOIN media_matches m ON m.media_id=s.media_id WHERE m.content_id=$source",
            r => (Media: r.GetString(0), Episode: r.GetString(1), Position: r.GetInt32(2), Start: r.IsDBNull(3) ? (double?)null : r.GetDouble(3), End: r.IsDBNull(4) ? (double?)null : r.GetDouble(4)), ("$source", source));
        EpisodeSql(db, tx, "UPDATE movie_editions SET content_id=$target,revision=revision+1 WHERE content_id=$source", ("$target", target), ("$source", source));
        foreach (var item in media) EpisodeSql(db, tx, "UPDATE media_matches SET content_id=$target,episode_id=$episode,revision=revision+1 WHERE media_id=$id", ("$id", item.Id), ("$target", target), ("$episode", item.Episode is null ? null : episodeMap[item.Episode][0].TargetEpisodeId));
        foreach (var group in segments.GroupBy(segment => segment.Media))
        {
            EpisodeSql(db, tx, "DELETE FROM media_episode_segments WHERE media_id=$id", ("$id", group.Key)); var position = 0;
            foreach (var segment in group.OrderBy(segment => segment.Position)) foreach (var map in episodeMap[segment.Episode])
            {
                var single = episodeMap[segment.Episode].Length == 1;
                var start = single ? segment.Start : segment.Start is { } offset && map.StartSeconds is { } value ? offset + value : (double?)null;
                var end = single ? segment.End : segment.Start is { } baseTime && map.EndSeconds is { } last ? baseTime + last : (double?)null;
                EpisodeSql(db, tx, "INSERT INTO media_episode_segments(media_id,episode_id,position,start_seconds,end_seconds) VALUES($media,$episode,$position,$start,$end)",
                    ("$media", segment.Media), ("$episode", map.TargetEpisodeId), ("$position", position++), ("$start", start), ("$end", end));
            }
        }
        var aliases = EpisodeRows(db, tx, "SELECT a.provider,a.provider_id,a.retired,t.episode_id,t.position,t.source_start,t.source_end FROM episode_aliases a JOIN episode_alias_targets t ON t.provider=a.provider AND t.provider_id=a.provider_id WHERE a.content_id=$source ORDER BY a.provider,a.provider_id,t.position",
            r => (Provider: r.GetString(0), External: r.GetInt32(1), Retired: r.GetBoolean(2), Episode: r.GetString(3), Position: r.GetInt32(4), Start: r.IsDBNull(5) ? (double?)null : r.GetDouble(5), End: r.IsDBNull(6) ? (double?)null : r.GetDouble(6)), ("$source", source));
        foreach (var alias in aliases.GroupBy(row => (row.Provider, row.External)))
        {
            if (alias.Any(row => !episodeMap.ContainsKey(row.Episode))) continue;
            var targets = alias.SelectMany(row => episodeMap[row.Episode].Select(map => (Id: map.TargetEpisodeId,
                Start: episodeMap[row.Episode].Length == 1 ? row.Start : alias.Count() == 1 ? map.StartSeconds : null,
                End: episodeMap[row.Episode].Length == 1 ? row.End : alias.Count() == 1 ? map.EndSeconds : null))).ToArray();
            WriteEpisodeAlias(db, tx, alias.Key.Provider, alias.Key.External, target, targets);
            if (alias.First().Retired) EpisodeSql(db, tx, "UPDATE episode_aliases SET retired=1 WHERE provider=$provider AND provider_id=$external", ("$provider", alias.Key.Provider), ("$external", alias.Key.External));
        }
        foreach (var file in EpisodeRows(db, tx, "SELECT f.download_id,f.relative_path,f.episode_id,f.episodes_json FROM acquisition_files f JOIN acquisition_links a ON a.download_id=f.download_id WHERE a.content_id=$source", r => (Download: r.GetString(0), Path: r.GetString(1), Episode: Text(r, 2), Episodes: Text(r, 3)), ("$source", source)))
        {
            var oldIds = file.Episodes is null ? file.Episode is null ? [] : new[] { file.Episode } : JsonSerializer.Deserialize<string[]>(file.Episodes, WebJson)!;
            var ids = oldIds.SelectMany(id => idMap.GetValueOrDefault(id) ?? [id]).Distinct().ToArray();
            EpisodeSql(db, tx, "UPDATE acquisition_files SET episode_id=$episode,episodes_json=$json WHERE download_id=$download AND relative_path=$path", ("$episode", ids.FirstOrDefault()), ("$json", JsonSerializer.Serialize(ids, WebJson)), ("$download", file.Download), ("$path", file.Path));
        }
        foreach (var row in EpisodeRows(db, tx, "SELECT user_id,episode_key,position_seconds,watched,updated_at,event_client,event_sequence,source_media_id FROM content_watch_state WHERE content_id=$source",
            r => (User: r.GetString(0), Key: r.GetString(1), Position: r.GetDouble(2), Watched: r.GetBoolean(3), At: r.GetString(4), Client: Text(r, 5), Sequence: r.IsDBNull(6) ? (long?)null : r.GetInt64(6), Media: Text(r, 7)), ("$source", source)))
        {
            var targets = row.Key.Length == 0 ? [new CatalogEpisodeMap("", "")] : episodeMap.GetValueOrDefault(row.Key) ?? [];
            foreach (var map in targets)
            {
                if (targets.Length > 1 && !row.Watched && (map.StartSeconds is null || map.EndSeconds is null || row.Position < map.StartSeconds)) continue;
                var seconds = targets.Length == 1 ? row.Position : map.StartSeconds is { } start && map.EndSeconds is { } end ? Math.Clamp(row.Position - start, 0, end - start) : 0;
                var watched = targets.Length == 1 ? row.Watched : map.StartSeconds is { } first && map.EndSeconds is { } last ? row.Watched && row.Position == 0 || seconds >= (last - first) * .95 : row.Watched;
                EpisodeSql(db, tx, """
                    INSERT INTO content_watch_state(user_id,content_id,episode_key,position_seconds,watched,updated_at,event_client,event_sequence,source_media_id)
                    VALUES($user,$target,$key,$position,$watched,$at,$client,$sequence,$media)
                    ON CONFLICT(user_id,content_id,episode_key) DO UPDATE SET position_seconds=$position,watched=$watched,updated_at=$at,event_client=$client,event_sequence=$sequence,source_media_id=$media WHERE content_watch_state.updated_at<$at
                    """, ("$user", row.User), ("$target", target), ("$key", map.TargetEpisodeId), ("$position", seconds), ("$watched", watched), ("$at", row.At), ("$client", row.Client), ("$sequence", row.Sequence), ("$media", row.Media));
            }
        }
        RebaseEpisodeRequestScopes(db, tx, scopes, target, idMap, actorId);
    }
}
