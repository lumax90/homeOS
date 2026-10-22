using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using MimeKit;

namespace HomeServer.Tests;

public sealed class NotificationDeliveryTests
{
    private const string Secret = "isolated-signature-secret";
    private static NotificationChannelInput Hook(string user = "1", int maximum = 60) => new(user, "Isolated webhook", "webhook", true, ["request"], maximum, Url: "https://notify.example.test/hook?key=private-destination", SigningSecret: Secret);
    [Fact]
    public async Task TransactionalOutboxFiltersDeduplicatesSignsAndProtectsConnectionSecrets()
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home);
        var channel = setup.Settings.Save(null, Hook()); Assert.Equal("https://notify.example.test", channel.Target);
        var protectedValue = home.Store.GetNotificationConnection(channel.Id)!.ProtectedSecrets; Assert.DoesNotContain(Secret, protectedValue); Assert.DoesNotContain("private-destination", JsonSerializer.Serialize(channel));
        home.Store.PublishNotification("1", "request", "İzole istek", "Türkçe ileti", "event-one");
        home.Store.PublishNotification("1", "request", "Duplicate", "ignored", "event-one"); home.Store.PublishNotification("1", "music", "Other event", "ignored", "event-two");
        var other = home.Store.SaveAccount(null, new("Other"), "fixture"); home.Store.PublishNotification(other.Id, "request", "Private", "other user", "event-three");
        var job = Assert.Single(home.Store.GetNotificationDeliveries("1")); Assert.Empty(home.Store.GetNotificationDeliveries(other.Id));
        Assert.True(await setup.Dispatcher.RunOnceAsync(CancellationToken.None)); var sent = Assert.Single(setup.Handler.Requests);
        Assert.Null(sent.Authorization); Assert.Null(sent.Cookie); Assert.Equal(job.Id, sent.Id);
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(sent.Timestamp + ".").Concat(sent.Body).ToArray()));
        Assert.Equal("sha256=" + expected, sent.Signature); using var json = JsonDocument.Parse(sent.Body); Assert.Equal("Türkçe ileti", json.RootElement.GetProperty("message").GetString()); Assert.False(json.RootElement.GetProperty("test").GetBoolean());
        Assert.Equal("sent", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.Single(home.Store.GetNotificationAttempts(job.Id));
    }
    [Fact]
    public async Task RetryAfterAndAttemptBudgetIncludeTestMessagesAndKeepOneDeliveryIdentity()
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); var channel = setup.Settings.Save(null, Hook(maximum: 1));
        setup.Handler.Status = HttpStatusCode.TooManyRequests; setup.Handler.RetryAfter = TimeSpan.FromSeconds(45);
        var job = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow()); Assert.True(await setup.Dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.Equal("queued", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.Equal(1, home.Store.GetNotificationDelivery(job.Id)!.Attempts);
        setup.Clock.Now += TimeSpan.FromSeconds(46); Assert.False(await setup.Dispatcher.RunOnceAsync(CancellationToken.None)); Assert.Single(setup.Handler.Requests);
        home.Store.PublishNotification("1", "request", "Queued event", "pending", "second");
        setup.Clock.Now += TimeSpan.FromHours(1); setup.Handler.Status = HttpStatusCode.OK;
        Assert.True(await setup.Dispatcher.RunOnceAsync(CancellationToken.None)); Assert.False(await setup.Dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, setup.Handler.Requests.Count);
        setup.Clock.Now += TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(2)); Assert.True(await setup.Dispatcher.RunOnceAsync(CancellationToken.None));
        var sameDelivery = setup.Handler.Requests.Where(request => request.Id == job.Id).ToArray(); Assert.Equal(2, sameDelivery.Length); Assert.Equal(sameDelivery[0].Body, sameDelivery[1].Body);
        Assert.Equal("sent", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.Equal(2, home.Store.GetNotificationAttempts(job.Id).Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InFlightConnectionOrAccountRevocationCancelsWithoutRetargeting(bool connection)
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); var channel = setup.Settings.Save(null, Hook());
        setup.Handler.Block = true; var job = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow());
        var work = setup.Dispatcher.RunOnceAsync(CancellationToken.None); await setup.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (connection) { setup.Settings.Save(channel.Id, Hook() with { Revision = channel.Revision, Url = "https://changed.example.test/new?secret=another", SigningSecret = "replacement-fixture-secret" }); setup.Dispatcher.Interrupt(channelId: channel.Id); }
        else home.Store.SaveAccount("1", new("Owner", Role: "admin"));
        await work.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(setup.Handler.Cancelled); Assert.Equal("cancelled", home.Store.GetNotificationDelivery(job.Id)!.Status);
        Assert.Equal("notify.example.test", Assert.Single(setup.Handler.Requests).Host);
    }
    [Fact]
    public void CrashRecoveryFencesTheOldLeaseAndRetainsAttemptsAndStableMessageIdentity()
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); var channel = setup.Settings.Save(null, Hook());
        var queued = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow()); var old = home.Store.ClaimNotificationDelivery(setup.Clock.GetUtcNow())!;
        home.Store.RecoverNotificationDeliveries(setup.Clock.GetUtcNow()); home.Store.FinishNotificationDelivery(old, new(true, false, "stale success"), setup.Clock.GetUtcNow());
        Assert.Equal("queued", home.Store.GetNotificationDelivery(queued.Id)!.Status); Assert.False(home.Store.NotificationLeaseCurrent(old));
        setup.Clock.Now += TimeSpan.FromSeconds(31); var next = home.Store.ClaimNotificationDelivery(setup.Clock.GetUtcNow())!;
        Assert.Equal(old.Id, next.Id); Assert.NotEqual(old.Lease, next.Lease); Assert.Equal(2, next.Attempts); Assert.Equal(NotificationTransport.Payload(old), NotificationTransport.Payload(next));
        home.Store.FinishNotificationDelivery(next, new(true, false, "accepted"), setup.Clock.GetUtcNow()); Assert.Equal("sent", home.Store.GetNotificationDelivery(queued.Id)!.Status);
        Assert.Contains("bilinmiyor", home.Store.GetNotificationAttempts(queued.Id)[0].Result);
    }
    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(422)]
    public async Task PermanentHttpFailureDoesNotRetryOrExposeResponseSecrets(int status)
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); var channel = setup.Settings.Save(null, Hook()); setup.Handler.Status = (HttpStatusCode)status;
        var job = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow()); await setup.Dispatcher.RunOnceAsync(CancellationToken.None);
        var failed = home.Store.GetNotificationDelivery(job.Id)!; Assert.Equal("failed", failed.Status); Assert.DoesNotContain("remote-password", failed.Result); Assert.Single(setup.Handler.Requests);
        Assert.False(await setup.Dispatcher.RunOnceAsync(CancellationToken.None));
    }
    [Fact]
    public void QueueOverflowPreservesInboxAndRecordsDroppedDeliveries()
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); var channel = setup.Settings.Save(null, Hook());
        using var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"); db.Open(); using (var tx = db.BeginTransaction())
        {
            using var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO notifications(id,user_id,kind,title,message,created_at,dedup_key) VALUES($id,'1','request','Isolated','Fixture',$now,$id)";
            var id = command.Parameters.AddWithValue("$id", ""); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            for (var i = 0; i < 1005; i++) { id.Value = Guid.NewGuid().ToString("N"); command.ExecuteNonQuery(); } tx.Commit();
        }
        using var count = db.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM notifications"; Assert.Equal(1005L, count.ExecuteScalar());
        count.CommandText = "SELECT COUNT(*) FROM notification_deliveries"; Assert.Equal(1000L, count.ExecuteScalar()); Assert.Equal(5, home.Store.GetNotificationConnection(channel.Id)!.Channel.Dropped);
        Assert.Equal(429, Assert.Throws<DomainException>(() => home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow())).StatusCode);
    }
    [Theory]
    [InlineData("ssl")]
    [InlineData("starttls")]
    public async Task RealSmtpTlsAuthenticatesAndSendsUtf8MimeWithStableMessageId(string security)
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); await using var receiver = new SmtpFixture(security);
        var input = new NotificationChannelInput("1", "Isolated SMTP", "smtp", true, ["request"], Host: "127.0.0.1", Port: receiver.Port, Security: security,
            Username: "fixture-user", Password: "fixture-password", From: "Home <sender@example.test>", To: "target@example.test", CertificateSha256: receiver.Fingerprint);
        var channel = setup.Settings.Save(null, input); home.Store.PublishNotification("1", "request", "Çağrı ve İstek", "İzole Türkçe e-posta.\n.Test satırı", "mail-one");
        var job = Assert.Single(home.Store.GetNotificationDeliveries("1")); Assert.True(await setup.Dispatcher.RunOnceAsync(CancellationToken.None));
        var received = await receiver.Result.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(receiver.Authenticated); Assert.True(receiver.Encrypted);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(received)); using var message = MimeMessage.Load(bytes);
        Assert.Equal("Home · Çağrı ve İstek", message.Subject); Assert.Contains("İzole Türkçe", message.TextBody); Assert.Equal(job.Id + "@home.local", message.MessageId);
        Assert.Equal("sent", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.DoesNotContain("fixture-password", JsonSerializer.Serialize(channel));
    }
    [Fact]
    public async Task RequiredStartTlsNeverDowngradesToCleartext()
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); await using var receiver = new SmtpFixture("none");
        var channel = setup.Settings.Save(null, new("1", "TLS required", "smtp", true, ["request"], Host: "127.0.0.1", Port: receiver.Port, From: "sender@example.test", To: "target@example.test"));
        var job = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow()); await setup.Dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.Equal("failed", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.Equal("", await receiver.Result.WaitAsync(TimeSpan.FromSeconds(10))); Assert.False(receiver.Authenticated);
        Assert.Throws<DomainException>(() => setup.Settings.Save(null, new("1", "Unsafe", "smtp", true, ["request"], Host: "mail.example.test", Security: "none", From: "sender@example.test", To: "target@example.test")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UntrustedOrWrongPinnedSmtpCertificateIsRejectedBeforeCredentials(bool wrongPin)
    {
        using var home = new TestWorkspace(); using var setup = new Setup(home); await using var receiver = new SmtpFixture("ssl");
        var channel = setup.Settings.Save(null, new("1", "Certificate rejection", "smtp", true, ["request"], Host: "127.0.0.1", Port: receiver.Port, Security: "ssl",
            Username: "fixture-user", Password: "fixture-password", From: "sender@example.test", To: "target@example.test", CertificateSha256: wrongPin ? new string('0', 64) : null));
        var job = home.Store.QueueNotificationTest(channel.Id, channel.Revision, setup.Clock.GetUtcNow()); await setup.Dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.Equal("failed", home.Store.GetNotificationDelivery(job.Id)!.Status); Assert.False(receiver.Authenticated); Assert.Equal(1, home.Store.GetNotificationDelivery(job.Id)!.Attempts);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(1); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Setup : IDisposable
    {
        public Handler Handler { get; } = new(); public Clock Clock { get; } = new(); public NotificationSettings Settings { get; } public NotificationDispatcher Dispatcher { get; }
        public Setup(TestWorkspace home)
        {
            home.Store.CreateOwner(new("Owner", "fixture")); Settings = new(home.Store, new EphemeralDataProtectionProvider());
            Dispatcher = new(home.Store, Settings, new NotificationTransport(new Clients(Handler), Clock), Clock, new EventHub());
        }
        public void Dispose() => Handler.Dispose();
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed record Sent(string Id, string Host, string Timestamp, string? Signature, byte[] Body, string? Authorization, string? Cookie);
    private sealed class Handler : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = []; public HttpStatusCode Status = HttpStatusCode.OK; public TimeSpan? RetryAfter; public bool Block; public bool Cancelled;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string? Header(string key) => request.Headers.TryGetValues(key, out var values) ? values.Single() : null;
            Requests.Add(new(Header("Idempotency-Key")!, request.RequestUri!.Host, Header("X-Home-Timestamp")!, Header("X-Home-Signature"), await request.Content!.ReadAsByteArrayAsync(token), Header("Authorization"), Header("Cookie"))); Started.TrySetResult();
            if (Block) try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { Cancelled = true; throw; }
            var response = new HttpResponseMessage(Status) { Content = new StringContent("remote-password=sensitive-response") };
            if (RetryAfter is { } delay) response.Headers.RetryAfter = new RetryConditionHeaderValue(delay); return response;
        }
    }
    private sealed class SmtpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15)); private readonly X509Certificate2 _certificate;
        public int Port { get; } public bool Authenticated { get; private set; } public bool Encrypted { get; private set; } public string Fingerprint { get; } public Task<string> Result { get; }
        public SmtpFixture(string security)
        {
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null); Fingerprint = Convert.ToHexStringLower(_certificate.GetCertHash(HashAlgorithmName.SHA256));
            _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; Result = Run(security);
        }
        private async Task<string> Run(string security)
        {
            using var socket = await _listener.AcceptTcpClientAsync(_stop.Token); Stream stream = socket.GetStream(); var data = new StringBuilder(); StreamReader? reader = null; StreamWriter? writer = null;
            async Task Tls()
            {
                reader?.Dispose(); writer?.Dispose(); var ssl = new SslStream(stream, false); stream = ssl;
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token); Encrypted = true;
            }
            void Readers() { reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true); writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { NewLine = "\r\n", AutoFlush = true }; }
            try
            {
                if (security == "ssl") await Tls(); Readers(); await writer!.WriteLineAsync("220 isolated.test ESMTP");
                while (await reader!.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.StartsWith("EHLO", StringComparison.Ordinal))
                    {
                        await writer!.WriteLineAsync("250-isolated.test"); await writer.WriteLineAsync("250-8BITMIME");
                        if (security == "starttls" && !Encrypted) await writer.WriteLineAsync("250-STARTTLS");
                        await writer.WriteLineAsync("250 AUTH PLAIN");
                    }
                    else if (line == "STARTTLS") { await writer!.WriteLineAsync("220 TLS ready"); await Tls(); Readers(); }
                    else if (line.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
                    {
                        var value = line.Length > 11 ? line[11..].Trim() : ""; if (value.Length == 0) { await writer!.WriteLineAsync("334 "); value = (await reader.ReadLineAsync(_stop.Token))!; }
                        Authenticated = Encoding.UTF8.GetString(Convert.FromBase64String(value)) == "\0fixture-user\0fixture-password"; await writer!.WriteLineAsync(Authenticated ? "235 Authenticated" : "535 Denied");
                    }
                    else if (line.StartsWith("MAIL FROM:", StringComparison.Ordinal) || line.StartsWith("RCPT TO:", StringComparison.Ordinal)) await writer!.WriteLineAsync("250 Accepted");
                    else if (line == "DATA")
                    {
                        await writer!.WriteLineAsync("354 Data"); while (await reader.ReadLineAsync(_stop.Token) is { } body && body != ".") data.Append(body.StartsWith("..", StringComparison.Ordinal) ? body[1..] : body).Append("\r\n"); await writer.WriteLineAsync("250 Queued");
                    }
                    else if (line == "QUIT") { await writer!.WriteLineAsync("221 Bye"); break; }
                    else await writer!.WriteLineAsync("250 OK");
                }
                return data.ToString();
            }
            finally { reader?.Dispose(); writer?.Dispose(); await stream.DisposeAsync(); }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop(); try { await Result; } catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Security.Authentication.AuthenticationException) { }
            _certificate.Dispose(); _stop.Dispose();
        }
    }
}
