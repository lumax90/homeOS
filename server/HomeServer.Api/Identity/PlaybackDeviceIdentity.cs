using System.Security.Cryptography;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Identity;

public sealed class PlaybackDeviceIdentity(MediaStore store, IDataProtectionProvider protection, NetworkConfiguration network)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Playback.BrowserDevice.v1");
    public string Resolve(HttpContext context)
    {
        var cookie = NetworkConfiguration.CookieName("home.player-device", network.Current.PathBase);
        string? id = null;
        if (context.Request.Cookies[cookie] is { Length: < 2048 } value)
            try { var decoded = _protector.Unprotect(value); if (Guid.TryParseExact(decoded, "N", out _) && store.GetPlaybackDevice(decoded) is not null) id = decoded; }
            catch (CryptographicException) { }
        id ??= store.CreatePlaybackDevice(context.Request.Headers.UserAgent.ToString()).Id;
        context.Response.Cookies.Append(cookie, _protector.Protect(id), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = network.Current.RequireHttps || context.Request.IsHttps,
            Path = network.Current.PathBase.Length == 0 ? "/" : network.Current.PathBase, Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
        return id;
    }
}
