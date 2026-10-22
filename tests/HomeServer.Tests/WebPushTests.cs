using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class WebPushTests
{
    [Fact]
    public async Task RealVapidSignatureAndEncryptedPayloadValidateAtAnIndependentReceiver()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home);
        var subscribed = fixture.Subscribe(); var repeated = fixture.Subscribe(); Assert.Equal(subscribed.ChannelId, repeated.ChannelId); Assert.Equal(subscribed.BindingId, repeated.BindingId); Assert.Equal(subscribed.Revision, repeated.Revision);
        var connection = home.Store.GetNotificationConnection(subscribed.ChannelId)!;
        Assert.DoesNotContain(fixture.Input.Endpoint, JsonSerializer.Serialize(connection.Channel)); Assert.DoesNotContain(fixture.Input.Keys.Auth, connection.ProtectedSecrets);
        var material = fixture.Push.Vapid(subscribed.KeyRevision); Assert.DoesNotContain(material.PrivateKey, home.Store.GetPushServerSnapshot().ProtectedKey!);
        home.Store.PublishNotification("1", "request", "PRIVATE MEDIA TITLE", "PRIVATE CONTENT BODY", "push-one");
        var delivery = Assert.Single(home.Store.GetNotificationDeliveries("1")); Assert.True(await fixture.Dispatcher.RunOnceAsync(CancellationToken.None));
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("60", request.Ttl); Assert.Equal(delivery.Id, request.Topic); Assert.Equal("aes128gcm", request.Encoding); Assert.Null(request.Cookie);
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(request.Body));
        var auth = request.Authorization!; Assert.StartsWith("vapid ", auth);
        var parts = auth[6..].Split(',').Select(value => value.Trim()).ToDictionary(value => value[..value.IndexOf('=')], value => value[(value.IndexOf('=') + 1)..]);
        Assert.Equal(fixture.Configuration.PublicKey, parts["k"]);
        var jwt = parts["t"].Split('.'); using var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwt[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString()); Assert.Equal("mailto:operator@example.test", claims.RootElement.GetProperty("sub").GetString());
        Assert.InRange(claims.RootElement.GetProperty("exp").GetInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 3500, 3700);
        using var signer = ECDsa.Create(Public(WebEncoders.Base64UrlDecode(parts["k"])));
        Assert.True(signer.VerifyData(Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), WebEncoders.Base64UrlDecode(jwt[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var plaintext = Decrypt(request.Body, fixture.Agent, fixture.AgentPublic, fixture.Auth);
        Assert.DoesNotContain("PRIVATE", plaintext); using var message = JsonDocument.Parse(plaintext);
        Assert.Equal(subscribed.BindingId, message.RootElement.GetProperty("bindingId").GetString()); Assert.Equal(delivery.Id, message.RootElement.GetProperty("deliveryId").GetString());
        Assert.Equal("sent", home.Store.GetNotificationDelivery(delivery.Id)!.Status);
    }
    [Theory]
    [InlineData("http://fcm.googleapis.com/token")]
    [InlineData("https://127.0.0.1/token")]
    [InlineData("https://fcm.googleapis.com.attacker.test/token")]
    [InlineData("https://fcm.googleapis.com:444/token")]
    [InlineData("https://user:secret@fcm.googleapis.com/token")]
    [InlineData("https://updates.push.services.mozilla.com.attacker.test/token")]
    [InlineData("https://web.push.apple.com/token#fragment")]
    public void NormalAccountsCannotTurnPushSubscriptionsIntoArbitraryHttpRequests(string endpoint)
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home);
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { Endpoint = endpoint })); Assert.Empty(home.Store.GetNotificationChannels()); Assert.Empty(fixture.Handler.Requests);
    }
    [Fact]
    public void InvalidCurveKeysAndStaleAccountCommandsCannotChangeTheDeviceBinding()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var valid = fixture.Subscribe();
        var invalid = new byte[65]; invalid[0] = 4;
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { Keys = fixture.Input.Keys with { P256dh = WebEncoders.Base64UrlEncode(invalid) } }));
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { Keys = fixture.Input.Keys with { Auth = "short" } }));
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { ExpectedSessionId = Guid.NewGuid().ToString("N") }));
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { Name = "Outdated form", ExpectedRevision = valid.Revision - 1 }));
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input with { ExpirationTime = 0 }));
        Assert.Equal(valid.BindingId, home.Store.GetCurrentBrowserPush(fixture.Actor)!.BindingId);
        var otherLogin = home.Store.CreateLoginSession("1", 1, "Another browser"); var other = home.Store.GetPlaybackActor("1", otherLogin.Id);
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(other, fixture.Input with { ExpectedSessionId = otherLogin.Id }));
    }
    [Fact]
    public void LogoutCancelsQueuedPushAndAProfileSwitchGetsANewBindingWithoutOldHistory()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var first = fixture.Subscribe();
        var queued = home.Store.QueueNotificationTest(first.ChannelId, first.Revision, DateTimeOffset.UtcNow);
        home.Store.RevokeLoginSession(fixture.Actor.LoginId, fixture.Actor.UserId);
        Assert.Null(home.Store.GetBrowserPushBinding(first.ChannelId)); Assert.Equal("", home.Store.GetNotificationConnection(first.ChannelId)!.ProtectedSecrets);
        Assert.Equal("cancelled", home.Store.GetNotificationDelivery(queued.Id)!.Status);
        var member = home.Store.SaveAccount(null, new("Member"), "fixture"); var login = home.Store.CreateLoginSession(member.Id, 1, "Shared browser", playbackDeviceId: fixture.Actor.DeviceId);
        var actor = home.Store.GetPlaybackActor(member.Id, login.Id);
        var next = fixture.Push.Subscribe(actor, fixture.Input with { ExpectedUserId = member.Id, ExpectedSessionId = login.Id });
        Assert.NotEqual(first.BindingId, next.BindingId); Assert.NotEqual(first.ChannelId, next.ChannelId); Assert.Empty(home.Store.GetNotificationDeliveries(member.Id));
        Assert.Throws<DomainException>(() => fixture.Push.Subscribe(actor, fixture.Input));
        home.Store.ChangePassword(member.Id, "new-fixture"); Assert.Null(home.Store.GetBrowserPushBinding(next.ChannelId));
    }
    [Fact]
    public void VapidRotationIsRevisionedAndRevokesSubscriptionsButContactEditsKeepTheKey()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var first = fixture.Subscribe();
        var contact = fixture.Push.Save(new(true, "mailto:updated@example.test", fixture.Configuration.Revision));
        Assert.Equal(fixture.Configuration.PublicKey, contact.PublicKey); Assert.NotNull(home.Store.GetCurrentBrowserPush(fixture.Actor));
        Assert.Throws<DomainException>(() => fixture.Push.Save(new(true, "mailto:stale@example.test", fixture.Configuration.Revision)));
        var changed = fixture.Push.Save(new(true, contact.Subject, contact.Revision, true)); Assert.NotEqual(contact.PublicKey, changed.PublicKey); Assert.True(changed.KeyRevision > contact.KeyRevision);
        Assert.Null(home.Store.GetCurrentBrowserPush(fixture.Actor)); Assert.Throws<DomainException>(() => fixture.Push.Subscribe(fixture.Actor, fixture.Input));
        Assert.Equal("", home.Store.GetNotificationConnection(first.ChannelId)!.ProtectedSecrets);
        Assert.Throws<DomainException>(() => fixture.Push.Save(new(true, "mailto:bad\"claim@example.test", changed.Revision)));
    }
    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task GonePushEndpointsAreRetiredWithoutLeakingTheirCapabilityUrl(int status)
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var subscription = fixture.Subscribe(); fixture.Handler.Status = (HttpStatusCode)status;
        var job = home.Store.QueueNotificationTest(subscription.ChannelId, subscription.Revision, DateTimeOffset.UtcNow);
        Assert.True(await fixture.Dispatcher.RunOnceAsync(CancellationToken.None)); Assert.Null(home.Store.GetCurrentBrowserPush(fixture.Actor));
        Assert.Equal("failed", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.DoesNotContain("endpoint-secret", home.Store.GetNotificationDelivery(job.Id)!.Result);
        Assert.Equal("", home.Store.GetNotificationConnection(subscription.ChannelId)!.ProtectedSecrets); Assert.Single(fixture.Handler.Requests);
        Assert.False(await fixture.Dispatcher.RunOnceAsync(CancellationToken.None));
    }
    [Fact]
    public async Task PushRetryUsesDurableHomeAttemptsRatherThanTheLibraryRetryLoop()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var subscription = fixture.Subscribe(); fixture.Handler.Status = HttpStatusCode.TooManyRequests;
        var job = home.Store.QueueNotificationTest(subscription.ChannelId, subscription.Revision, DateTimeOffset.UtcNow);
        await fixture.Dispatcher.RunOnceAsync(CancellationToken.None); Assert.Single(fixture.Handler.Requests);
        var waiting = home.Store.GetNotificationDelivery(job.Id)!; Assert.Equal("queued", waiting.Status); Assert.Equal(1, waiting.Attempts); Assert.Single(home.Store.GetNotificationAttempts(job.Id));
        Assert.InRange((DateTimeOffset.Parse(waiting.NextAttemptAt) - DateTimeOffset.UtcNow).TotalSeconds, 35, 50);
        Assert.False(await fixture.Dispatcher.RunOnceAsync(CancellationToken.None));
    }
    [Fact]
    public async Task RevokingLoginWhileSendingCancelsTheEncryptedRequestAndFencesCompletion()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var subscription = fixture.Subscribe(); fixture.Handler.Block = true;
        var job = home.Store.QueueNotificationTest(subscription.ChannelId, subscription.Revision, DateTimeOffset.UtcNow);
        var work = fixture.Dispatcher.RunOnceAsync(CancellationToken.None); await fixture.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        home.Store.RevokeLoginSession(fixture.Actor.LoginId, "1"); await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Handler.Cancelled); Assert.Equal("cancelled", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.Null(home.Store.GetBrowserPushBinding(subscription.ChannelId));
    }
    [Fact]
    public void MigrationFrom24PreservesExistingChannelsDeliveriesAttemptsAndForeignKeys()
    {
        using var home = new TestWorkspace(); var root = Path.Combine(home.Root, "old-state"); Directory.CreateDirectory(root);
        var paths = new HomePaths(root, Path.Combine(root, "thumbnails"), null); var assembly = typeof(MediaStore).Assembly;
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "home.db")}"))
        {
            db.Open();
            using (var pragma = db.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=OFF"; pragma.ExecuteNonQuery(); }
            foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                var version = int.Parse(name.Split('.')[^2][..3]); if (version > 24) continue;
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!); using var command = db.CreateCommand(); command.CommandText = reader.ReadToEnd() + $"\nPRAGMA user_version={version}"; command.ExecuteNonQuery();
            }
        }
        var old = new MediaStore(paths); old.CreateOwner(new("Owner", "fixture"));
        var settings = new NotificationSettings(old, new EphemeralDataProtectionProvider()); var channel = settings.Save(null, new("1", "Existing webhook", "webhook", true, ["request"], Url: "https://example.test/capability"));
        var delivery = old.QueueNotificationTest(channel.Id, channel.Revision, DateTimeOffset.UtcNow); var attempt = old.ClaimNotificationDelivery(DateTimeOffset.UtcNow.AddSeconds(1))!;
        old.FinishNotificationDelivery(attempt, new(true, false, "Accepted"), DateTimeOffset.UtcNow);
        old.Initialize(); Assert.Equal("Existing webhook", old.GetNotificationConnection(channel.Id)!.Channel.Name); Assert.Equal("sent", old.GetNotificationDelivery(delivery.Id)!.Status); Assert.Single(old.GetNotificationAttempts(delivery.Id));
        using var check = new SqliteConnection($"Data Source={Path.Combine(root, "home.db")}"); check.Open(); using var query = check.CreateCommand(); query.CommandText = "PRAGMA foreign_key_check"; using var violations = query.ExecuteReader(); Assert.False(violations.Read());
        Assert.Equal("https://example.test/capability", settings.Secrets(channel.Id).Url);
    }
    [Fact]
    public void DeletingAndRecreatingPushChannelsCannotResetTheUsersHourlyAttemptBudget()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home); var subscription = fixture.Subscribe();
        for (var i = 0; i < 120; i++)
        {
            if (i == 60) { home.Store.RemoveBrowserPush(fixture.Actor, subscription.ChannelId, subscription.Revision); subscription = fixture.Subscribe(); }
            var now = DateTimeOffset.UtcNow; home.Store.QueueNotificationTest(subscription.ChannelId, subscription.Revision, now);
            var attempt = home.Store.ClaimNotificationDelivery(now.AddSeconds(1)); Assert.NotNull(attempt); home.Store.FinishNotificationDelivery(attempt, new(true, false, "Isolated accepted attempt"), now.AddSeconds(1));
        }
        home.Store.RemoveBrowserPush(fixture.Actor, subscription.ChannelId, subscription.Revision); subscription = fixture.Subscribe();
        home.Store.PruneNotificationDeliveries(DateTimeOffset.UtcNow);
        var pending = home.Store.QueueNotificationTest(subscription.ChannelId, subscription.Revision, DateTimeOffset.UtcNow);
        Assert.Null(home.Store.ClaimNotificationDelivery(DateTimeOffset.UtcNow.AddSeconds(1))); Assert.Contains("Hesabın", home.Store.GetNotificationDelivery(pending.Id)!.Result); Assert.Empty(fixture.Handler.Requests);
    }
    [Fact]
    public void ExpiredRegistrationTombstonesStillEnforceTheHourlyCreationLimit()
    {
        using var home = new TestWorkspace(); using var fixture = new Fixture(home);
        for (var i = 0; i < 30; i++) { var value = fixture.Subscribe(); home.Store.RemoveBrowserPush(fixture.Actor, value.ChannelId, value.Revision); }
        home.Store.PruneNotificationDeliveries(DateTimeOffset.UtcNow);
        Assert.Equal(429, Assert.Throws<DomainException>(() => fixture.Subscribe()).StatusCode); Assert.Empty(home.Store.GetNotificationChannels());
    }
    private static ECParameters Public(byte[] value) => new() { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = value[1..33], Y = value[33..65] } };
    // Receiver implementation follows RFC 8291; it doesn't call the sending library's encryption helpers.
    private static string Decrypt(byte[] body, ECDiffieHellman agent, byte[] agentPublic, byte[] auth)
    {
        Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4))); Assert.Equal(65, body[20]);
        var serverPublic = body[21..86]; using var peer = ECDiffieHellman.Create(Public(serverPublic));
        var shared = agent.DeriveRawSecretAgreement(peer.PublicKey); var key = HMACSHA256.HashData(auth, shared);
        var ikm = HMACSHA256.HashData(key.AsSpan(), [..Encoding.ASCII.GetBytes("WebPush: info\0"), ..agentPublic, ..serverPublic, 1]);
        var prk = HMACSHA256.HashData(body[..16], ikm);
        var cek = HMACSHA256.HashData(prk.AsSpan(), [..Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"), 1])[..16];
        var nonce = HMACSHA256.HashData(prk.AsSpan(), [..Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"), 1])[..12];
        var ciphertext = body[86..^16]; var plain = new byte[ciphertext.Length]; using var aes = new AesGcm(cek, 16); aes.Decrypt(nonce, ciphertext, body[^16..], plain);
        var end = plain.Length - 1; while (end >= 0 && plain[end] == 0) end--; Assert.True(end >= 0); Assert.Equal(2, plain[end]); return Encoding.UTF8.GetString(plain, 0, end);
    }
    private sealed class Fixture : IDisposable
    {
        public ECDiffieHellman Agent { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); public byte[] AgentPublic { get; } public byte[] Auth { get; } = RandomNumberGenerator.GetBytes(16);
        public WebPushSettings Push { get; } public PushServerConfiguration Configuration { get; } public PlaybackActor Actor { get; } public BrowserPushInput Input { get; }
        public PushHandler Handler { get; } = new(); public NotificationDispatcher Dispatcher { get; }
        public Fixture(TestWorkspace home)
        {
            home.Store.CreateOwner(new("Owner", "fixture")); var login = home.Store.CreateLoginSession("1", 1, "Isolated browser"); Actor = home.Store.GetPlaybackActor("1", login.Id);
            var protection = new EphemeralDataProtectionProvider(); var notifications = new NotificationSettings(home.Store, protection); Push = new(home.Store, protection, notifications); Configuration = Push.Save(new(true, "mailto:operator@example.test", 0));
            var parameters = Agent.ExportParameters(false); AgentPublic = [4, ..parameters.Q.X!, ..parameters.Q.Y!];
            Input = new("https://fcm.googleapis.com/fcm/send/endpoint-secret", new(WebEncoders.Base64UrlEncode(AgentPublic), WebEncoders.Base64UrlEncode(Auth)), Configuration.PublicKey!, Configuration.KeyRevision, Actor.UserId, Actor.LoginId, "Isolated device", ["request"]);
            var clients = new Clients(Handler); Dispatcher = new(home.Store, notifications, new NotificationTransport(clients, TimeProvider.System, new WebPushTransport(clients, Push, TimeProvider.System)), TimeProvider.System, new EventHub());
        }
        public BrowserPushRegistration Subscribe() => Push.Subscribe(Actor, Input);
        public void Dispose() { Agent.Dispose(); Handler.Dispose(); }
    }
    private sealed class Clients(PushHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(new PushResponseHeadersOnlyHandler { InnerHandler = handler }, false); }
    private sealed record Sent(byte[] Body, string? Authorization, string? Cookie, string Ttl, string Topic, string Encoding);
    private sealed class PushHandler : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = []; public HttpStatusCode Status = HttpStatusCode.Created; public bool Block; public bool Cancelled;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(new(await request.Content!.ReadAsByteArrayAsync(token), request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("Cookie", out var cookie) ? cookie.Single() : null,
                request.Headers.GetValues("TTL").Single(), request.Headers.GetValues("Topic").Single(), request.Content.Headers.ContentEncoding.Single())); Started.TrySetResult();
            if (Block) try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { Cancelled = true; throw; }
            var response = new HttpResponseMessage(Status) { Content = new UnreadableContent() }; if (Status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45)); return response;
        }
    }
    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("A remote response body must not be read.");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
