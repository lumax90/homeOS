using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Identity;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Downloads;

public static class DownloadEndpoints
{
    public static void MapDownloads(this RouteGroupBuilder api)
    {
        api.MapGet("/downloads", (bool? archived, CurrentAccount account, MediaStore store) => Results.Ok(store.GetDownloads(archived ?? false, account.User)));
        api.MapGet("/downloads/settings", (MediaStore store, IConfiguration configuration) => Results.Ok(store.GetTransferDefaults(configuration))).RequireAuthorization("admin");
        api.MapPut("/downloads/settings", async (TransferDefaults input, MediaStore store, TorrentEngine engine, CancellationToken token) =>
        { store.SaveTransferDefaults(input); await engine.ApplyLimitsAsync(token); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/downloads/{id}", (string id, CurrentAccount account, MediaStore store) => Results.Ok(store.RequireDownloadAccess(id, account.User)));
        api.MapGet("/downloads/{id}/files", (string id, CurrentAccount account, MediaStore store, TorrentEngine engine) => { store.RequireDownloadAccess(id, account.User); return Results.Ok(engine.ListFiles(id)); });
        api.MapGet("/downloads/{id}/policy", (string id, CurrentAccount account, MediaStore store) => { store.RequireDownloadAccess(id, account.User); return Results.Ok(new { policy = store.GetTransferPolicy(id), source = store.GetSourceSeedRules(id) }); });
        api.MapPut("/downloads/{id}/policy", async (string id, HttpContext context, CurrentAccount account, MediaStore store, TorrentEngine engine, DownloadWorker worker, CancellationToken token) =>
        {
            DownloadAccess.Require(id, account, context, store);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 4 * 1024 * 1024;
            var input = await context.Request.ReadFromJsonAsync<TransferPolicy>(token) ?? throw new DomainException("Aktarım tercihi gerekli.");
            engine.ValidateSelection(id, input); var saved = store.SaveTransferPolicy(id, input); worker.Interrupt(id); return Results.Ok(saved);
        });
        api.MapPost("/downloads/{id}/clean-staging", async (string id, CurrentAccount account, HttpContext context, MediaStore store, TorrentEngine engine, DownloadWorker worker, CancellationToken token) =>
        {
            DownloadAccess.Require(id, account, context, store);
            var revision = await worker.StopForMaintenanceAsync(id, token); token.ThrowIfCancellationRequested();
            store.CleanDownloadPayload(id, revision, () => engine.CleanPayload(id)); return Results.Ok(store.GetDownload(id));
        });
        api.MapPost("/downloads/{id}/archive", async (string id, CurrentAccount account, HttpContext context, MediaStore store, DownloadWorker worker, CancellationToken token) =>
        { DownloadAccess.Require(id, account, context, store); await worker.StopForMaintenanceAsync(id, token); store.ArchiveDownload(id); return Results.NoContent(); });
        api.MapPost("/downloads/{id}/restore", (string id, CurrentAccount account, HttpContext context, MediaStore store) => { DownloadAccess.Require(id, account, context, store); store.RestoreDownload(id); return Results.NoContent(); });
        api.MapPost("/downloads/magnet", (AddMagnet input, CurrentAccount account, HttpContext context, MediaStore store, TorrentEngine engine, EventHub events) =>
        {
            var download = engine.AddMagnet(input.Magnet, principal: DownloadAccess.Actor(account, context, store));
            events.Publish("downloads.changed");
            return Results.Accepted($"/api/downloads/{download.Id}", download);
        });
        api.MapPost("/downloads/torrent", async (HttpContext context, CurrentAccount account, MediaStore store, TorrentEngine engine, EventHub events, CancellationToken token) =>
        {
            var actor = DownloadAccess.Actor(account, context, store);
            const int max = 4 * 1024 * 1024;
            if (context.Request.ContentLength > max) throw new DomainException("Torrent dosyası en fazla 4 MB olabilir.", 413);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = max;
            var bytes = await MetadataProvider.ReadLimitedAsync(context.Request.Body, max, token);
            var download = engine.AddTorrent(bytes, principal: actor);
            events.Publish("downloads.changed");
            return Results.Accepted($"/api/downloads/{download.Id}", download);
        });
        api.MapPost("/downloads/{id}/control", (string id, DownloadCommand command, CurrentAccount account, HttpContext context, MediaStore store, DownloadWorker worker) =>
        { DownloadAccess.Require(id, account, context, store); return Results.Ok(worker.Control(id, command.Action)); });
    }
}
