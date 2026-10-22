using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Metadata;

public static class PersonalCatalogEndpoints
{
    public static void MapPersonalCatalog(this RouteGroupBuilder api)
    {
        api.MapPost("/content/save", async (ContentRequestInput input, MetadataProvider provider, CurrentAccount actor, MediaStore store, CancellationToken token) =>
        {
            actor.RequireDiscovery(); var data = await provider.DetailsAsync(input.Provider, input.Kind, input.ProviderId, token); actor.RequireMetadata(data);
            return Results.Ok(store.SaveContent(data, actor.User));
        });
        api.MapGet("/content/{id}/personal", (string id, CurrentAccount actor, MediaStore store) => { actor.RequireContent(id); return Results.Ok(store.GetPersonalContent(id, actor.User)); });
        api.MapPut("/content/{id}/personal", (string id, PersonalContent input, CurrentAccount actor, MediaStore store) => { actor.RequireContent(id); store.SetPersonalContent(id, actor.User, input); return Results.NoContent(); });
        api.MapGet("/favorites", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetFavorites(actor.User)));
        api.MapGet("/content/{id}/presentation", (string id, MediaStore store) => Results.Ok(store.GetContentPresentation(id))).RequireAuthorization("admin");
        api.MapPut("/content/{id}/presentation", (string id, ContentPresentation input, MediaStore store, EventHub events) =>
        { store.SetContentPresentation(id, input); events.Publish("catalog.changed"); return Results.Ok(store.GetContent(id)); }).RequireAuthorization("admin");
        api.MapGet("/media/{id}/access", (string id, MediaStore store) => Results.Ok(store.GetMediaAccess(id))).RequireAuthorization("admin");
        api.MapPut("/media/{id}/access", (string id, MediaAccessInput input, MediaStore store, EventHub events) => { store.SetMediaAccess(id, input); events.Publish("catalog.changed"); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/collections", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetCollections(actor.User)));
        api.MapPost("/collections", (CollectionInput input, CurrentAccount actor, MediaStore store) => Results.Ok(store.SaveCollection(null, input, actor.User)));
        api.MapPut("/collections/{id}", (string id, CollectionInput input, CurrentAccount actor, MediaStore store) => Results.Ok(store.SaveCollection(id, input, actor.User)));
        api.MapDelete("/collections/{id}", (string id, CurrentAccount actor, MediaStore store) => { store.DeleteCollection(id, actor.User); return Results.NoContent(); });
        api.MapGet("/collections/{id}/items", (string id, CurrentAccount actor, MediaStore store) => Results.Ok(store.GetCollectionItems(id, actor.User)));
        api.MapPost("/collections/{id}/items", (string id, CollectionItemInput input, CurrentAccount actor, MediaStore store) =>
        { actor.RequireContent(input.ContentId); store.ChangeCollectionItem(id, input.ContentId, actor.User, false); return Results.NoContent(); });
        api.MapDelete("/collections/{id}/items/{contentId}", (string id, string contentId, CurrentAccount actor, MediaStore store) =>
        { store.ChangeCollectionItem(id, contentId, actor.User, true); return Results.NoContent(); });
    }
}
