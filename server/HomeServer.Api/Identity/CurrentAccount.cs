using System.Security.Claims;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Identity;

public sealed class CurrentAccount(IHttpContextAccessor contexts, MediaStore store)
{
    public Account User => contexts.HttpContext?.Items[typeof(Account)] as Account ?? throw new DomainException("Oturum gerekli.", 401);
    public string? SessionId => contexts.HttpContext?.User.FindFirstValue("home.sid");
    public MediaItem RequireMedia(string id) => store.GetMedia(id, User) ?? throw new DomainException("Medya bulunamadı veya bu kütüphaneye erişimin yok.", 404);
    public ContentEntry RequireContent(string id)
    {
        var content = store.GetContent(id, User) ?? throw new DomainException("İçerik bulunamadı.", 404);
        if (!User.IsAdmin && !User.CanDiscover && store.GetContentFiles(id, User).Count == 0) throw new DomainException("İçerik bulunamadı.", 404);
        return content;
    }
    public void RequireDiscovery() { if (!User.IsAdmin && !User.CanDiscover) throw new DomainException("Bu hesapta keşif izni yok.", 403); }
    public void RequireMetadata(ProviderContent metadata) { if (!store.CanSeeProviderContent(metadata, User)) throw new DomainException("İçerik bulunamadı veya profil kapsamı dışında.", 404); }
    public void RequireUnratedCatalog() { RequireDiscovery(); if (!User.IsAdmin && User.MaximumAge is not null && !User.AllowUnrated) throw new DomainException("Bu müzik sağlayıcısı yaş sınıflandırması sunmuyor; profil bilinmeyen sınıflandırmaları kapatıyor.", 403); }
}
