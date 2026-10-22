using System.Security.Claims;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.AspNetCore.Identity;

namespace HomeServer.Identity;

public sealed class HouseholdProfiles(MediaStore store, IdentityService identity, IPasswordHasher<Owner> hasher, NetworkConfiguration network)
{
    private string Cookie => NetworkConfiguration.CookieName("home.device", network.Current.PathBase);
    public HouseholdDevice? Device(HttpContext context) => store.GetHouseholdDevice(context.Request.Cookies[Cookie]);
    public IReadOnlyList<DeviceProfile> Profiles(HttpContext context)
    {
        var device = Device(context); if (device is null) return [];
        return device.Users.Select(grant => (User: store.GetAccountSecret(grant.Key), Version: grant.Value))
            .Where(item => item.User is { PinHash: not null, Account.Enabled: true } && item.User.Version == item.Version)
            .Select(item => new DeviceProfile(item.User!.Account.Id, item.User.Account.DisplayName, item.User.Account.Role)).ToArray();
    }
    public void Trust(HttpContext context, Account owner, string[] users)
    {
        var existing = Device(context); var created = store.CreateHouseholdDevice(owner, users);
        if (existing is not null && existing.OwnerId == owner.Id) store.DeleteHouseholdDevice(existing.Id);
        context.Response.Cookies.Append(Cookie, created.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict,
            Secure = network.Current.RequireHttps || context.Request.IsHttps, Path = network.Current.PathBase.Length == 0 ? "/" : network.Current.PathBase, Expires = DateTimeOffset.Parse(created.Device.ExpiresAt) });
    }
    public void Forget(HttpContext context)
    {
        if (Device(context) is { } device) store.DeleteHouseholdDevice(device.Id);
        context.Response.Cookies.Delete(Cookie, new CookieOptions { Path = network.Current.PathBase.Length == 0 ? "/" : network.Current.PathBase });
    }
    public async Task SwitchAsync(HttpContext context, ProfileSwitchInput input)
    {
        var device = Device(context) ?? throw new DomainException("Bu cihaz profil geçişi için etkinleştirilmedi.", 403);
        if (DateTimeOffset.TryParse(device.RetryAt, out var retry) && retry > DateTimeOffset.UtcNow) throw new DomainException("PIN denemesi bekleme süresi henüz dolmadı.", 429);
        var user = store.GetAccountSecret(input.UserId);
        var permitted = user is { Account.Enabled: true, PinHash: not null } && device.Users.TryGetValue(input.UserId, out var version) && version == user.Version;
        var matched = permitted && input.Pin is { Length: >= 6 and <= 12 } && hasher.VerifyHashedPassword(new Owner(user!.Account.DisplayName, ""), user.PinHash!, input.Pin) != PasswordVerificationResult.Failed;
        store.RecordPinAttempt(device.Id, matched);
        if (!matched) throw new DomainException("PIN veya seçilen profil doğru değil.", 403);
        // Re-read the grant after hashing: an account/device change invalidates an in-flight switch.
        var current = Device(context);
        if (current is null || !current.Users.TryGetValue(user!.Account.Id, out var expected) || store.GetAccountSecret(user.Account.Id)?.Version != expected) throw new DomainException("Cihaz veya hesap yetkisi değişti.", 409);
        var oldUser = context.User.FindFirstValue(ClaimTypes.NameIdentifier); var oldSession = context.User.FindFirstValue("home.sid");
        if (oldUser is not null && oldSession is not null) store.RevokeLoginSession(oldSession, oldUser);
        await identity.SignInAsync(context, user, current.Id);
    }
    public string? HashPin(string name, string? pin)
    {
        if (string.IsNullOrEmpty(pin)) return null;
        if (pin.Length is < 6 or > 12 || pin.Any(c => c is < '0' or > '9')) throw new DomainException("PIN 6–12 rakam olmalı.");
        return hasher.HashPassword(new Owner(name, ""), pin);
    }
}
