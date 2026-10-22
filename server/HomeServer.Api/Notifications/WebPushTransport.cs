using System.Text.Json;
using HomeServer.Core;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace HomeServer.Notifications;

public sealed class WebPushTransport(IHttpClientFactory clients, WebPushSettings settings, TimeProvider clock)
{
    public async Task<NotificationSendResult> SendAsync(NotificationDelivery delivery, BrowserPushSecret subscription, CancellationToken token)
    {
        _ = WebPushSettings.ValidateEndpoint(subscription.Endpoint);
        var material = settings.Vapid(subscription.KeyRevision);
        using var authentication = new VapidAuthentication(material.Configuration.PublicKey!, material.PrivateKey) { Subject = material.Configuration.Subject, Expiration = 3600 };
        using var http = clients.CreateClient("web-push");
        var service = new PushServiceClient(http) { AutoRetryAfter = false, DefaultTimeToLive = 60 };
        var message = new PushMessage(JsonSerializer.Serialize(new
        {
            v = 1, bindingId = subscription.BindingId, deliveryId = delivery.Id, test = delivery.IsTest,
            expiresAt = clock.GetUtcNow().AddMinutes(2).ToUnixTimeMilliseconds()
        })) { TimeToLive = 60, Topic = delivery.Id, Urgency = PushMessageUrgency.Normal };
        try
        {
            await service.RequestPushMessageDeliveryAsync(new PushSubscription
            {
                Endpoint = subscription.Endpoint, Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth }
            }, message, authentication, token);
            return new(true, false, "Push servisi şifreli bildirimi kabul etti.");
        }
        catch (PushServiceClientException ex)
        {
            var status = (int)ex.StatusCode;
            var retry = ex.Headers?.RetryAfter?.Delta ?? (ex.Headers?.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : (TimeSpan?)null);
            return new(false, status is 408 or 425 or 429 or >= 500, status is 404 or 410 ? "Tarayıcı push aboneliğinin süresi doldu; bu cihazda yeniden etkinleştir." : $"Push HTTP {status}: bildirim kabul edilmedi.", retry, status is 404 or 410);
        }
    }
}

/// <summary>The protocol library only needs status/headers; do not buffer a remote error body or retain capability URLs in errors.</summary>
public sealed class PushResponseHeadersOnlyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        response.Content.Dispose(); response.Content = new ByteArrayContent([]); response.ReasonPhrase = "Push response";
        return response;
    }
}
