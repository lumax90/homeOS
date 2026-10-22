using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Sources;

public static class SourceCatalogEndpoints
{
    public static void MapSourceCatalog(this RouteGroupBuilder api)
    {
        var group=api.MapGroup("").RequireAuthorization("admin");
        group.MapGet("/source-catalog",(SourceCatalog catalog)=>Results.Ok(catalog.List()));
        group.MapGet("/indexers/{id}/setup",(string id,SourceSetupService service)=>Results.Ok(service.Edit(id)));
        group.MapPost("/source-setups/test",async(HttpContext context,CurrentAccount actor,SourceSetupService service,CancellationToken token)=>
        {
            if(context.Features.Get<IHttpMaxRequestBodySizeFeature>() is{IsReadOnly:false}feature)feature.MaxRequestBodySize=96*1024;
            var input=await context.Request.ReadFromJsonAsync<SourceSetupInput>(token)??throw new DomainException("Kaynak bağlantısı gerekli.");
            return Results.Ok(await service.TestAsync(input,actor.User.Id,actor.SessionId??throw new DomainException("Oturum gerekli.",401),token));
        });
        group.MapGet("/source-setups/{id}",(string id,CurrentAccount actor,SourceSetupService service)=>Results.Ok(service.Status(id,actor.User.Id,actor.SessionId!)));
        group.MapPost("/source-setups/connect",(SourceSetupConnect input,CurrentAccount actor,SourceSetupService service)=>Results.Ok(service.Connect(input,actor.User.Id,actor.SessionId!)));
        group.MapDelete("/source-setups/{id}",(string id,CurrentAccount actor,MediaStore store)=>{store.CancelSourceSetup(id,actor.User.Id,actor.SessionId!);return Results.NoContent();});
    }
}
