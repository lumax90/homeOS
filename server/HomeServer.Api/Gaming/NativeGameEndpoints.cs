using System.Net;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using System.Net.WebSockets;
using System.Text.Json;
using System.Diagnostics;

namespace HomeServer.Gaming;

public static class NativeGameEndpoints
{
    internal static bool Local(HttpContext context) => context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    public static void MapNativeGames(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet("/games/native/status", (NativeRuntimeSetup setup, CurrentAccount actor, HttpContext context) => Results.Ok(setup.Status(actor.User, Local(context))));
        api.MapPost("/games/native/setup", async (NativeSetupCommand input, NativeRuntimeSetup setup, CurrentAccount actor, HttpContext context, CancellationToken token) =>
        {
            GameEndpoints.RequireCommandActor(actor, context);
            return Results.Ok(await setup.ConfigureAsync(input, actor.User, Local(context), token));
        }).RequireRateLimiting("watch-command");
        api.MapGet("/games/native/options",(HomePaths paths)=>Results.Ok(NativeEmulationSettings.Read(paths)));
        api.MapGet("/games/{id}/settings",async(string id,NativeGameManager games,CurrentAccount actor,CancellationToken token)=>Results.Ok(await games.SettingsAsync(id,actor.User,actor.SessionId??"",token)));
        api.MapPut("/games/{id}/settings",async(string id,NativeSettingsChange input,NativeGameManager games,CurrentAccount actor,HttpContext context,CancellationToken token)=>{GameEndpoints.RequireCommandActor(actor,context);return Results.Ok(await games.SaveSettingsAsync(id,input,actor.User,actor.SessionId??"",token));}).RequireRateLimiting("watch-command");
        api.MapPut("/games/native/options",async(NativeEmulationOptions input,NativeEmulationSettings settings,CurrentAccount actor,HttpContext context,CancellationToken token)=>{GameEndpoints.RequireCommandActor(actor,context);return Results.Ok(await settings.SaveAsync(input,actor.User,Local(context),token));}).RequireRateLimiting("watch-command");
        api.MapGet("/games/native/sessions/{id}/input-stream",async(string id,NativeGameManager games,CurrentAccount actor,HttpContext context,CancellationToken token)=>
        {
            if(!actor.User.IsAdmin||!context.WebSockets.IsWebSocketRequest||context.Request.Headers.Origin.ToString()!=$"{context.Request.Scheme}://{context.Request.Host}")throw new DomainException("Oyun giriş bağlantısı bu istemcide kullanılamaz.",403);
            using var socket=await context.WebSockets.AcceptWebSocketAsync();var buffer=new byte[8192];var authenticated=false;var ticket="";var messages=0;var window=Stopwatch.StartNew();
            try
            {
                while(socket.State==WebSocketState.Open)
                {
                    using var messageBudget=CancellationTokenSource.CreateLinkedTokenSource(token);messageBudget.CancelAfter(TimeSpan.FromSeconds(authenticated?35:2));
                    var length=0;WebSocketReceiveResult result;
                    do{if(length==buffer.Length)throw new DomainException("Oyun giriş mesajı çok büyük.",413);result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer,length,buffer.Length-length),messageBudget.Token);if(result.MessageType==WebSocketMessageType.Close)return;length+=result.Count;}while(!result.EndOfMessage);
                    if(result.MessageType!=WebSocketMessageType.Text)throw new DomainException("Oyun giriş mesajı geçersiz.",400);
                    if(!authenticated){using var document=JsonDocument.Parse(buffer.AsMemory(0,length));ticket=document.RootElement.GetProperty("ticket").GetString()??"";games.OpenInputStream(id,ticket,actor.User,actor.SessionId??"");authenticated=true;await socket.SendAsync("ok"u8.ToArray(),WebSocketMessageType.Text,true,token);continue;}
                    if(window.ElapsedMilliseconds>=1000){messages=0;window.Restart();}if(++messages>1000)throw new DomainException("Oyun giriş hızı sınırı aşıldı.",429);
                    if(length==4&&buffer.AsSpan(0,4).SequenceEqual("ping"u8)){games.RequireInputStream(id,ticket,actor.User,actor.SessionId??"");continue;}
                    var input=JsonSerializer.Deserialize<NativeGameInput>(buffer.AsSpan(0,length),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("Oyun giriş mesajı geçersiz.",400);games.Input(id,input,actor.User,actor.SessionId??"");
                }
            }
            catch(Exception error)when(error is DomainException or JsonException or WebSocketException or OperationCanceledException or KeyNotFoundException){if(socket.State==WebSocketState.Open)await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation,"Input stream ended",CancellationToken.None);}
            finally{if(authenticated)games.ReleaseInputStream(id,actor.User,actor.SessionId??"");}
        }).RequireRateLimiting("watch-command");
        api.MapGet("/games/native/sessions/{id}/options",async(string id,NativeGameManager games,CurrentAccount actor,CancellationToken token)=>Results.Ok(await games.GetOptionsAsync(id,actor.User,actor.SessionId??"",token)));
        api.MapPut("/games/native/sessions/{id}/options",async(string id,NativeCoreOptionChange input,NativeGameManager games,CurrentAccount actor,HttpContext context,CancellationToken token)=>{GameEndpoints.RequireCommandActor(actor,context);return Results.Ok(await games.SetOptionAsync(id,input,actor.User,actor.SessionId??"",token));}).RequireRateLimiting("watch-command");
        api.MapGet("/games/native/sessions/{id}/controllers",async(string id,NativeGameManager games,CurrentAccount actor,CancellationToken token)=>Results.Ok(await games.ControllersAsync(id,actor.User,actor.SessionId??"",token)));
        api.MapPut("/games/native/sessions/{id}/controllers",async(string id,NativeControllerChange input,NativeGameManager games,CurrentAccount actor,HttpContext context,CancellationToken token)=>{GameEndpoints.RequireCommandActor(actor,context);return Results.Ok(await games.SetControllerAsync(id,input,actor.User,actor.SessionId??"",token));}).RequireRateLimiting("watch-command");
        api.MapPost("/games/{id}/native-sessions", async (string id, GameStart input, NativeGameManager games, CurrentAccount actor, HttpContext context, CancellationToken token) =>
        {
            GameEndpoints.RequireCommandActor(actor,context);
            return Results.Ok(await games.LaunchAsync(id,input,actor.User,actor.SessionId??"",token,context.Request.Host.Host));
        }).RequireRateLimiting("watch-command");
        api.MapPost("/games/native/sessions/{id}/command", async (string id, NativeGameCommand input, NativeGameManager games, CurrentAccount actor, HttpContext context) =>
        {
            GameEndpoints.RequireCommandActor(actor,context);
            using var budget=new CancellationTokenSource(input.Action is "end" or "save"?TimeSpan.FromMinutes(10):TimeSpan.FromSeconds(12));return Results.Ok(await games.CommandAsync(id,input.Action,actor.User,actor.SessionId??"",budget.Token));
        }).RequireRateLimiting("watch-command");
        api.MapPost("/games/native/sessions/{id}/input", (string id, NativeGameInput input, NativeGameManager games, CurrentAccount actor, HttpContext context) =>
        {
            GameEndpoints.RequireCommandActor(actor,context);
            games.Input(id,input,actor.User,actor.SessionId??"");return Results.NoContent();
        }).RequireRateLimiting("native-input");
        app.MapMethods("/game-native/{id}/whep/{reader?}",["OPTIONS","POST","PATCH","DELETE"],async (string id,string? reader,NativeGameManager games,CurrentAccount actor,HttpContext context,CancellationToken token)=>
        {
            var header=context.Request.Headers.Authorization.ToString();var ticket=header.StartsWith("Bearer ",StringComparison.Ordinal)?header[7..]:"";
            if(ticket.Length!=64||ticket.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Yerel oyun yayın bileti gerekli.",401);
            games.RequireInputStream(id,ticket,actor.User,actor.SessionId??"");
            if(context.Request.ContentLength>64*1024)throw new DomainException("Yerel yayın isteği çok büyük.",413);
            using var body=new MemoryStream();var buffer=new byte[8192];int bytes;while((bytes=await context.Request.Body.ReadAsync(buffer,token))>0){if(body.Length+bytes>64*1024)throw new DomainException("Yerel yayın isteği çok büyük.",413);body.Write(buffer,0,bytes);}
            using var response=await games.WhepAsync(id,ticket,context.Request.Method,reader,body.ToArray(),context.Request.ContentType,context.Request.Headers.IfMatch.ToString(),token);
            context.Response.StatusCode=(int)response.StatusCode;context.Response.Headers.CacheControl="no-store";
            if(response.Headers.Location is{}location)context.Response.Headers.Location=context.Request.PathBase+location.ToString();
            if(response.Headers.ETag is{}etag)context.Response.Headers.ETag=etag.ToString();
            if(response.Headers.TryGetValues("Link",out var links))context.Response.Headers["Link"]=string.Join(",",links);
            if(response.Headers.TryGetValues("Accept-Patch",out var patch))context.Response.Headers["Accept-Patch"]=string.Join(",",patch);
            if(response.Content.Headers.ContentType is{}type)context.Response.ContentType=type.ToString();
            var payload=await response.Content.ReadAsByteArrayAsync(token);if(payload.Length>64*1024)throw new DomainException("Yerel yayın yanıtı çok büyük.",502);await context.Response.Body.WriteAsync(payload,token);
        }).RequireAuthorization();
    }
}
