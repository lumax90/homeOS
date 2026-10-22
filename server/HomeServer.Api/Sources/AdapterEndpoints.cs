using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Sources;
public static class AdapterEndpoints
{
 public static void MapAdapters(this RouteGroupBuilder api)
 {
  var group=api.MapGroup("").RequireAuthorization("admin");
  group.MapGet("/source-adapters",(MediaStore store)=>Results.Ok(SourceAdapterRegistry.Builtins.Concat(store.SourceAdapters())));
  group.MapPost("/source-adapters",async(HttpContext context,MediaStore store,CancellationToken token)=>{if(context.Features.Get<IHttpMaxRequestBodySizeFeature>() is{IsReadOnly:false}feature)feature.MaxRequestBodySize=256*1024;var manifest=await context.Request.ReadFromJsonAsync<SourceAdapterManifest>(token)??throw new DomainException("Adaptör tanımı gerekli.");SourceAdapterRegistry.Validate(manifest);store.SaveSourceAdapter(manifest);return Results.Ok(manifest);});
  group.MapDelete("/source-adapters/{id}",(string id,MediaStore store)=>{store.RemoveSourceAdapter(id);return Results.NoContent();});
  group.MapGet("/indexers/{id}/transport",(string id,SourceTransportService network)=>Results.Ok(network.Public(id)));
  group.MapPut("/indexers/{id}/transport",(string id,SourceTransportInput input,SourceTransportService network)=>{network.Save(id,input);return Results.Ok(network.Public(id));});
  group.MapGet("/indexers/{id}/history",(string id,MediaStore store)=>{_=store.GetIndexer(id);return Results.Ok(store.SourceRequestHistory(id));});
 }
}
