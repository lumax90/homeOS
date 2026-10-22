using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Identity;
using HomeServer.Downloads;

namespace HomeServer.Sources;

public static class SourceEndpoints
{
    public static void MapSources(this RouteGroupBuilder api)
    {
        api.MapGet("/indexers",(IndexerService service)=>Results.Ok(service.List())).RequireAuthorization("admin");
        api.MapPost("/indexers",(IndexerInput input,IndexerService service)=>Results.Ok(service.Save(null,input))).RequireAuthorization("admin");
        api.MapPut("/indexers/{id}",(string id,IndexerInput input,IndexerService service)=>Results.Ok(service.Save(id,input))).RequireAuthorization("admin");
        api.MapPost("/indexers/{id}/test",async(string id,IndexerService service,MediaStore store,CancellationToken token)=>{store.RequirePublishedIndexer(id);return Results.Ok(await service.TestSetupAsync(id,token));}).RequireAuthorization("admin");
        api.MapPost("/releases/search",async(ReleaseSearchInput input,CurrentAccount account,IndexerService service,CancellationToken token)=> { account.RequireContent(input.ContentId); return Results.Ok(await service.SearchAsync(input,token)); });
        api.MapPost("/releases/{id}/download",async(string id,GrabRelease input,CurrentAccount account,HttpContext context,MediaStore store,IndexerService service,CancellationToken token)=>
        { var actor = DownloadAccess.Actor(account, context, store); account.RequireContent(store.GetRelease(id).ContentId); return Results.Ok(await service.GrabForAccountAsync(id,input.OverrideRejections,actor,token)); });
        api.MapPost("/downloads/{id}/link", (string id, CurrentAccount account, HttpContext context, MediaStore store, EventHub events) =>
        {
            DownloadAccess.Require(id, account, context, store);
            store.RetryAcquisitionLink(id);
            events.Publish("downloads.changed");
            return Results.Accepted(null, new { message = "Katalog bağlantısı yeniden sıraya alındı." });
        });
        api.MapPost("/downloads/{id}/import-profile", (string id, ImportProfileReview input, CurrentAccount account, HttpContext context, MediaStore store, EventHub events) =>
        {
            DownloadAccess.Require(id, account, context, store);
            var result = store.ReviewImportProfile(id, input.QualityProfileId);
            events.Publish("downloads.changed");
            return Results.Accepted($"/api/downloads/{id}", result);
        });
    }
}
