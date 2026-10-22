using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Music;

public sealed partial class MusicBrainzProvider
{
    public async Task<MusicRelation[]> AlbumRelationsAsync(string id, CancellationToken token)
    {
        id = Id(id); using var json = await GetAsync($"release-group/{id}?inc=release-group-rels+recording-rels", token);
        if (Text(json.RootElement, "id") != id) throw new DomainException("Albüm ilişkisi farklı kimliğe ait.", 502);
        return Relations(json.RootElement);
    }
    public async Task<MusicRecording> RecordingAsync(string id, CancellationToken token)
    {
        id = Id(id); using var json = await GetAsync($"recording/{id}?inc=artist-credits+recording-rels+release-group-rels", token); var root = json.RootElement;
        if (Text(root, "id") != id) throw new DomainException("Recording kimliği farklı.", 502);
        return new(id, Text(root, "title") ?? "", root.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.True, Credits(root), Relations(root));
    }
    private static MusicRelation[] Relations(JsonElement root)
    {
        if (!root.TryGetProperty("relations", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        if (rows.GetArrayLength() > 2000) throw new DomainException("Müzik ilişki listesi sınırı aşıyor.", 502);
        var result = new List<MusicRelation>();
        foreach (var relation in rows.EnumerateArray())
        {
            var kind = Text(relation, "target-type");
            if (kind is not ("release-group" or "recording") || !relation.TryGetProperty(kind, out var target)) continue;
            var id = Id(Text(target, "id") ?? "");
            result.Add(new(Text(relation, "type") ?? "", Text(relation, "direction") ?? "", kind, id, Text(target, "title") ?? "", $"https://musicbrainz.org/{kind}/{id}",
                kind == "release-group" ? Album(target) : null, target.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.True));
        }
        return result.DistinctBy(relation => (relation.Type, relation.Direction, relation.TargetType, relation.Id)).ToArray();
    }
    public async Task<MusicCollection> CollectionAsync(string id, CancellationToken token)
    {
        id = Id(id); using var json = await GetAsync("collection/" + id, token); var root = json.RootElement;
        if (root.TryGetProperty("collections", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 1) throw new DomainException("Koleksiyon yanıtı tekil değil.", 502);
            root = list[0];
        }
        if (Text(root, "id") != id) throw new DomainException("Koleksiyon kimliği eşleşmedi.", 502);
        var type = Text(root, "entity-type");
        if (type is not ("artist" or "release-group" or "release")) throw new DomainException("Sanatçı, release-group veya yayın içeren herkese açık bir MusicBrainz koleksiyonu seç.", 422);
        return new(id, Text(root, "name") ?? "MusicBrainz koleksiyonu", type);
    }
    public async Task<MusicCollectionPage> CollectionPageAsync(MusicCollection collection, string kind, int offset, CancellationToken token)
    {
        if (kind is not ("artist" or "album") || collection.EntityType == "artist" && kind != "artist" || offset is < 0 or > 500) throw new DomainException("Koleksiyon takip türü veya sayfası geçersiz.");
        var id = Id(collection.Id); var type = collection.EntityType;
        if (type is not ("artist" or "release-group" or "release")) throw new DomainException("Koleksiyon türü geçersiz.");
        var include = type switch { "release" => "&inc=artist-credits+release-groups", "release-group" => "&inc=artist-credits", _ => "" };
        using var json = await GetAsync($"{type}?collection={id}&limit=50&offset={offset}{include}", token); var root = json.RootElement;
        var count = Number(root, type + "-count") ?? throw new DomainException("Koleksiyon toplamı eksik.", 502);
        if (count > 500 || Number(root, type + "-offset") != offset) throw new DomainException("Koleksiyon sayfası farklı veya 500 kayıt sınırını aşıyor.", 422);
        var rows = root.GetProperty(type + "s");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 50 || rows.GetArrayLength() != Math.Min(50, Math.Max(0, count - offset))) throw new DomainException("Koleksiyon sayfası eksik.", 502);
        var entities = new List<string>(); var candidates = new List<MusicListCandidate>();
        foreach (var row in rows.EnumerateArray())
        {
            entities.Add(Id(Text(row, "id") ?? ""));
            if (type == "artist") { var artist = Artist(row); candidates.Add(new("artist", artist.Id, artist.Name, Artist: artist)); }
            else if (kind == "artist")
                foreach (var credit in Credits(row).Where(credit => credit.Mbid is not null)) candidates.Add(new("artist", credit.Mbid!, credit.Name, Artist: new(credit.Mbid!, credit.Name, credit.SortName, null, null, "musicbrainz", credit.Mbid)));
            else
            {
                var group = type == "release-group" ? row : row.TryGetProperty("release-group", out var releaseGroup) ? releaseGroup : throw new DomainException("Koleksiyondaki yayının release-group kimliği eksik.", 502);
                var album = Album(group); if (album.ArtistCredits is not { Length: > 0 }) { var credits = Credits(row); album = album with { ArtistCredits = credits, Artist = MusicCredits.Display(credits) }; }
                candidates.Add(new("album", album.Id, album.Title, Album: album));
            }
        }
        return new(candidates.DistinctBy(item => item.Mbid).ToArray(), entities.ToArray(), count, offset, 50);
    }
}
