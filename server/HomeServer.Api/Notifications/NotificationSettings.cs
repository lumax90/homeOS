using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace HomeServer.Notifications;

public sealed class NotificationSettings(MediaStore store, IDataProtectionProvider protection)
{
    public static readonly string[] EventKinds = ["request", "approval", "available", "automation", "music", "subtitle", "system"];
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Notification.Connection.v1");
    public string Protect(NotificationSecrets secrets) => _protector.Protect(JsonSerializer.Serialize(secrets));
    public NotificationSecrets Secrets(string id)
    {
        var connection = store.GetNotificationConnection(id) ?? throw new DomainException("Kanal bulunamadı.", 404);
        return Secrets(connection);
    }
    public NotificationSecrets Secrets(NotificationConnection connection)
    {
        if (connection.Channel.Deleted) throw new DomainException("Kanal silindi.", 410);
        try { return JsonSerializer.Deserialize<NotificationSecrets>(_protector.Unprotect(connection.ProtectedSecrets))!; }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new DomainException("Bağlantı sırları açılamadı; kanalı yeniden yapılandır.", 409); }
    }
    public NotificationChannel Save(string? id, NotificationChannelInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || input.Name.Any(char.IsControl) || input.Kind is not ("webhook" or "smtp")
            || input.Events is null || input.Events.Length is < 1 or > 7 || input.Events.Any(value => !EventKinds.Contains(value)) || input.MaximumHourlyAttempts is < 1 or > 1000)
            throw new DomainException("Kanal adı, olay seçimi veya deneme bütçesi geçersiz.");
        var before = id is null ? new NotificationSecrets() : Secrets(id);
        var next = input.Kind == "webhook" ? new NotificationSecrets(Url: input.Url ?? before.Url, SigningSecret: input.SigningSecret ?? before.SigningSecret)
            : new NotificationSecrets(Host: input.Host ?? before.Host, Port: input.Port ?? before.Port, Security: input.Security ?? before.Security,
                Username: input.Username ?? before.Username, Password: input.Password ?? before.Password, From: input.From ?? before.From, To: input.To ?? before.To,
                CertificateSha256: input.CertificateSha256?.ToLowerInvariant() ?? before.CertificateSha256);
        string target;
        if (input.Kind == "webhook")
        {
            if (next.Url is not { Length: <= 2048 } || !Uri.TryCreate(next.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
                || next.SigningSecret is { Length: > 0 and < 16 } or { Length: > 256 }) throw new DomainException("HTTP(S) hedefi ve isteğe bağlı en az 16 karakter imza sırrı gerekli.");
            target = uri.GetLeftPart(UriPartial.Authority);
        }
        else
        {
            if (string.IsNullOrEmpty(next.Username)) next = next with { Password = null };
            if (string.IsNullOrWhiteSpace(next.Host) || next.Host.Length > 253 || Uri.CheckHostName(next.Host) == UriHostNameType.Unknown || next.Port is < 1 or > 65535
                || next.Security is not ("starttls" or "ssl" or "none") || next.Security == "none" && !Loopback(next.Host)
                || next.Username?.Length > 320 || next.Password?.Length > 2048 || !string.IsNullOrEmpty(next.Username) && string.IsNullOrEmpty(next.Password)
                || !string.IsNullOrEmpty(next.CertificateSha256) && (next.CertificateSha256.Length != 64 || next.CertificateSha256.Any(c => !Uri.IsHexDigit(c)))
                || !Mailbox(next.From) || !Mailbox(next.To)) throw new DomainException("SMTP sunucusu, TLS, kimlik bilgileri veya e-posta adresleri geçersiz. Şifresiz SMTP yalnız loopback'te kullanılabilir.");
            target = $"{next.Host}:{next.Port} · {next.Security}";
        }
        return store.SaveNotificationChannel(id, input, _protector.Protect(JsonSerializer.Serialize(next)), target);
    }
    private static bool Mailbox(string? value) => value is { Length: > 0 and <= 400 } && !value.Any(char.IsControl) && MailboxAddress.TryParse(value, out var address) && address.Address.Contains('@');
    private static bool Loopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
}
