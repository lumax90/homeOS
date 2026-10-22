using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HomeServer.Identity;

public static class IdentityEndpoints
{
    public static void MapIdentity(this RouteGroupBuilder api)
    {
        api.MapGet("/account", (CurrentAccount actor) => Results.Ok(actor.User));
        api.MapPut("/account/pin", async (PinInput input, CurrentAccount actor, MediaStore store, HouseholdProfiles profiles, IdentityService identity, HttpContext context) =>
        {
            var user = store.GetAccountSecret(actor.User.Id)!;
            if (!identity.Matches(user, input.CurrentPassword)) throw new DomainException("Mevcut hesap parolası doğru değil.", 403);
            var updated = store.SetAccountPin(user.Account.Id, profiles.HashPin(user.Account.DisplayName, input.Pin), user.Version);
            await identity.SignInAsync(context, updated); return Results.NoContent();
        });
        api.MapPost("/profiles/device", (DeviceProfileInput input, CurrentAccount actor, HouseholdProfiles profiles, HttpContext context) =>
        { profiles.Trust(context, actor.User, input.UserIds); return Results.NoContent(); }).RequireAuthorization("admin");
        api.MapGet("/account/sessions", (CurrentAccount actor, MediaStore store) => Results.Ok(store.GetLoginSessions(actor.User.Id, actor.SessionId)));
        api.MapDelete("/account/sessions/{id}", async (string id, CurrentAccount actor, MediaStore store, HttpContext context, PlaybackSessions playback) =>
        {
            store.RevokeLoginSession(id, actor.User.Id);
            await playback.ReconcileAsync();
            if (id == actor.SessionId) await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        api.MapPut("/account/password", async (PasswordChange input, CurrentAccount actor, MediaStore store, IdentityService identity, HttpContext context) =>
        {
            var user = store.GetAccountSecret(actor.User.Id)!;
            if (!identity.Matches(user, input.CurrentPassword)) throw new DomainException("Mevcut parola doğru değil.", 403);
            var updated = store.ChangePassword(user.Account.Id, identity.Hash(user.Account.DisplayName, input.NewPassword), user.Version);
            await identity.SignInAsync(context, updated);
            return Results.Ok(new { message = "Parola değiştirildi; diğer oturumlar kapatıldı." });
        });
        var admin = api.MapGroup("/users").RequireAuthorization("admin");
        admin.MapGet("", (MediaStore store) => Results.Ok(store.GetAccounts()));
        admin.MapPost("", (CreateAccount input, IdentityService identity, MediaStore store) =>
        {
            if (input.Account is null) throw new DomainException("Hesap bilgileri gerekli.");
            return Results.Ok(store.SaveAccount(null, input.Account, identity.Hash(input.Account.DisplayName, input.Password)));
        });
        admin.MapPut("/{id}", async (string id, AccountInput input, CurrentAccount actor, HttpContext context, IdentityService identity, MediaStore store, PlaybackSessions playback) =>
        {
            var result = store.SaveAccount(id, input);
            await playback.ReconcileAsync();
            if (id == actor.User.Id) await identity.SignInAsync(context, store.GetAccountSecret(id)!);
            return Results.Ok(result);
        });
        admin.MapPost("/{id}/password", async (string id, PasswordReset input, CurrentAccount actor, MediaStore store, IdentityService identity, HttpContext context, PlaybackSessions playback) =>
        {
            var user = store.GetAccount(id) ?? throw new DomainException("Hesap bulunamadı.", 404);
            var updated = store.ChangePassword(id, identity.Hash(user.DisplayName, input.NewPassword));
            await playback.ReconcileAsync();
            if (id == actor.User.Id) await identity.SignInAsync(context, updated);
            return Results.NoContent();
        });
        admin.MapPost("/invitations", (AccountInput input, MediaStore store) => Results.Ok(store.CreateInvitation(input)));
        admin.MapPut("/{id}/pin", async (string id, PinReset input, CurrentAccount actor, MediaStore store, HouseholdProfiles profiles, IdentityService identity, HttpContext context, PlaybackSessions playback) =>
        {
            var user = store.GetAccountSecret(id) ?? throw new DomainException("Hesap bulunamadı.", 404);
            var updated = store.SetAccountPin(id, profiles.HashPin(user.Account.DisplayName, input.Pin), user.Version);
            await playback.ReconcileAsync();
            if (id == actor.User.Id) await identity.SignInAsync(context, updated);
            return Results.NoContent();
        });
    }
    public sealed record PinReset(string? Pin);
}
