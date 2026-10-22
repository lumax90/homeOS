using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public DiscoveryPersonalPage GetDiscoveryPersonal(string kind, string shelf, int offset, int limit, Account viewer)
    {
        if (kind is not ("movie" or "show") || shelf is not ("watchlist" or "history" or "continue") || offset is < 0 or > 5000 || limit is < 1 or > 60)
            throw new DomainException("Kişisel keşif rafı veya sayfası geçersiz.");
        // Administrator permission never broadens the personal-history owner predicate.
        var ownProgress = "EXISTS(SELECT 1 FROM content_watch_state w WHERE w.content_id=c.id AND w.user_id=$viewer AND ";
        var predicate = shelf switch
        {
            "watchlist" => """
                (EXISTS(SELECT 1 FROM personal_content p WHERE p.content_id=c.id AND p.user_id=$viewer AND p.favorite=1)
                OR EXISTS(SELECT 1 FROM content_requests r WHERE r.content_id=c.id AND r.requested_by=$viewer AND r.status IN ('pending','approved','fulfilled'))
                OR EXISTS(SELECT 1 FROM collection_items i JOIN collections k ON k.id=i.collection_id WHERE i.content_id=c.id AND k.user_id=$viewer))
                """,
            "continue" => ownProgress + "w.position_seconds>0 AND w.watched=0)",
            _ => ownProgress + "(w.position_seconds>0 OR w.watched=1))"
        };
        var where = " WHERE c.kind=$kind AND " + ContentBrowseAccess + " AND " + predicate;
        var order = shelf == "watchlist" ? " ORDER BY c.title,c.id" : " ORDER BY (SELECT MAX(w.updated_at) FROM content_watch_state w WHERE w.content_id=c.id AND w.user_id=$viewer) DESC,c.id";
        using var db = Open();
        (string, object?)[] args = [("$kind", kind), ..ViewerArgs(viewer)];
        var total = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM content c" + where, args));
        var items = Query(db, ContentSelect + where + order + " LIMIT $limit OFFSET $offset", ReadContent,
            [("$limit", limit), ("$offset", offset), ..args]);
        return new(items, total, offset, limit);
    }
}
