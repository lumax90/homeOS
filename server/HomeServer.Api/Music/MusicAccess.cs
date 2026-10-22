using HomeServer.Core;
using HomeServer.Identity;

namespace HomeServer.Music;

public static class MusicAccess
{
    public static bool Online(Account actor) => actor.Enabled && (actor.IsAdmin || actor.CanDiscover && (actor.MaximumAge is null || actor.AllowUnrated));
    public static void RequireCommand(CurrentAccount actor, HttpContext context)
    {
        if (context.Request.Headers["X-Home-User"].ToString() != actor.User.Id || context.Request.Headers["X-Home-Session"].ToString() != actor.SessionId)
            throw new DomainException("Müzik komutu sırasında hesap değişti; ekranı yenile.", 409);
    }
}
