namespace HomeServer.Core;

public sealed record PushServerConfiguration(bool Enabled = false, string Subject = "", string? PublicKey = null, int Revision = 0, int KeyRevision = 0);
public sealed record PushServerInput(bool Enabled, string Subject, int Revision, bool RotateKey = false);
public sealed record PushServerSnapshot(PushServerConfiguration Configuration, string? ProtectedKey);
public sealed record BrowserPushKeys(string P256dh, string Auth);
public sealed record BrowserPushInput(string Endpoint, BrowserPushKeys Keys, string ApplicationServerKey, int KeyRevision,
    string ExpectedUserId, string ExpectedSessionId, string Name, string[] Events, int? ExpectedRevision = null, long? ExpirationTime = null);
public sealed record BrowserPushSecret(string Endpoint, string P256dh, string Auth, string BindingId, int KeyRevision);
public sealed record BrowserPushRegistration(string ChannelId, string UserId, string SessionId, string BindingId, string Name, string[] Events, int Revision, int KeyRevision, string ExpiresAt);
public sealed record BrowserPushDevice(string ChannelId, string Name, string DeviceName, int Revision, string[] Events, bool Current);
public sealed record BrowserPushBinding(string ChannelId, string UserId, string LoginId, string DeviceId, string EndpointHash, string KeysHash, string BindingId, int KeyRevision, string ExpiresAt);
