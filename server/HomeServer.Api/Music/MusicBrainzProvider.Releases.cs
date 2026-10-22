using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Music;

public sealed partial class MusicBrainzProvider
{
    public async Task<MusicPage<MusicBrainzAlbum>> SearchAlbumsAsync(string query, string? artist, int offset, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length is < 2 or > 120 || artist?.Length > 120 || offset is < 0 or > 10000) throw new DomainException("Albüm arama alanları geçersiz.");
        static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var term = "releasegroup:\"" + Escape(query.Trim()) + "\"" + (string.IsNullOrWhiteSpace(artist) ? "" : " AND artist:\"" + Escape(artist.Trim()) + "\"");
        using var json = await GetAsync($"release-group?query={Uri.EscapeDataString(term)}&limit=25&offset={offset}", token);
        return new MusicPage<MusicBrainzAlbum>(json.RootElement.GetProperty("release-groups").EnumerateArray().Select(Album).ToArray(), json.RootElement.GetProperty("count").GetInt32(), offset, 25);
    }
    public async Task<MusicPage<MusicReleaseSummary>> ReleasesAsync(string groupId, int offset, CancellationToken token)
    {
        groupId = Id(groupId); if (offset is < 0 or > 100000) throw new DomainException("Yayın sayfası geçersiz.");
        using var json = await GetAsync($"release?release-group={groupId}&inc=artist-credits+media&limit=25&offset={offset}", token);
        var items = json.RootElement.GetProperty("releases").EnumerateArray().Select(value => ReleaseSummary(value, groupId)).ToArray();
        return new MusicPage<MusicReleaseSummary>(items, json.RootElement.GetProperty("release-count").GetInt32(), offset, 25);
    }
    public async Task<MusicRelease> ReleaseAsync(string id, CancellationToken token)
    {
        id = Id(id);
        using var json = await GetAsync($"release/{id}?inc=recordings+artist-credits+release-groups", token);
        var root = json.RootElement;
        if (Id(Text(root, "id") ?? "") != id) throw new DomainException("Yayın kimliği sağlayıcı yanıtıyla eşleşmedi.", 502);
        var groupId = root.TryGetProperty("release-group", out var group) ? Id(Text(group, "id") ?? "") : throw new DomainException("Release-group kimliği eksik.", 502);
        var metadata = ReleaseSummary(root, groupId); var tracks = new List<MusicReleaseTrack>();
        foreach (var disc in root.GetProperty("media").EnumerateArray())
        {
            var position = Number(disc, "position") ?? throw new DomainException("Disc numarası eksik.", 502);
            if (!disc.TryGetProperty("tracks", out var items)) continue;
            foreach (var track in items.EnumerateArray())
            {
                var recording = track.GetProperty("recording");
                var credits = Credits(track); if (credits.Length == 0) credits = Credits(recording); if (credits.Length == 0) credits = metadata.ArtistCredits ?? [];
                var artist = MusicCredits.Display(credits);
                tracks.Add(new MusicReleaseTrack(Id(Text(track, "id") ?? ""), Id(Text(recording, "id") ?? ""), position,
                    Number(track, "position") ?? throw new DomainException("Parça sırası eksik.", 502), Text(track, "number") ?? "", Text(track, "title") ?? Text(recording, "title") ?? "", artist,
                    Number(track, "length") ?? Number(recording, "length"), credits));
            }
            if (tracks.Count > 2000) throw new DomainException("Müzik yayını 2000 parça sınırını aşıyor.", 413);
        }
        if (tracks.Count == 0 || tracks.GroupBy(track => (track.Disc, track.Position)).Any(group => group.Count() != 1)) throw new DomainException("Yayın parça listesi boş veya çelişkili.", 502);
        return new MusicRelease(metadata with { TrackCount = tracks.Count }, tracks.ToArray());
    }
    private static MusicReleaseSummary ReleaseSummary(JsonElement value, string groupId)
    {
        var formats = new List<string>(); var tracks = 0;
        if (value.TryGetProperty("media", out var media))
            foreach (var disc in media.EnumerateArray()) { if (Text(disc, "format") is { } format) formats.Add(format); tracks += Number(disc, "track-count") ?? 0; }
        return new MusicReleaseSummary(Id(Text(value, "id") ?? ""), groupId, Text(value, "title") ?? "", Credit(value), Text(value, "date"), Text(value, "country"), Text(value, "status"), formats.Distinct().ToArray(), tracks, Credits(value));
    }
    private static string Credit(JsonElement value) => MusicCredits.Display(Credits(value));
    private static MusicArtistCredit[] Credits(JsonElement value)
    {
        if (!value.TryGetProperty("artist-credit", out var array) || array.ValueKind != JsonValueKind.Array) return [];
        if (array.GetArrayLength() > 64) throw new DomainException("Sağlayıcı sanatçı katkı sınırını aşıyor.", 502);
        return MusicCredits.Normalize(array.EnumerateArray().Select(credit =>
        {
            var artist = credit.TryGetProperty("artist", out var entity) && entity.ValueKind == JsonValueKind.Object ? entity : credit;
            var name = Text(artist, "name") ?? Text(credit, "name") ?? ""; var rawId = Text(artist, "id");
            return new MusicArtistCredit(rawId is null ? null : Id(rawId), name, Text(credit, "name") ?? name, Text(credit, "joinphrase") ?? "", Text(artist, "sort-name"));
        }).ToArray(), "");
    }
    private static int? Number(JsonElement value, string key) => value.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var result) && result >= 0 ? result : null;
}
