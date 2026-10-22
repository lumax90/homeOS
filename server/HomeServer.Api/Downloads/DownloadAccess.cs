using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Downloads;

public static class DownloadAccess
{
    public static DownloadPrincipal Actor(CurrentAccount account, HttpContext context, MediaStore store)
    {
        if (context.Request.Headers["X-Home-User"].ToString() != account.User.Id || context.Request.Headers["X-Home-Session"].ToString() != account.SessionId)
            throw new DomainException("İndirme komutu sırasında hesap değişti; ekranı yenile.", 409);
        return store.DownloadActor(account.User.Id, account.SessionId);
    }
    public static DownloadRecord Require(string id, CurrentAccount account, HttpContext context, MediaStore store)
    { var actor = Actor(account, context, store); return store.RequireDownloadAccess(id, store.RequireDownloadPrincipal(actor)); }
}
