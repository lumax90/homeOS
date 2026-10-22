using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;

namespace HomeServer.Notifications;

public sealed class WebPushSettings(MediaStore store, IDataProtectionProvider protection, NotificationSettings notifications)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Push.Vapid.v1");
    public PushServerConfiguration Configuration => store.GetPushServerSnapshot().Configuration;
    public PushServerConfiguration Save(PushServerInput input)
    {
        if (input.Subject is null || input.Subject.Length > 500 || input.Subject.Any(c => char.IsControl(c) || c is '"' or '\\')) throw new DomainException("Push iletişim adresi geçersiz.");
        var subject = input.Subject.Trim();
        if (subject.Length > 0 || input.Enabled)
        {
            if (!Uri.TryCreate(subject, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0
                || (uri.Scheme == "mailto" ? uri.Query.Length > 0 || !MailboxAddress.TryParse(subject[7..], out var mailbox) || !mailbox.Address.Contains('@')
                    : uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.IsLoopback || string.IsNullOrEmpty(uri.Host))) throw new DomainException("Push için mailto: e-posta veya HTTPS iletişim adresi gerekli.");
            subject = uri.AbsoluteUri;
        }
        var snapshot = store.GetPushServerSnapshot();
        if (snapshot.Configuration.Revision != input.Revision) throw new DomainException("Push sunucu ayarı değişti; yeniden aç.", 409);
        var current = snapshot.Configuration; var publicKey = current.PublicKey; var encrypted = snapshot.ProtectedKey; var keyRevision = current.KeyRevision;
        if (input.RotateKey || input.Enabled && publicKey is null)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var parameters = key.ExportParameters(true);
            publicKey = WebEncoders.Base64UrlEncode([4, ..parameters.Q.X!, ..parameters.Q.Y!]); encrypted = _protector.Protect(WebEncoders.Base64UrlEncode(parameters.D!));
            CryptographicOperations.ZeroMemory(parameters.D!); keyRevision++;
        }
        return store.SavePushServerConfiguration(input.Revision, new(input.Enabled, subject, publicKey, current.Revision + 1, keyRevision), encrypted);
    }
    public (PushServerConfiguration Configuration, string PrivateKey) Vapid(int keyRevision)
    {
        var snapshot = store.GetPushServerSnapshot();
        if (!snapshot.Configuration.Enabled || snapshot.Configuration.KeyRevision != keyRevision || snapshot.Configuration.PublicKey is null || string.IsNullOrEmpty(snapshot.ProtectedKey)) throw new DomainException("Push imza anahtarı değişti veya sunucu kapalı.", 409);
        try { return (snapshot.Configuration, _protector.Unprotect(snapshot.ProtectedKey)); }
        catch (CryptographicException) { throw new DomainException("Push imza anahtarı açılamadı; yönetici anahtarı yenilemeli.", 409); }
    }
    public BrowserPushRegistration Subscribe(PlaybackActor actor, BrowserPushInput input)
    {
        if (input.Keys is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || input.Name.Any(char.IsControl) || input.Events is null || input.Events.Length is < 1 or > 7
            || input.Events.Any(value => !NotificationSettings.EventKinds.Contains(value)) || input.ExpirationTime is < 0 or > 253402300799999) throw new DomainException("Push cihazı veya olay tercihleri geçersiz.");
        var endpoint = ValidateEndpoint(input.Endpoint); var publicKey = Decode(input.Keys.P256dh, 65); var auth = Decode(input.Keys.Auth, 16);
        try
        {
            if (publicKey[0] != 4) throw new CryptographicException();
            using var peer = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] } });
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException) { throw new DomainException("Push P-256 anahtarı geçerli bir eğri noktası değil."); }
        var bindingId = Guid.NewGuid().ToString("N");
        var secrets = new BrowserPushSecret(endpoint.AbsoluteUri, WebEncoders.Base64UrlEncode(publicKey), WebEncoders.Base64UrlEncode(auth), bindingId, input.KeyRevision);
        var keyHash = Convert.ToHexStringLower(SHA256.HashData([..publicKey, ..auth])); var endpointHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)));
        return store.SaveBrowserPush(actor, input, endpointHash, keyHash, bindingId, notifications.Protect(new(Push: secrets)), endpoint.GetLeftPart(UriPartial.Authority));
    }
    public static Uri ValidateEndpoint(string? value)
    {
        if (value is null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new DomainException("Tarayıcının HTTPS push servis adresi gerekli.");
        var host = uri.IdnHost.ToLowerInvariant();
        if (host is not ("fcm.googleapis.com" or "updates.push.services.mozilla.com") && !host.EndsWith(".push.apple.com", StringComparison.Ordinal) && !host.EndsWith(".notify.windows.com", StringComparison.Ordinal))
            throw new DomainException("Bu push servisi desteklenen tarayıcı servisleri arasında değil.");
        return uri;
    }
    private static byte[] Decode(string? value, int size)
    {
        if (value is null || value.Length > 128 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '='))) throw new DomainException("Push anahtarı geçersiz.");
        try { var bytes = WebEncoders.Base64UrlDecode(value); return bytes.Length == size ? bytes : throw new DomainException("Push anahtar boyutu geçersiz."); }
        catch (FormatException) { throw new DomainException("Push anahtar kodlaması geçersiz."); }
    }
}
