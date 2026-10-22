using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Identity;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Usenet;

public static class UsenetEndpoints
{
    public static void MapUsenet(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/usenet").RequireAuthorization("admin");
        admin.MapGet("/servers", (UsenetService service) => Results.Ok(service.List()));
        admin.MapPost("/servers", async (UsenetServerInput input, UsenetService service) => Results.Ok(await service.SaveAsync(null, input)));
        admin.MapPut("/servers/{id}", async (string id, UsenetServerInput input, UsenetService service) => Results.Ok(await service.SaveAsync(id, input)));
        admin.MapPost("/servers/{id}/test", async (string id, UsenetService service, CancellationToken token) => Results.Ok(await service.TestAsync(id, token)));
        admin.MapGet("/tools", (Par2Tool tool) => Results.Ok(new { par2Available = tool.Executable is not null, par2Path = tool.Executable, archiveEngine = "SharpCompress 0.50.5", formats = new[] { "rar", "zip", "7z" } }));
        admin.MapPut("/tools", (Par2Path input, MediaStore store) =>
        {
            if (!string.IsNullOrEmpty(input.Path) && !File.Exists(input.Path)) throw new DomainException("PAR2 çalıştırılabilir dosyası bulunamadı.");
            store.SetSetting("usenet.par2-path", input.Path ?? ""); return Results.NoContent();
        });
        api.MapPost("/downloads/nzb", async (HttpContext context, CurrentAccount account, MediaStore store, NzbEngine engine, EventHub events, CancellationToken token) =>
        {
            var actor = DownloadAccess.Actor(account, context, store);
            if (context.Request.ContentLength > NzbParser.MaximumBytes) throw new DomainException("NZB dosyası en fazla 8 MiB olabilir.", 413);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = NzbParser.MaximumBytes;
            var bytes = await MetadataProvider.ReadLimitedAsync(context.Request.Body, NzbParser.MaximumBytes, token);
            var download = engine.AddNzb(bytes, principal: actor); events.Publish("downloads.changed"); return Results.Accepted($"/api/downloads/{download.Id}", download);
        }).RequireAuthorization("acquire");
        api.MapPut("/downloads/{id}/archive-password", (string id, ArchivePassword input, CurrentAccount account, HttpContext context, MediaStore store, NzbEngine engine, DownloadWorker worker) =>
        { DownloadAccess.Require(id, account, context, store); engine.SetPassword(id, input.Password); return Results.Ok(worker.Control(id, "resume")); }).RequireAuthorization("acquire");
    }
    public sealed record Par2Path(string? Path);
}
