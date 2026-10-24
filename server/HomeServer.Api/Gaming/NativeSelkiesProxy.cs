using System.Net.WebSockets;
using HomeServer.Core;
using HomeServer.Identity;

namespace HomeServer.Gaming;

/// <summary>Only the leased viewer and signalling surface is published at Home's origin.</summary>
public static class NativeSelkiesProxy
{
    private const string Page="""
        <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Home</title></head>
        <body><div id="app"></div><script type="module" src="./home-viewer.js"></script></body></html>
        """;
    public static bool Allowed(string? asset)
    {
        if(string.IsNullOrEmpty(asset)||asset is "index.html" or "home-viewer.js" or "api/status" or "api/health" or "api/turn" or "api/webrtc/signaling")return true;
        if(asset.Length>512||asset.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('/' or '.' or '-' or '_'))||asset.Split('/').Any(part=>part is "" or "." or ".."))return false;
        if(asset.StartsWith("api/",StringComparison.Ordinal))return false;
        var extension=Path.GetExtension(asset);
        return extension is ".js" or ".css" or ".wasm" or ".json" or ".txt" or ".png" or ".svg" or ".ico";
    }
    public static void Map(WebApplication app)
    {
        app.MapMethods("/game-native/{id}/selkies/{**asset}",["GET","HEAD"],async(string id,string? asset,NativeGameManager games,CurrentAccount actor,IWebHostEnvironment environment,HttpContext context,CancellationToken token)=>
        {
            if(!Allowed(asset))throw new DomainException("Oyun yayın kaynağı bulunamadı.",404);
            var session=games.RequireSelkies(id,actor.User,actor.SessionId??"");
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token,session.Lifetime);
            using var abort=session.Lifetime.Register(context.Abort);
            context.Response.Headers.CacheControl="no-store";
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            if(asset=="api/webrtc/signaling")
            {
                if(!context.WebSockets.IsWebSocketRequest||context.Request.Headers.Origin.ToString()!=session.Origin)
                    throw new DomainException("Oyun giriş bağlantısı bu istemcide kullanılamaz.",403);
                await Signalling(session,context,lifetime.Token);return;
            }
            if(context.WebSockets.IsWebSocketRequest)throw new DomainException("Oyun yayın kaynağı bulunamadı.",404);
            if(string.IsNullOrEmpty(asset)||asset=="index.html")
            {
                var socketOrigin=session.Origin.Replace("https://","wss://",StringComparison.Ordinal).Replace("http://","ws://",StringComparison.Ordinal);
                context.Response.Headers.ContentSecurityPolicy=$"default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self' {socketOrigin} blob:; media-src blob:; worker-src 'self' blob:; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
                await Results.Content(Page,"text/html; charset=utf-8").ExecuteAsync(context);return;
            }
            if(asset=="home-viewer.js")
            {
                var path=Path.Combine(environment.WebRootPath??Path.Combine(environment.ContentRootPath,"wwwroot"),"native","selkies-viewer.js");
                await Results.File(path,"text/javascript; charset=utf-8").ExecuteAsync(context);return;
            }
            using var request=session.ReadRequest(asset,context.Request.Method=="HEAD");
            using var response=await session.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,lifetime.Token);
            context.Response.StatusCode=(int)response.StatusCode;
            if(response.Content.Headers.ContentLength>32L*1024*1024)throw new DomainException("Oyun yayın kaynağı çok büyük.",502);
            context.Response.ContentType=response.Content.Headers.ContentType?.ToString()??"application/octet-stream";
            if(context.Request.Method=="HEAD")return;
            await using var stream=await response.Content.ReadAsStreamAsync(lifetime.Token);var buffer=new byte[65536];long total=0;int count;
            while((count=await stream.ReadAsync(buffer,lifetime.Token))>0)
            {total+=count;if(total>32L*1024*1024){context.Abort();return;}await context.Response.Body.WriteAsync(buffer.AsMemory(0,count),lifetime.Token);}
        }).RequireAuthorization();
    }
    private static async Task Signalling(NativeSelkiesSession session,HttpContext context,CancellationToken token)
    {
        session.AcquireReader();
        try
        {
            using var upstream=new ClientWebSocket();upstream.Options.Proxy=null;upstream.Options.SetRequestHeader("Origin",session.Origin);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);
            await upstream.ConnectAsync(new Uri(session.Target("api/webrtc/signaling").Replace("http://","ws://",StringComparison.Ordinal)),budget.Token);
            using var browser=await context.WebSockets.AcceptWebSocketAsync();
            var incoming=Copy(browser,upstream,budget.Token);var outgoing=Copy(upstream,browser,budget.Token);
            await Task.WhenAny(incoming,outgoing);budget.Cancel();
            try{await Task.WhenAll(incoming,outgoing);}catch(Exception error)when(error is OperationCanceledException or WebSocketException){}
        }
        finally{session.ReleaseReader();}
    }
    private static async Task Copy(WebSocket source,WebSocket target,CancellationToken token)
    {
        var buffer=new byte[16384];long size=0;
        while(source.State==WebSocketState.Open&&!token.IsCancellationRequested)
        {
            var message=await source.ReceiveAsync(buffer.AsMemory(),token);
            if(message.MessageType==WebSocketMessageType.Close)return;
            size+=message.Count;if(size>65536||message.MessageType!=WebSocketMessageType.Text)throw new WebSocketException("Invalid signalling frame");
            await target.SendAsync(buffer.AsMemory(0,message.Count),message.MessageType,message.EndOfMessage,token);
            if(message.EndOfMessage)size=0;
        }
    }
}
