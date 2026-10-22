namespace HomeServer.Core;

public sealed record NotificationChannel(string Id, string UserId, string Name, string Kind, bool Enabled, string[] Events, int MaximumHourlyAttempts,
    int Revision, string Target, bool SecretConfigured, string CreatedAt, int Dropped, bool Deleted = false);
public sealed record NotificationChannelInput(string UserId, string Name, string Kind, bool Enabled, string[] Events, int MaximumHourlyAttempts = 60,
    int Revision = 0, string? Url = null, string? SigningSecret = null, string? Host = null, int? Port = null, string? Security = null,
    string? Username = null, string? Password = null, string? From = null, string? To = null, string? CertificateSha256 = null);
public sealed record NotificationSecrets(string? Url = null, string? SigningSecret = null, string? Host = null, int Port = 587,
    string Security = "starttls", string? Username = null, string? Password = null, string? From = null, string? To = null, string? CertificateSha256 = null, BrowserPushSecret? Push = null);
public sealed record NotificationConnection(NotificationChannel Channel, string ProtectedSecrets);
public sealed record NotificationDelivery(string Id, string ChannelId, string ChannelName, string UserId, int ChannelRevision, int UserVersion,
    string? NotificationId, string Kind, string Title, string Message, string? ContentId, string CreatedAt, bool IsTest, string Status,
    int Attempts, int Revision, [property: System.Text.Json.Serialization.JsonIgnore] string? Lease, string NextAttemptAt, string? FinishedAt, string Result);
public sealed record NotificationDeliveryCommand(int Revision);
public sealed record NotificationDeliveryAttempt(int Number, string StartedAt, string? FinishedAt, string Result);
public sealed record NotificationSendResult(bool Success, bool Retryable, string Message, TimeSpan? RetryAfter = null, bool SubscriptionExpired = false);
