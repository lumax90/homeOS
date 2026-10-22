using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Operations;

public static class OperationsEndpoints
{
    public static void MapOperations(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/operations").RequireAuthorization("admin");
        group.MapGet("/updates", (UpdateService updates) => Results.Ok(updates.Status()));
        group.MapPost("/updates", async (HttpContext context, UpdateService updates, CancellationToken token) =>
        {
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = PackageTrust.MaximumArchive;
            return Results.Ok(await updates.PrepareAsync(context.Request.Body, token));
        });
        group.MapDelete("/updates", async (UpdateService updates, CancellationToken token) => { await updates.CancelAsync(token); return Results.NoContent(); });
        group.MapPost("/updates/apply", async (UpdateService updates, RestartCoordinator restart, IHostApplicationLifetime lifetime, HttpContext context, CancellationToken token) =>
        {
            await updates.ApplyAsync(token); context.Response.OnCompleted(() => { restart.Requested = true; lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Accepted(null, new { message = "Yedek alındı; doğrulanan sürüm açılıyor." });
        });
        group.MapPost("/updates/rollback", async (UpdateService updates, RestartCoordinator restart, IHostApplicationLifetime lifetime, HttpContext context, CancellationToken token) =>
        {
            await updates.RevertAsync(token); context.Response.OnCompleted(() => { restart.Requested = true; lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Accepted(null, new { message = "Önceki sürüm ve onun veri yedeği hazırlanıyor." });
        });
        group.MapGet("/network", (NetworkConfiguration network, HttpContext context) => Results.Ok(new { current = network.Current, pending = network.Pending,
            certificateConfigured = network.HasCertificate, effectiveHttps = context.Request.IsHttps, effectiveHost = context.Request.Host.ToString(), remoteAddress = context.Connection.RemoteIpAddress?.ToString() }));
        group.MapPut("/network", (NetworkPreferences input, NetworkConfiguration network) => { network.Save(input); return Results.Ok(new { requiresRestart = true }); });
        group.MapPost("/network/certificate", async (HttpContext context, NetworkConfiguration network, CancellationToken token) =>
        {
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 512 * 1024;
            var input = await context.Request.ReadFromJsonAsync<CertificateInput>(token) ?? throw new DomainException("Sertifika verisi gerekli.");
            return Results.Ok(network.SaveCertificate(input));
        });
        group.MapGet("/encoding", async (EncodingSupport encoding, CancellationToken token) => Results.Ok(await encoding.InspectAsync(token)));
        group.MapPut("/encoding/{preference}", (string preference, EncodingSupport encoding) => { encoding.SetPreference(preference); return Results.NoContent(); });
        group.MapGet("/runtime", (RestartCoordinator restart, HomePaths paths) => Results.Ok(new { canRestart = restart.Supported, statePath = paths.State, processId = Environment.ProcessId }));
        group.MapPost("/restart", (RestartCoordinator restart, IHostApplicationLifetime lifetime, HttpContext context) =>
        {
            if (!restart.Supported) throw new DomainException("Bu kurulumda Home'u süreç/container yöneticisinden yeniden başlat.", 409);
            context.Response.OnCompleted(() => { restart.Requested = true; lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Accepted(null, new { message = "Home yeniden başlatılıyor." });
        });
        group.MapGet("/jobs", (MediaStore store) => Results.Ok(store.GetSystemJobs()));
        group.MapGet("/backups", (BackupService backups) => Results.Ok(backups.List()));
        group.MapPost("/backups", (MediaStore store, EventHub events) => { var job = store.QueueBackup(); events.Publish("system.changed"); return Results.Accepted(null, job); });
        group.MapGet("/backups/{id}", (string id, BackupService backups) => Results.File(backups.FilePath(id), "application/zip", $"home-{id}.zip", enableRangeProcessing: true));
        group.MapGet("/restore", (BackupService backups) => Results.Ok(new { pending = backups.PendingRestore() }));
        group.MapPost("/restore", async (HttpContext context, BackupService backups, CancellationToken token) =>
        {
            if (context.Request.ContentLength > BackupService.MaximumArchiveBytes) throw new DomainException("Yedek en fazla 1 GiB olabilir.", 413);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = BackupService.MaximumArchiveBytes;
            var plan = await backups.PrepareRestoreAsync(context.Request.Body, token);
            return Results.Ok(new { plan, requiresRestart = true, message = "Yedek doğrulandı. Sonraki başlangıçta atomik olarak etkinleştirilecek." });
        });
        group.MapDelete("/restore", (BackupService backups) => { backups.CancelRestore(); return Results.NoContent(); });
    }
}
