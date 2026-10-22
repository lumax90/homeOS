using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public static class ReceiverEndpoints
{
    public static void MapReceivers(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet("/receivers/settings", (ReceiverService service) => Results.Ok(service.Settings));
        api.MapPut("/receivers/settings", (ReceiverSettings input, ReceiverService service) => { service.SaveSettings(input); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/receivers/devices", (MediaStore store) => Results.Ok(store.RenderDevices()));
        api.MapPost("/receivers/discover", async (DlnaController controller, CancellationToken token) => Results.Ok(await controller.DiscoverAsync(token))).RequireAuthorization("admin");
        api.MapPost("/receivers/start", async (ReceiverStart input, CurrentAccount actor, MediaStore store, ReceiverService service, HttpContext context, CancellationToken token) =>
            Results.Ok(await service.StartAsync(input, store.GetPlaybackActor(actor.User.Id, actor.SessionId), $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}", token)));
        api.MapGet("/receivers/sessions", (CurrentAccount actor, MediaStore store, ReceiverService service) => Results.Ok(service.Sessions(store.GetPlaybackActor(actor.User.Id, actor.SessionId))));
        api.MapGet("/receivers/{id}/status", (string id, CurrentAccount actor, MediaStore store, ReceiverService service) => Results.Ok(service.Status(id, store.GetPlaybackActor(actor.User.Id, actor.SessionId))));
        api.MapPost("/receivers/{id}/control", async (string id, ReceiverControl input, CurrentAccount actor, MediaStore store, ReceiverService service, CancellationToken token) => Results.Ok(await service.ControlAsync(id, input, store.GetPlaybackActor(actor.User.Id, actor.SessionId), token)));
        api.MapPost("/receivers/media/{id}/prepare", async (string id, CurrentAccount actor, ReceiverService service, CancellationToken token) => Results.Ok(await service.PrepareAsync(id, actor.User, token)));
        app.MapMethods("/receiver/{secret}/{file}", ["GET", "HEAD"], (string secret, string file, ReceiverService service, HttpContext context) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*"; context.Response.Headers.AccessControlExposeHeaders = "Content-Range,Accept-Ranges,Content-Length";
            context.Response.Headers.CacheControl = "private, no-store"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; return service.Media(secret, file);
        }).AllowAnonymous();
        app.MapMethods("/receiver/{secret}/{file}", ["OPTIONS"], (HttpContext context) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*"; context.Response.Headers.AccessControlAllowMethods = "GET,HEAD,OPTIONS";
            context.Response.Headers.AccessControlAllowHeaders = "Range"; return Results.NoContent();
        }).AllowAnonymous();
    }
}
public sealed class ReceiverMonitor(MediaStore store, ReceiverService receivers, ILogger<ReceiverMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Parallel.ForEachAsync(store.Receivers().Where(grant => grant.Kind == "dlna"), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (grant, cancel) => await receivers.RefreshAsync(grant, cancel));
                }
                catch (Exception ex) when (!token.IsCancellationRequested) { logger.LogWarning("Alıcı durumları okunamadı: {Type}", ex.GetType().Name); }
                await Task.Delay(5000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
