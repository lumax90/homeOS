using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public static class GameEndpoints
{
    public static void MapGames(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet("/games/native/runtime", (NativeGamingRuntime runtime) => Results.Ok(runtime.Status()));
        api.MapPost("/games/roms/detect",async(CurrentAccount actor,HttpContext context,MediaStore store,RetroArchCoreCatalog catalog,CancellationToken token)=>
        {
            RequireCommandActor(actor,context);var login=actor.SessionId??throw new DomainException("Oturum gerekli.",401);store.RequireRomImportActor(actor.User,login);_ = catalog.Data;
            if(!context.Request.HasJsonContentType())throw new DomainException("ROM algılama isteği JSON olmalı.",415);
            if(context.Request.ContentLength>NativeRomDetection.MaximumRequestBytes)throw new DomainException("ROM algılama örneği çok büyük veya geçersiz.",413);
            var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is{IsReadOnly:false})limit.MaxRequestBodySize=NativeRomDetection.MaximumRequestBytes;
            NativeRomProbeRequest input;try{input=await context.Request.ReadFromJsonAsync<NativeRomProbeRequest>(token)??throw new DomainException("ROM algılama dosya listesi geçersiz.",400);}catch(System.Text.Json.JsonException){throw new DomainException("ROM algılama dosya listesi geçersiz.",400);}
            store.RequireRomImportActor(actor.User,login);return Results.Ok(NativeRomDetection.Detect(input));
        }).RequireRateLimiting("watch-command");
        api.MapPost("/games/roms",async(NativeRomLibrary library,CurrentAccount actor,HttpContext context,EventHub events,GameCatalog games,MediaStore store,RomArtwork artwork,RomMetadata metadata,GameLibraryPreferences libraryPreferences,CancellationToken token)=>
        {
            RequireCommandActor(actor, context);
            var system=context.Request.Headers["X-Home-Rom-System"].ToString();if(system.Length==0)system="gba";var definition=NativeSystems.Require(system);
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if(bodyLimit is{IsReadOnly:false})bodyLimit.MaxRequestBodySize=definition.MaximumBytes+2*1024*1024;
            var encoded = context.Request.Headers["X-Home-Rom-Name"].ToString();
            if (encoded.Length is < 1 or > 1024) throw new DomainException("ROM dosyası adı geçersiz.");
            string name; try { name = Uri.UnescapeDataString(encoded); } catch (UriFormatException) { throw new DomainException("ROM dosyası adı geçersiz."); }
            var login=actor.SessionId??throw new DomainException("Oturum gerekli.",401);GameRom rom;
            if(Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(context.Request.ContentType,out var contentType)&&contentType.MediaType.Equals("multipart/form-data",StringComparison.OrdinalIgnoreCase))rom=await library.ImportDiscAsync(context.Request.Body,Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(contentType.Boundary).Value??"",system,name,actor.User,login,token);
            else{if(!string.Equals(context.Request.ContentType,"application/octet-stream",StringComparison.OrdinalIgnoreCase))throw new DomainException("ROM dosyası ikili veri olarak gönderilmeli.",415);rom=await library.ImportAsync(context.Request.Body,name,context.Request.ContentLength,actor.User,login,token,system);}
            // Metadata queue pressure must not turn a successful durable ROM import into an HTTP error.
            if(libraryPreferences.Read(actor.User.Id).AutomaticMetadata)try{metadata.Queue(rom,actor.User,automatic:true);}catch(DomainException error)when(error.StatusCode==429){}events.Publish("games.changed");return Results.Ok(new{id=rom.Id,rom.Title,rom.System,rom.Sha256,rom.SizeBytes,playable=games.RequireEntry(rom.Id,store,actor.User).Playable});
        }).RequireRateLimiting("watch-command");
        api.MapNativeCores();
        api.MapGet("/games/library/settings",(RomMetadata metadata,CurrentAccount actor)=>Results.Ok(metadata.LibraryStatus(actor.User)));
        api.MapPut("/games/library/settings",(GameLibraryOptionsChange input,GameLibraryPreferences preferences,CurrentAccount actor,HttpContext context)=>{RequireCommandActor(actor,context);return Results.Ok(preferences.Save(actor.User,actor.SessionId??"",input));}).RequireRateLimiting("watch-command");
        api.MapPost("/games/library/refresh",(GameLibraryRefresh input,GameLibraryPreferences preferences,CurrentAccount actor,HttpContext context)=>{RequireCommandActor(actor,context);return Results.Ok(preferences.Refresh(actor.User,actor.SessionId??"",input.Revision));}).RequireRateLimiting("watch-command");
        api.MapGet("/games/{id}/metadata",(string id,RomMetadata metadata,MediaStore store,CurrentAccount actor)=>Results.Ok(metadata.Status(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),actor.User)));
        api.MapPost("/games/{id}/metadata/refresh",(string id,RomMetadata metadata,MediaStore store,CurrentAccount actor,HttpContext context)=>{RequireCommandActor(actor,context);store.RequireRomImportActor(actor.User,actor.SessionId??"");return Results.Ok(metadata.Queue(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),actor.User,true));}).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/metadata/search",async(string id,RomMetadataSearch input,RomMetadata metadata,MediaStore store,CurrentAccount actor,HttpContext context,CancellationToken token)=>{RequireCommandActor(actor,context);return Results.Ok(await metadata.SearchAsync(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),input.Query,actor.User,actor.SessionId??"",token));}).RequireRateLimiting("watch-command");
        api.MapPut("/games/{id}/metadata",async(string id,RomMetadataSelect input,RomMetadata metadata,MediaStore store,CurrentAccount actor,HttpContext context,CancellationToken token)=>{RequireCommandActor(actor,context);return Results.Ok(await metadata.SelectAsync(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),input,actor.User,actor.SessionId??"",token));}).RequireRateLimiting("watch-command");
        api.MapGet("/games", (GameCatalog games, MediaStore store, CurrentAccount actor, NativeRuntimeSetup setup, HttpContext context) => Results.Ok(new { items = games.List(store, actor.User), serverRunnerAvailable = setup.Status(actor.User, NativeGameEndpoints.Local(context)).Available }));
        api.MapPost("/games/roms/artwork",(RomArtwork artwork,MediaStore store,CurrentAccount actor,HttpContext context)=>{RequireCommandActor(actor,context);var count=0;foreach(var rom in store.GetGameRoms(actor.User).Where(rom=>!artwork.HasCover(rom)).Take(64)){artwork.Queue(rom,actor.User);count++;}return Results.Ok(new{queued=count});}).RequireRateLimiting("watch-command");
        api.MapGet("/games/sessions/active", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetActiveGameSessions(actor.User)));
        api.MapGet("/games/{id}", (string id, GameCatalog games, MediaStore store, CurrentAccount actor) => Results.Ok(games.RequireEntry(id, store, actor.User)));
        api.MapPut("/games/{id}/preference", (string id, GamePreference input, GameCatalog games, MediaStore store, CurrentAccount actor, EventHub events, HttpContext context) =>
        { RequireCommandActor(actor, context); games.RequireEntry(id, store, actor.User); var state = store.SetGamePreference(id, input, actor.User); events.Publish("games.changed"); return Results.Ok(state); }).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/install", (string id, GameInstaller installer, GameCatalog games, MediaStore store, CurrentAccount actor, EventHub events, HttpContext context) =>
        { RequireCommandActor(actor, context); games.RequireEntry(id, store, actor.User); var job = installer.Queue(id, actor.User); events.Publish("games.changed"); return Results.Ok(job); }).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/install/cancel", (string id, GameCatalog games, MediaStore store, CurrentAccount actor, EventHub events, HttpContext context) =>
        { RequireCommandActor(actor, context); games.RequireEntry(id, store, actor.User); var job = store.GetProductJobs(actor.User, "game-install", id).FirstOrDefault() ?? throw new DomainException("Oyun kurulum işi bulunamadı.", 404); store.CancelProductJob(job.Id, job.Revision, actor.User); events.Publish("games.changed"); return Results.NoContent(); }).RequireRateLimiting("watch-command");
        api.MapGet("/games/{id}/artwork/{asset}", (string id, string asset, GameCatalog games, GameStore catalog, MediaStore store, CurrentAccount actor,RomArtwork artwork) =>
        { if (asset is not ("cover.png" or "backdrop.png")) throw new DomainException("Oyun görseli bulunamadı.", 404); games.RequireEntry(id, store, actor.User);if(store.GetGameRom(id,actor.User) is{} rom)return Results.File(artwork.Open(rom),"image/png",enableRangeProcessing:true); var definition = catalog.Find(id); return definition is not null ? Results.File(catalog.Artwork(definition), "image/png", enableRangeProcessing: true) : Results.File(games.OpenVerified(games.Require(id, actor.User), asset), "image/png", enableRangeProcessing: true); });
        api.MapGet("/games/{id}/covers",async(string id,string? query,RomArtwork artwork,MediaStore store,CurrentAccount actor,CancellationToken token)=>Results.Ok(await artwork.SearchAsync(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),query,token))).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/cover",async(string id,RomCoverCommand input,RomArtwork artwork,MediaStore store,CurrentAccount actor,HttpContext context,CancellationToken token)=>{RequireCommandActor(actor,context);await artwork.SetCoverAsync(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),input.Name,actor.User,token);return Results.NoContent();}).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/cover/upload",async(string id,RomArtwork artwork,MediaStore store,CurrentAccount actor,HttpContext context,CancellationToken token)=>{RequireCommandActor(actor,context);var rom=store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404);if(context.Request.ContentType!="image/png"||context.Request.ContentLength is null or >4*1024*1024)throw new DomainException("En fazla 4 MiB geçerli PNG kapak seç.",400);var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is{IsReadOnly:false})limit.MaxRequestBodySize=4*1024*1024;using var bytes=new MemoryStream();await context.Request.Body.CopyToAsync(bytes,token);await artwork.SaveImageAsync(rom,bytes.ToArray(),null,"User supplied",token);return Results.NoContent();}).RequireRateLimiting("watch-command");
        api.MapPut("/games/{id}/presentation",(string id,RomPresentationCommand input,RomArtwork artwork,MediaStore store,CurrentAccount actor,HttpContext context)=>{RequireCommandActor(actor,context);return Results.Ok(artwork.SetPresentation(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),input));}).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/sessions", (string id, GameStart input, GameCatalog games, MediaStore store, CurrentAccount actor, HttpContext context, EventHub events) =>
        {
            RequireCommandActor(actor, context);
            var game = games.Require(id, actor.User); var session = store.StartGameSession(game, input, actor.User, actor.SessionId ?? throw new DomainException("Oturum gerekli.", 401));
            events.Publish("games.changed");
            var query = game.Id == "a-dark-room" ? "?ignorebrowser=true" : ""; // Upstream's opt-in supported-browser override; Home owns the surrounding TV/player UI.
            return Results.Ok(new GameLaunch(session.Id, context.Request.PathBase + $"/game-runtime/{session.Id}/{input.Ticket}/index.html" + query, game.Version, session.ExpiresAt, store.GetGameCheckpoint(id, game.Version, actor.User.Id)));
        }).RequireRateLimiting("watch-command");
        api.MapPut("/games/sessions/{id}", (string id, GamePulse input, GameCatalog games, MediaStore store, CurrentAccount actor, HttpContext context) =>
        {
            RequireCommandActor(actor, context);
            var login = actor.SessionId ?? throw new DomainException("Oturum gerekli.", 401); var session = store.RequireGameSession(id, actor.User, login);
            return Results.Ok(store.PulseGameSession(id, games.Require(session.GameId, actor.User), input, actor.User, login));
        }).RequireRateLimiting("watch-command");
        api.MapDelete("/games/sessions/{id}", (string id, MediaStore store, CurrentAccount actor, EventHub events, HttpContext context) =>
        { RequireCommandActor(actor, context); store.CloseGameSession(id, actor.User, actor.SessionId ?? ""); events.Publish("games.changed"); return Results.NoContent(); }).RequireRateLimiting("watch-command");
        api.MapPost("/games/sessions/{id}/end", (string id, GamePulse input, GameCatalog games, MediaStore store, CurrentAccount actor, EventHub events, HttpContext context) =>
        {
            RequireCommandActor(actor, context);
            var login = actor.SessionId ?? throw new DomainException("Oturum gerekli.", 401); var session = store.RequireGameSession(id, actor.User, login);
            try { return Results.Ok(store.EndGameSession(id, games.Require(session.GameId, actor.User), input, actor.User, login)); }
            finally { events.Publish("games.changed"); }
        }).RequireRateLimiting("watch-command");
        app.MapMethods("/game-runtime/{id}/{ticket}/{**asset}", ["GET", "HEAD"], (string id, string ticket, string? asset, GameCatalog games, MediaStore store, HttpContext context) =>
        {
            var session = store.GetGameRuntimeSession(id, ticket) ?? throw new DomainException("Oyun oturumu sona erdi.", 401);
            var actor = store.GetAccount(session.UserId) ?? throw new DomainException("Oturum gerekli.", 401);
            var game = games.Require(session.GameId, actor);
            if (game.Version != session.GameVersion) throw new DomainException("Oyun paketi değişti.", 409);
            asset ??= "index.html";
            var file = games.OpenVerified(game, asset);
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            var runtime = origin + context.Request.PathBase + $"/game-runtime/{id}/{ticket}/";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Access-Control-Allow-Origin"] = "*"; // Opaque sandbox frame; no cookies or credentialed CORS.
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            context.Response.Headers["Permissions-Policy"] = "gamepad=*, fullscreen=*, autoplay=*";
            context.Response.Headers.ContentSecurityPolicy = $"sandbox allow-scripts; default-src 'none'; script-src {runtime} 'unsafe-eval' 'wasm-unsafe-eval'; style-src {runtime}{(game.Runtime == "html" ? " 'unsafe-inline'" : "")}; img-src {runtime} data: blob:; connect-src {runtime}; media-src {runtime} blob:; worker-src {runtime} blob:; frame-ancestors {origin}; base-uri 'none'; form-action 'none'";
            return Results.File(file, GameCatalog.ContentType(asset), enableRangeProcessing: true);
        });
    }
    internal static void RequireCommandActor(CurrentAccount actor, HttpContext context)
    {
        if (context.Request.Headers["X-Home-User"].ToString() != actor.User.Id || context.Request.Headers["X-Home-Session"].ToString() != actor.SessionId)
            throw new DomainException("Oyun komutu sırasında hesap değişti; ekranı yenile.", 409);
    }
}
