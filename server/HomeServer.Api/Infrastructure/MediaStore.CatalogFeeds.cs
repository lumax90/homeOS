using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string FeedSelect="SELECT id,user_id,provider,kind,external_id,name,collection_id,enabled,request_new,role_filter,profile_id,revision,next_run,last_run,last_error FROM catalog_feeds";
    private static CatalogFeed ReadFeed(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetBoolean(7),r.GetBoolean(8),r.GetString(9),r.GetString(10),r.GetInt32(11),r.GetString(12),Text(r,13),Text(r,14));
    public CatalogFeed? GetCatalogFeed(string id){using var db=Open();return Query(db,FeedSelect+" WHERE id=$id",ReadFeed,("$id",id)).FirstOrDefault();}
    public IReadOnlyList<CatalogFeed> GetCatalogFeeds(Account actor){using var db=Open();return Query(db,FeedSelect+" WHERE user_id=$user ORDER BY name",ReadFeed,("$user",actor.Id));}
    public CatalogFeed SaveCatalogFeed(string? id,CatalogFeedInput input,string name,Account actor)
    {
        if(input.Provider is not("tmdb" or "tvmaze")||input.Kind is not("person" or "collection" or "list")||input.Provider=="tvmaze"&&input.Kind!="person"||!int.TryParse(input.ExternalId,out var number)||number<1||input.RoleFilter is not("all" or "cast" or "director" or "writer" or "crew"))throw new DomainException("Kaynak koleksiyonu ayarları geçersiz.");
        lock(_writeGate)
        {
            var user=GetAccount(actor.Id)??throw new DomainException("Hesap bulunamadı.",403);if(!user.Enabled||!user.IsAdmin&&!user.CanDiscover)throw new DomainException("Keşif yetkisi gerekli.",403);
            if(!GetCollections(user).Any(collection=>collection.Id==input.CollectionId&&collection.Owned))throw new DomainException("Kendi koleksiyonunu seç.",403);_=GetQualityProfile(input.ProfileId);
            var existing=id is null?null:GetCatalogFeed(id)??throw new DomainException("Kaynak liste bulunamadı.",404);if(existing is not null&&existing.UserId!=actor.Id)throw new DomainException("Kaynak liste bulunamadı.",404);
            if(existing is not null&&(existing.Provider!=input.Provider||existing.Kind!=input.Kind||existing.ExternalId!=input.ExternalId||existing.CollectionId!=input.CollectionId))throw new DomainException("Farklı kaynak/koleksiyon için yeni bağlantı oluştur.",409);
            if((existing?.Revision??0)!=input.ExpectedRevision)throw new DomainException("Liste tercihi değişti.",409);if(existing is null&&GetCatalogFeeds(user).Count>=50)throw new DomainException("En fazla 50 kaynak liste bağlanabilir.");
            using var db=Open();id??=Guid.NewGuid().ToString("N");Execute(db,"""
              INSERT INTO catalog_feeds(id,user_id,provider,kind,external_id,name,collection_id,enabled,request_new,role_filter,profile_id,next_run)
              VALUES($id,$user,$provider,$kind,$external,$name,$collection,$enabled,$request,$role,$profile,$now)
              ON CONFLICT(id) DO UPDATE SET enabled=$enabled,request_new=$request,role_filter=$role,profile_id=$profile,revision=revision+1,next_run=$now,last_error=NULL
              """,("$id",id),("$user",actor.Id),("$provider",input.Provider),("$kind",input.Kind),("$external",input.ExternalId),("$name",name[..Math.Min(200,name.Length)]),("$collection",input.CollectionId),("$enabled",input.Enabled),("$request",input.RequestNew),("$role",input.RoleFilter),("$profile",input.ProfileId),("$now",Now()));return GetCatalogFeed(id)!;
        }
    }
    public IReadOnlyList<CatalogFeedItem> GetCatalogFeedItems(string id,Account actor,int offset=0)
    {
        var feed=GetCatalogFeed(id);if(feed?.UserId!=actor.Id)throw new DomainException("Kaynak liste bulunamadı.",404);using var db=Open();return Query(db,"SELECT provider,kind,provider_id,metadata_json,content_id,excluded FROM catalog_feed_items WHERE feed_id=$id ORDER BY first_seen DESC,provider_id LIMIT 100 OFFSET $offset",r=>new CatalogFeedItem(r.GetString(0),r.GetString(1),r.GetInt32(2),JsonSerializer.Deserialize<ProviderContent>(r.GetString(3),WebJson)!,Text(r,4),r.GetBoolean(5)),("$id",id),("$offset",Math.Max(0,offset))).Where(item=>CanSeeProviderContent(item.Metadata,actor)).ToArray();
    }
    public void CatalogFeedException(string id,FeedException input,Account actor)
    {
        lock(_writeGate)
        {
            var feed=GetCatalogFeed(id);if(feed?.UserId!=actor.Id||feed.Revision!=input.Revision)throw new DomainException("Liste bulunamadı veya değişti.",409);
            using var db=Open();Execute(db,"UPDATE catalog_feed_items SET excluded=$excluded WHERE feed_id=$id AND provider=$provider AND kind=$kind AND provider_id=$external; UPDATE catalog_feeds SET revision=revision+1 WHERE id=$id",("$id",id),("$excluded",input.Excluded),("$provider",input.Provider),("$kind",input.Kind),("$external",input.ProviderId));
        }
    }
    public void AddCatalogFeedItem(CatalogFeed feed,ProviderContent metadata,Account actor)
    {
        lock(_writeGate)
        {
            if(GetCatalogFeed(feed.Id) is not{}current||current.Revision!=feed.Revision||current.UserId!=actor.Id)throw new DomainException("Kaynak liste tercihi değişti.",409);
            if(!CanSeeProviderContent(metadata,actor))return;using var db=Open();
            var excluded=Convert.ToInt32(Scalar(db,"SELECT COALESCE(excluded,0) FROM catalog_feed_items WHERE feed_id=$id AND provider=$provider AND kind=$kind AND provider_id=$external",("$id",feed.Id),("$provider",metadata.Provider),("$kind",metadata.Kind),("$external",metadata.ProviderId)))==1;
            var content=excluded?null:SaveContent(metadata,actor);
            Execute(db,"INSERT INTO catalog_feed_items(feed_id,provider,kind,provider_id,metadata_json,content_id,first_seen,last_seen) VALUES($id,$provider,$kind,$external,$json,$content,$now,$now) ON CONFLICT(feed_id,provider,kind,provider_id) DO UPDATE SET metadata_json=$json,content_id=COALESCE($content,content_id),last_seen=$now",("$id",feed.Id),("$provider",metadata.Provider),("$kind",metadata.Kind),("$external",metadata.ProviderId),("$json",JsonSerializer.Serialize(metadata,WebJson)),("$content",content?.Id),("$now",Now()));
            if(content is not null){ChangeCollectionItem(feed.CollectionId,content.Id,actor,false);if(feed.RequestNew)RequestContent(metadata,"all",feed.ProfileId,actor);}
        }
    }
    public void CompleteCatalogFeed(CatalogFeed feed,string? error=null){lock(_writeGate){using var db=Open();Execute(db,"UPDATE catalog_feeds SET last_run=$now,next_run=$next,last_error=$error WHERE id=$id AND revision=$revision",("$id",feed.Id),("$revision",feed.Revision),("$now",Now()),("$next",DateTimeOffset.UtcNow.AddHours(error is null?12:1).ToString("O")),("$error",error));}}
    public IReadOnlyList<CatalogFeed> DueCatalogFeeds(){using var db=Open();return Query(db,FeedSelect+" WHERE enabled=1 AND next_run<=$now ORDER BY next_run LIMIT 5",ReadFeed,("$now",Now()));}
    public void DeleteCatalogFeed(string id,Account actor){lock(_writeGate){using var db=Open();Execute(db,"DELETE FROM catalog_feeds WHERE id=$id AND user_id=$user",("$id",id),("$user",actor.Id));}}
}
