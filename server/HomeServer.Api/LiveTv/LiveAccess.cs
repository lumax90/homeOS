using HomeServer.Core;
using HomeServer.Identity;

namespace HomeServer.LiveTv;
public static class LiveAccess
{
    public static void Command(CurrentAccount actor,HttpContext context)
    {if(context.Request.Headers["X-Home-User"].ToString()!=actor.User.Id||context.Request.Headers["X-Home-Session"].ToString()!=actor.SessionId)throw new DomainException("TV komutu sırasında hesap değişti; ekranı yenile.",409);}
}
