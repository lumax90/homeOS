using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Metadata;

public sealed class CatalogFeedService(MediaStore store,MetadataProvider provider,EventHub events):IProductWorkHandler
{
    public string[] Kinds=>["catalog-feed"];
    public ProductJob Queue(string id,Account actor){var feed=store.GetCatalogFeed(id);if(feed?.UserId!=actor.Id)throw new DomainException("Kaynak liste bulunamadı.",404);return store.QueueProductJob("catalog-feed",id,new{revision=feed.Revision},actor);}
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        var feed=store.GetCatalogFeed(job.ScopeId!)??throw new DomainException("Kaynak liste kaldırıldı.",404);if(job.Input.GetProperty("revision").GetInt32()!=feed.Revision)throw new DomainException("Kaynak liste tercihi değişti.",409);var count=0;
        try
        {
            for(var page=1;page<=50;page++)
            {
                var list=await provider.CatalogFeedPageAsync(feed.Provider,feed.Kind,feed.ExternalId,feed.RoleFilter,page,token);
                foreach(var item in list.Items)
                {
                    token.ThrowIfCancellationRequested();var actor=store.RequireProductJob(job);if(!actor.IsAdmin&&!actor.CanDiscover)throw new DomainException("Keşif izni kaldırıldı.",403);
                    var metadata=await provider.DetailsAsync(item.Provider,item.Kind,item.ProviderId,token);actor=store.RequireProductJob(job);store.AddCatalogFeedItem(feed,metadata,actor);if(++count>1000)throw new DomainException("Kaynak liste 1000 içerik sınırını aşıyor.",413);store.ProductProgress(job,Math.Min(95,count/10),$"{count} gerçek içerik işlendi.");
                }
                if(!list.More)break;
            }
            store.CompleteCatalogFeed(feed);store.FinishProductJob(job,new{items=count},message:$"{count} içerik koleksiyona işlendi; istisnalar korundu.");events.Publish("catalog.changed");
        }
        catch(Exception ex){store.CompleteCatalogFeed(feed,ex is DomainException?ex.Message:"Kaynak liste güncellenemedi.");throw;}
    }
}
public sealed class CatalogFeedScheduler(MediaStore store,CatalogFeedService feeds):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token){while(!token.IsCancellationRequested){foreach(var feed in store.DueCatalogFeeds()){try{if(store.GetAccount(feed.UserId) is {Enabled:true}actor)feeds.Queue(feed.Id,actor);else store.CompleteCatalogFeed(feed,"Hesap etkin değil.");}catch(DomainException ex){store.CompleteCatalogFeed(feed,ex.Message);}}try{await Task.Delay(60000,token);}catch(OperationCanceledException){break;}}}
}
public static class CatalogFeedEndpoints
{
    public static void MapCatalogFeeds(this RouteGroupBuilder api)
    {
        api.MapGet("/people",async(string provider,string q,int? page,CurrentAccount actor,MetadataProvider metadata,CancellationToken token)=>{actor.RequireDiscovery();return Results.Ok(await metadata.SearchPeopleAsync(provider,q,page??1,token));});
        api.MapGet("/people/{provider}/{id:int}",async(string provider,int id,CurrentAccount actor,MetadataProvider metadata,CancellationToken token)=>{actor.RequireDiscovery();return Results.Ok(await metadata.PersonAsync(provider,id,token));});
        api.MapGet("/people/{provider}/{id:int}/works",async(string provider,int id,string? role,int? offset,CurrentAccount actor,MetadataProvider metadata,MediaStore store,CancellationToken token)=>{actor.RequireDiscovery();var rows=await metadata.PersonWorksAsync(provider,id,role??"all",token);return Results.Ok(new{items=rows.Where(row=>store.CanSeeProviderContent(row.Metadata,actor.User)).Skip(Math.Max(0,offset??0)).Take(50),total=rows.Length});});
        api.MapGet("/discovery/{provider}/{kind}/{id:int}/credits",async(string provider,string kind,int id,CurrentAccount actor,MetadataProvider metadata,CancellationToken token)=>{var content=await metadata.DetailsAsync(provider,kind,id,token);actor.RequireMetadata(content);return Results.Ok(await metadata.CreditsAsync(provider,kind,id,token));});
        api.MapGet("/discovery/{provider}/{kind}/{id:int}/media",async(string provider,string kind,int id,bool? trailersOnly,CurrentAccount actor,MetadataProvider metadata,CancellationToken token)=>{var content=await metadata.DetailsAsync(provider,kind,id,token);actor.RequireMetadata(content);return Results.Ok(await metadata.MediaAsync(provider,kind,id,token,trailersOnly==true));});
        api.MapGet("/catalog-feeds",(CurrentAccount actor,MediaStore store)=>Results.Ok(store.GetCatalogFeeds(actor.User)));
        api.MapPost("/catalog-feeds",async(CatalogFeedInput input,CurrentAccount actor,MetadataProvider metadata,MediaStore store,CatalogFeedService feeds,CancellationToken token)=>{actor.RequireDiscovery();var page=await metadata.CatalogFeedPageAsync(input.Provider,input.Kind,input.ExternalId,input.RoleFilter,1,token);var feed=store.SaveCatalogFeed(null,input,page.Name,actor.User);return Results.Ok(new{feed,job=feeds.Queue(feed.Id,actor.User)});});
        api.MapPut("/catalog-feeds/{id}",(string id,CatalogFeedInput input,CurrentAccount actor,MediaStore store)=>{actor.RequireDiscovery();return Results.Ok(store.SaveCatalogFeed(id,input,store.GetCatalogFeed(id)?.Name??"",actor.User));});
        api.MapPost("/catalog-feeds/{id}/refresh",(string id,CurrentAccount actor,CatalogFeedService feeds)=>{actor.RequireDiscovery();return Results.Ok(feeds.Queue(id,actor.User));});
        api.MapGet("/catalog-feeds/{id}/items",(string id,int? offset,CurrentAccount actor,MediaStore store)=>Results.Ok(store.GetCatalogFeedItems(id,actor.User,offset??0)));
        api.MapPut("/catalog-feeds/{id}/exception",(string id,FeedException input,CurrentAccount actor,MediaStore store)=>{store.CatalogFeedException(id,input,actor.User);return Results.NoContent();});
        api.MapDelete("/catalog-feeds/{id}",(string id,CurrentAccount actor,MediaStore store)=>{store.DeleteCatalogFeed(id,actor.User);return Results.NoContent();});
        api.MapGet("/product-jobs",(string? kind,string? scope,CurrentAccount actor,MediaStore store)=>Results.Ok(store.GetProductJobs(actor.User,kind,scope)));
        api.MapPost("/product-jobs/{id}/cancel",(string id,ProductJobCommand input,CurrentAccount actor,MediaStore store)=>{store.CancelProductJob(id,input.Revision,actor.User);return Results.NoContent();});
    }
}
