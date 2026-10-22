using System.Security.Claims;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace HomeServer.Identity;

public sealed class IdentityService(MediaStore store, IPasswordHasher<Owner> hasher, PlaybackDeviceIdentity? devices = null, PlaybackSessions? playback = null)
{
    public async Task SignInAsync(HttpContext context, AccountSecret user, string? householdDeviceId = null)
    {
        var session = store.CreateLoginSession(user.Account.Id, user.Version, context.Request.Headers.UserAgent.ToString(), householdDeviceId, devices?.Resolve(context));
        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } oldUser && context.User.FindFirstValue("home.sid") is { } oldSession) store.RevokeLoginSession(oldSession, oldUser);
        if (playback is not null) await playback.ReconcileAsync();
        var claims = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Account.Id), new Claim(ClaimTypes.Name, user.Account.DisplayName),
            new Claim(ClaimTypes.Role, user.Account.Role), new Claim("home.sid", session.Id),
            new Claim("home.acquire", user.Account.CanAcquire ? "yes" : "no"), new Claim("home.discover", user.Account.CanDiscover ? "yes" : "no")
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claims),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.Parse(session.ExpiresAt) });
    }
    public async Task LoginAsync(HttpContext context, Credentials input)
    {
        var user = string.IsNullOrWhiteSpace(input.DisplayName) ? null : store.FindAccount(input.DisplayName);
        if (user is null || !user.Account.Enabled || !Matches(user, input.Password)) throw new DomainException("Ad veya parola doğru değil.", 401);
        await SignInAsync(context, user);
    }
    public string Hash(string name, string password)
    {
        ValidatePassword(password);
        var value = new Owner(name, "");
        return hasher.HashPassword(value, password);
    }
    public bool Matches(AccountSecret user, string? password) => password is { Length: <= 256 } &&
        hasher.VerifyHashedPassword(new Owner(user.Account.DisplayName, user.PasswordHash), user.PasswordHash, password) != PasswordVerificationResult.Failed;
    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 10 or > 256) throw new DomainException("Parola 10–256 karakter olmalı.");
    }
}
