using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string ContentAge = "COALESCE((SELECT cp.minimum_age FROM content_presentation cp WHERE cp.content_id=c.id),c.minimum_age)";
    private const string ContentAccess = "(c.merged_into IS NULL AND ($admin=1 OR (NOT EXISTS(SELECT 1 FROM content_presentation cp WHERE cp.content_id=c.id AND cp.blocked=1) AND ($maxage<0 OR " + ContentAge + "<=$maxage OR (" + ContentAge + " IS NULL AND $unrated=1)))))";
    private static string MediaAccessFor(string alias)
    {
        var age = $"COALESCE({alias}.minimum_age,(SELECT COALESCE(cp.minimum_age,ac.minimum_age) FROM media_matches am JOIN content ac ON ac.id=am.content_id LEFT JOIN content_presentation cp ON cp.content_id=ac.id WHERE am.media_id={alias}.id))";
        return $"({alias}.excluded=0 AND ($admin=1 OR ({alias}.blocked=0 AND NOT EXISTS(SELECT 1 FROM media_matches am JOIN content_presentation cp ON cp.content_id=am.content_id WHERE am.media_id={alias}.id AND cp.blocked=1) AND ($maxage<0 OR {age}<=$maxage OR ({age} IS NULL AND $unrated=1)))))";
    }
    private static string MediaAccess => MediaAccessFor("m");
    private static string ContentBrowseAccess => ContentAccess + $" AND ($admin=1 OR $discover=1 OR EXISTS(SELECT 1 FROM media_matches am JOIN media m ON m.id=am.media_id JOIN libraries l ON l.id=m.library_id WHERE am.content_id=c.id AND l.enabled=1 AND {LibraryAccess} AND {MediaAccess}))";
    public bool CanSeeProviderContent(ProviderContent content, Account viewer)
    {
        if (viewer.IsAdmin) return true;
        var saved = FindContent(content.Provider, content.Kind, content.ProviderId);
        var presentation = saved is not null ? GetContentPresentation(saved.Id) : null;
        if (presentation?.Blocked == true) return false;
        var minimum = presentation?.MinimumAge ?? content.MinimumAge ?? saved?.Metadata.MinimumAge;
        return viewer.MaximumAge is null || (minimum is { } age ? age <= viewer.MaximumAge : viewer.AllowUnrated);
    }
    public ContentEntry SaveContent(ProviderContent content, Account viewer)
    {
        if (!CanSeeProviderContent(content, viewer)) throw new DomainException("Bu içerik profilinin erişim kapsamı dışında.", 403);
        lock (_writeGate)
        {
            using var db = Open(); using var tx = db.BeginTransaction(); var id = UpsertContent(db, tx, content); tx.Commit();
            return GetContent(id, viewer)!;
        }
    }
    public ContentPresentation GetContentPresentation(string id)
    {
        using var db = Open();
        return Query(db, "SELECT title,overview,minimum_age,blocked,tags_json FROM content_presentation WHERE content_id=$id",
            r => new ContentPresentation(Text(r, 0), Text(r, 1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetBoolean(3), JsonSerializer.Deserialize<string[]>(r.GetString(4))), ("$id", id)).FirstOrDefault() ?? new(Tags: []);
    }
    public void SetContentPresentation(string id, ContentPresentation input)
    {
        id = CanonicalContentId(id);
        if (input.Title?.Length > 500 || input.Overview?.Length > 20000 || input.MinimumAge is < 0 or > 21 || input.Tags?.Length > 20 || input.Tags?.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 40) == true)
            throw new DomainException("Yerel başlık, yaş veya etiket alanları geçersiz.");
        _ = GetContent(id) ?? throw new DomainException("İçerik bulunamadı.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                INSERT INTO content_presentation(content_id,title,overview,minimum_age,blocked,tags_json,search_text,updated_at)
                VALUES($id,$title,$overview,$age,$blocked,$tags,$search,$now)
                ON CONFLICT(content_id) DO UPDATE SET title=$title,overview=$overview,minimum_age=$age,blocked=$blocked,tags_json=$tags,search_text=$search,updated_at=$now
                """, ("$id", id), ("$title", string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim()), ("$overview", string.IsNullOrWhiteSpace(input.Overview) ? null : input.Overview.Trim()),
                 ("$age", input.MinimumAge), ("$blocked", input.Blocked), ("$tags", JsonSerializer.Serialize(input.Tags ?? [])), ("$search", MediaPaths.SearchKey((input.Title ?? "") + " " + string.Join(' ', input.Tags ?? []))), ("$now", Now()));
            Execute(db, "UPDATE content SET catalog_revision=catalog_revision+1 WHERE id=$id", ("$id", id));
        }
    }
    public MediaAccessInput GetMediaAccess(string id)
    {
        using var db = Open(); return Query(db, "SELECT minimum_age,blocked FROM media WHERE id=$id", r => new MediaAccessInput(r.IsDBNull(0) ? null : r.GetInt32(0), r.GetBoolean(1)), ("$id", id)).FirstOrDefault() ?? throw new DomainException("Medya bulunamadı.", 404);
    }
    public void SetMediaAccess(string id, MediaAccessInput input)
    {
        if (input.MinimumAge is < 0 or > 21) throw new DomainException("Yaş alanı geçersiz.");
        _ = GetMedia(id) ?? throw new DomainException("Medya bulunamadı.", 404);
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE media SET minimum_age=$age,blocked=$blocked WHERE id=$id", ("$id", id), ("$age", input.MinimumAge), ("$blocked", input.Blocked)); }
    }
    public PersonalContent GetPersonalContent(string id, Account viewer)
    {
        id = CanonicalContentId(id);
        _ = GetContent(id, viewer) ?? throw new DomainException("İçerik bulunamadı.", 404);
        using var db = Open(); return Query(db, "SELECT favorite,rating FROM personal_content WHERE user_id=$user AND content_id=$id",
            r => new PersonalContent(r.GetBoolean(0), r.IsDBNull(1) ? null : r.GetInt32(1)), ("$user", viewer.Id), ("$id", id)).FirstOrDefault() ?? new();
    }
    public void SetPersonalContent(string id, Account viewer, PersonalContent input)
    {
        id = CanonicalContentId(id);
        if (input.Rating is < 1 or > 10) throw new DomainException("Puan 1–10 arasında olmalı.");
        _ = GetContent(id, viewer) ?? throw new DomainException("İçerik bulunamadı.", 404);
        lock (_writeGate) { using var db = Open(); Execute(db, "INSERT INTO personal_content(user_id,content_id,favorite,rating) VALUES($user,$id,$favorite,$rating) ON CONFLICT(user_id,content_id) DO UPDATE SET favorite=$favorite,rating=$rating",
            ("$user", viewer.Id), ("$id", id), ("$favorite", input.Favorite), ("$rating", input.Rating)); }
    }
    public IReadOnlyList<ContentEntry> GetFavorites(Account viewer)
    {
        using var db = Open(); return Query(db, ContentSelect + " WHERE " + ContentBrowseAccess + " AND EXISTS(SELECT 1 FROM personal_content pc WHERE pc.content_id=c.id AND pc.user_id=$viewer AND pc.favorite=1) ORDER BY c.title LIMIT 500", ReadContent, ViewerArgs(viewer));
    }
    public ContentCollection SaveCollection(string? id, CollectionInput input, Account viewer)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || input.Shared && !viewer.IsAdmin) throw new DomainException("Koleksiyon adı veya paylaşım izni geçersiz.");
        lock (_writeGate)
        {
            using var db = Open();
            if (id is not null && Scalar(db, "SELECT id FROM collections WHERE id=$id AND user_id=$user", ("$id", id), ("$user", viewer.Id)) is not string) throw new DomainException("Koleksiyon bulunamadı.", 404);
            if (id is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM collections WHERE user_id=$user", ("$user", viewer.Id))) >= 100) throw new DomainException("Kişi başına koleksiyon sınırı 100.");
            id ??= Guid.NewGuid().ToString("N"); Execute(db, "INSERT INTO collections(id,user_id,name,shared,updated_at) VALUES($id,$user,$name,$shared,$now) ON CONFLICT(id) DO UPDATE SET name=$name,shared=$shared,updated_at=$now",
                ("$id", id), ("$user", viewer.Id), ("$name", input.Name.Trim()), ("$shared", input.Shared), ("$now", Now()));
            return GetCollections(viewer).Single(collection => collection.Id == id);
        }
    }
    public IReadOnlyList<ContentCollection> GetCollections(Account viewer)
    {
        using var db = Open(); return Query(db, $"""
            SELECT k.id,k.name,k.shared,k.user_id=$viewer,k.updated_at,
              (SELECT COUNT(*) FROM collection_items i JOIN content c ON c.id=i.content_id WHERE i.collection_id=k.id AND {ContentBrowseAccess})
            FROM collections k WHERE k.user_id=$viewer OR k.shared=1 ORDER BY k.updated_at DESC
            """, r => new ContentCollection(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.GetInt32(5), r.GetString(4)), ViewerArgs(viewer));
    }
    public IReadOnlyList<ContentEntry> GetCollectionItems(string id, Account viewer)
    {
        using var db = Open();
        if (Scalar(db, "SELECT id FROM collections WHERE id=$id AND (user_id=$user OR shared=1)", ("$id", id), ("$user", viewer.Id)) is not string) throw new DomainException("Koleksiyon bulunamadı.", 404);
        return Query(db, ContentSelect + " WHERE " + ContentBrowseAccess + " AND EXISTS(SELECT 1 FROM collection_items i WHERE i.content_id=c.id AND i.collection_id=$collection) ORDER BY c.title LIMIT 1000", ReadContent, [("$collection", id), ..ViewerArgs(viewer)]);
    }
    public void ChangeCollectionItem(string collectionId, string contentId, Account viewer, bool remove)
    {
        contentId = CanonicalContentId(contentId);
        _ = GetContent(contentId, viewer) ?? throw new DomainException("İçerik bulunamadı.", 404);
        lock (_writeGate)
        {
            using var db = Open();
            if (Scalar(db, "SELECT id FROM collections WHERE id=$id AND user_id=$user", ("$id", collectionId), ("$user", viewer.Id)) is not string) throw new DomainException("Bu koleksiyonu düzenleme iznin yok.", 403);
            if (!remove && Scalar(db,"SELECT content_id FROM collection_items WHERE collection_id=$id AND content_id=$content",("$id",collectionId),("$content",contentId)) is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM collection_items WHERE collection_id=$id", ("$id", collectionId))) >= 1000) throw new DomainException("Koleksiyon 1000 içerikle sınırlı.");
            Execute(db, remove ? "DELETE FROM collection_items WHERE collection_id=$collection AND content_id=$content" : "INSERT OR IGNORE INTO collection_items(collection_id,content_id,created_at) VALUES($collection,$content,$now)",
                ("$collection", collectionId), ("$content", contentId), ("$now", Now()));
            if(remove)Execute(db,"UPDATE catalog_feed_items SET excluded=1 WHERE content_id=$content AND feed_id IN (SELECT id FROM catalog_feeds WHERE collection_id=$collection AND user_id=$user)",("$content",contentId),("$collection",collectionId),("$user",viewer.Id));
        }
    }
    public void DeleteCollection(string id, Account viewer) { lock (_writeGate) { using var db = Open(); Execute(db, "DELETE FROM collections WHERE id=$id AND user_id=$user", ("$id", id), ("$user", viewer.Id)); } }
}
