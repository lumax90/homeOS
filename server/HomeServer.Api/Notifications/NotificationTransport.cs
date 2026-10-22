using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HomeServer.Notifications;

public interface INotificationTransport
{
    Task<NotificationSendResult> SendAsync(NotificationDelivery delivery, NotificationChannel channel, NotificationSecrets secrets, CancellationToken token);
}
public sealed class NotificationTransport(IHttpClientFactory clients, TimeProvider clock, WebPushTransport? push = null) : INotificationTransport
{
    public static byte[] Payload(NotificationDelivery delivery) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        version = 1, deliveryId = delivery.Id, eventId = delivery.NotificationId ?? delivery.Id, kind = delivery.Kind,
        title = SafeText(delivery.Title, 500), message = SafeText(delivery.Message, 4000), contentId = delivery.ContentId, occurredAt = delivery.CreatedAt, test = delivery.IsTest
    });
    private static string SafeText(string text, int maximum) => new(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').Take(maximum).ToArray());
    public async Task<NotificationSendResult> SendAsync(NotificationDelivery delivery, NotificationChannel channel, NotificationSecrets secrets, CancellationToken token)
    {
        try
        {
            return channel.Kind switch
            {
                "webhook" => await Webhook(delivery, secrets, token),
                "smtp" => await Smtp(delivery, secrets, token),
                "push" when push is not null && secrets.Push is not null => await push.SendAsync(delivery, secrets.Push, token),
                _ => throw new DomainException("Bildirim kanal türü kullanılamıyor.", 409)
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (SmtpCommandException ex) { var code = (int)ex.StatusCode; return new(false, code is >= 400 and < 500, $"SMTP {code}: gönderim kabul edilmedi."); }
        catch (Exception ex) when (ex is MailKit.Security.AuthenticationException or SslHandshakeException or NotSupportedException or System.Security.Authentication.AuthenticationException)
        { return new(false, false, "SMTP kimliği veya zorunlu TLS bağlantısı doğrulanamadı."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException or SmtpProtocolException or OperationCanceledException or System.Net.Sockets.SocketException)
        { return new(false, true, "Bildirim hedefinden zamanında doğrulanmış yanıt alınamadı."); }
    }
    private async Task<NotificationSendResult> Webhook(NotificationDelivery job, NotificationSecrets secrets, CancellationToken token)
    {
        using var client = clients.CreateClient("notifications");
        using var request = new HttpRequestMessage(HttpMethod.Post, secrets.Url);
        var payload = Payload(job); request.Content = new ByteArrayContent(payload); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Idempotency-Key", job.Id); request.Headers.Add("X-Home-Event", job.Kind);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture); request.Headers.Add("X-Home-Timestamp", timestamp);
        if (!string.IsNullOrEmpty(secrets.SigningSecret))
        {
            var signed = Encoding.UTF8.GetBytes(timestamp + ".").Concat(payload).ToArray();
            request.Headers.Add("X-Home-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secrets.SigningSecret), signed)));
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var status = (int)response.StatusCode;
        var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : (TimeSpan?)null);
        return response.IsSuccessStatusCode ? new(true, false, $"HTTP {status}: webhook kabul edildi.")
            : new(false, status is 408 or 425 or 429 or >= 500, $"HTTP {status}: webhook kabul edilmedi.", retry);
    }
    private static async Task<NotificationSendResult> Smtp(NotificationDelivery job, NotificationSecrets secrets, CancellationToken token)
    {
        using var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(secrets.From!)); message.To.Add(MailboxAddress.Parse(secrets.To!));
        message.Subject = "Home · " + SafeText(job.Title, 300).Replace('\n', ' ').Replace('\t', ' ');
        message.Date = DateTimeOffset.Parse(job.CreatedAt); message.MessageId = job.Id + "@home.local";
        message.Headers.Add("X-Home-Delivery", job.Id); message.Body = new TextPart("plain") { Text = SafeText(job.Message, 4000) + "\n\nHome · " + (job.IsTest ? "Kullanıcı tarafından istenen deneme iletisi." : "Kişisel bildirim kanalın.") };
        using var client = new SmtpClient { Timeout = 15000, CheckCertificateRevocation = true };
        if (!string.IsNullOrEmpty(secrets.CertificateSha256)) client.ServerCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null && Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)) == secrets.CertificateSha256
            && DateTimeOffset.UtcNow >= DateTimeOffset.Parse(certificate.GetEffectiveDateString()) && DateTimeOffset.UtcNow <= DateTimeOffset.Parse(certificate.GetExpirationDateString());
        await client.ConnectAsync(secrets.Host!, secrets.Port, secrets.Security switch { "ssl" => SecureSocketOptions.SslOnConnect, "none" => SecureSocketOptions.None, _ => SecureSocketOptions.StartTls }, token);
        if (!string.IsNullOrEmpty(secrets.Username)) await client.AuthenticateAsync(secrets.Username, secrets.Password!, token);
        await client.SendAsync(message, token);
        try { await client.DisconnectAsync(true, token); } catch (Exception ex) when (ex is IOException or SmtpProtocolException or OperationCanceledException) { }
        return new(true, false, "SMTP sunucusu iletiyi kabul etti.");
    }
}
