using System.Text.Json;
using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string CatalogOrderSelect = "SELECT id,content_id,name,kind,source,revision,entries_json FROM catalog_episode_orders";
    private static CatalogEpisodeOrder ReadCatalogOrder(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), JsonSerializer.Deserialize<EpisodeOrderItem[]>(r.GetString(6), WebJson)!);
    public CatalogEpisodeOrder? GetCatalogEpisodeOrder(string id) { using var db = Open(); var order = Query(db, CatalogOrderSelect + " WHERE id=$id", ReadCatalogOrder, ("$id", id)).FirstOrDefault(); return order is null ? null : OrderCoordinates(order); }
    public IReadOnlyList<CatalogEpisodeOrder> GetCatalogEpisodeOrders(string id)
    { id = CanonicalContentId(id); using var db = Open(); return Query(db, CatalogOrderSelect + " WHERE content_id=$id ORDER BY name,id", ReadCatalogOrder, ("$id", id)).Select(OrderCoordinates).ToArray(); }
    private CatalogEpisodeOrder OrderCoordinates(CatalogEpisodeOrder order)
    {
        var catalog = GetEpisodeCatalog(order.ContentId, true).ToDictionary(episode => episode.Id);
        return order with { Items = order.Items.Select(item => item with { Coordinates = item.EpisodeIds.Where(id => catalog.TryGetValue(id, out var row) && !row.Retired).Select(id => new EpisodeCoordinate(catalog[id].Metadata.SeasonNumber, catalog[id].Metadata.EpisodeNumber)).ToArray() }).ToArray() };
    }
    public CatalogEpisodeOrder SaveCatalogEpisodeOrder(string contentId, EpisodeOrderInput input, Account actor, string source = "manual")
    {
        if (input.Items is { Length: > 0 and <= 20000 } && input.Items.Any(item => item.Coordinates is not null))
        {
            lock (_writeGate)
            {
                if (input.Items.Any(item => item.Coordinates is { Length: 0 or > 20 })) throw new DomainException("Her sıra girdisi 1–20 kanonik bölüm içermeli.");
                var catalog = GetEpisodeCatalog(CanonicalContentId(contentId)).ToDictionary(episode => (episode.Metadata.SeasonNumber, episode.Metadata.EpisodeNumber), episode => episode.Id);
                input = input with { Items = input.Items.Select(item => item.Coordinates is null ? item : item with { EpisodeIds = item.Coordinates.Select(coordinate => catalog.GetValueOrDefault((coordinate.Season, coordinate.Episode)) ?? throw new DomainException("Kanonik bölüm koordinatı bulunamadı.", 409)).ToArray(), Coordinates = null }).ToArray() };
            }
        }
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || input.Kind is not ("dvd" or "digital" or "air" or "story" or "production" or "tv" or "custom") || input.Items is not { Length: > 0 and <= 20000 }
            || input.Items.Any(item => item.Season is < 0 or > 1000 || item.Episode is < 1 or > 10000 || item.EpisodeIds is not { Length: > 0 and <= 20 })
            || input.Items.Select(item => (item.Season, item.Episode)).Distinct().Count() != input.Items.Length || input.Items.SelectMany(item => item.EpisodeIds).Distinct().Count() != input.Items.Sum(item => item.EpisodeIds.Length)
            || input.Items.Sum(item => item.EpisodeIds.Length) > 20000) throw new DomainException("Alternatif bölüm sırası eksik, tekrarlı veya sınırı aşıyor.");
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var content = GetContent(contentId, actor) ?? throw new DomainException("Dizi bulunamadı.", 404); contentId = content.Id;
            if (content.Metadata.Kind != "show" || input.ExpectedCatalogRevision != content.CatalogRevision) throw new DomainException("Bölüm kataloğu değişti; sıra seçeneklerini yenile.", 409);
            var episodes = GetEpisodeCatalog(contentId).Select(row => row.Id).ToHashSet();
            if (input.Items.SelectMany(item => item.EpisodeIds).Any(id => !episodes.Contains(id))) throw new DomainException("Sıranın karşılıkları bu dizinin etkin bölümlerinden seçilmeli.", 409);
            var old = input.Id is null ? null : GetCatalogEpisodeOrder(input.Id) ?? throw new DomainException("Bölüm sırası bulunamadı.", 404);
            if (old?.ContentId is { } parent && parent != contentId || (old?.Revision ?? 0) != input.ExpectedRevision) throw new DomainException("Bölüm sırası veya revizyonu değişti.", 409);
            if (old is null && GetCatalogEpisodeOrders(contentId).Count >= 20) throw new DomainException("Dizi başına en fazla 20 alternatif sıra saklanabilir.", 429);
            var id = old?.Id ?? Guid.NewGuid().ToString("N"); var items = input.Items.OrderBy(item => item.Season).ThenBy(item => item.Episode).ToArray();
            using var db = Open(); using var tx = db.BeginTransaction();
            EpisodeSql(db, tx, """
                INSERT INTO catalog_episode_orders(id,content_id,name,kind,source,entries_json,updated_at) VALUES($id,$content,$name,$kind,$source,$json,$now)
                  ON CONFLICT(id) DO UPDATE SET name=$name,kind=$kind,source=$source,entries_json=$json,revision=revision+1,updated_at=$now
                """, ("$id", id), ("$content", contentId), ("$name", input.Name.Trim()), ("$kind", input.Kind), ("$source", source), ("$json", JsonSerializer.Serialize(items, WebJson)), ("$now", Now()));
            if (EpisodeValue(db, tx, "SELECT order_id FROM episode_numbering WHERE content_id=$id", ("$id", contentId)) as string == id)
            { EpisodeSql(db, tx, "UPDATE episode_numbering SET revision=revision+1 WHERE content_id=$id", ("$id", contentId)); InvalidateEpisodeCatalog(db, tx, contentId, false, false); }
            tx.Commit(); return GetCatalogEpisodeOrder(id)!;
        }
    }
    public EpisodeNumbering SelectCatalogEpisodeOrder(string contentId, EpisodeOrderSelection input, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var content = GetContent(contentId, actor) ?? throw new DomainException("Dizi bulunamadı.", 404); contentId = content.Id;
            if (input.OrderId is null) return SaveEpisodeNumbering(contentId, new("seasonal", "", ExpectedRevision: input.ExpectedRevision));
            var order = GetCatalogEpisodeOrder(input.OrderId) ?? throw new DomainException("Bölüm sırası bulunamadı.", 404);
            if (order.ContentId != contentId || GetEpisodeNumbering(contentId).Revision != input.ExpectedRevision) throw new DomainException("Bölüm sırası seçimi değişti.", 409);
            var valid = GetEpisodeCatalog(contentId).Select(row => row.Id).ToHashSet(); if (order.Items.SelectMany(item => item.EpisodeIds).Any(id => !valid.Contains(id))) throw new DomainException("Sırada arşivlenmiş veya taşınmış bölüm var; önce karşılıklarını düzenle.", 409);
            using var db = Open(); using var tx = db.BeginTransaction();
            EpisodeSql(db, tx, """
                INSERT INTO episode_numbering(content_id,mode,source,order_id) VALUES($id,'seasonal',$source,$order)
                  ON CONFLICT(content_id) DO UPDATE SET mode='seasonal',source=$source,order_id=$order,revision=revision+1;
                DELETE FROM episode_order_entries WHERE content_id=$id
                """, ("$id", contentId), ("$source", order.Source), ("$order", order.Id));
            InvalidateEpisodeCatalog(db, tx, contentId, false, false); tx.Commit(); return GetEpisodeNumbering(contentId);
        }
    }
    public void RemoveCatalogEpisodeOrder(string id, int revision, Account actor)
    {
        lock (_writeGate)
        {
            RequireEpisodeAdministrator(actor); var order = GetCatalogEpisodeOrder(id) ?? throw new DomainException("Bölüm sırası bulunamadı.", 404);
            if (order.Revision != revision) throw new DomainException("Bölüm sırası değişti.", 409);
            if (GetEpisodeNumbering(order.ContentId).OrderId == id) throw new DomainException("Etkin sırayı kaldırmadan önce farklı numaralandırma seç.", 409);
            using var db = Open(); Execute(db, "DELETE FROM catalog_episode_orders WHERE id=$id", ("$id", id));
        }
    }
    public CatalogEpisodeOrder ImportCatalogEpisodeOrder(string contentId, ProviderOrderDetails source, ImportCatalogOrder input, Account actor)
    {
        lock (_writeGate)
        {
            var items = new List<EpisodeOrderItem>();
            if (source.Fingerprint.Length > 0 && input.ExpectedSourceFingerprint != source.Fingerprint) throw new DomainException("Sağlayıcı sırası önizlemeden sonra değişti; grubu yeniden yükle.", 409);
            if (input.Provider != "tmdb" || input.OrderId != source.Id || source.Type is < 1 or > 7 or 2 || input.Seasons is null || input.Seasons.Count != source.Groups.Length || input.Seasons.Values.Any(season => season is < 0 or > 1000) || input.Seasons.Values.Distinct().Count() != input.Seasons.Count)
                throw new DomainException("Sağlayıcı gruplarının her birine farklı bir kaynak sezon numarası ata.");
            foreach (var group in source.Groups)
            {
                if (!input.Seasons.TryGetValue(group.Id, out var season)) throw new DomainException("Sağlayıcı sıra grubu için sezon numarası eksik.");
                for (var index = 0; index < group.Episodes.Length; index++) items.Add(new(season, index + 1, ResolveProviderEpisodeIds(contentId, input.Provider, group.Episodes[index])));
            }
            var kind = source.Type switch { 1 => "air", 3 => "dvd", 4 => "digital", 5 => "story", 6 => "production", 7 => "tv", _ => "custom" };
            return SaveCatalogEpisodeOrder(contentId, new(source.Name, kind, items.ToArray(), input.Id, input.ExpectedRevision, input.ExpectedCatalogRevision), actor, "tmdb:" + source.Id);
        }
    }
}
