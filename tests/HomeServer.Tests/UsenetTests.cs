using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Usenet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class UsenetTests
{
    [Fact]
    public void NzbRejectsCommandInjectionPathsAndDuplicateParts()
    {
        Assert.Throws<DomainException>(() => NzbParser.Parse(Nzb("../escape.mkv", ["one@test"])));
        Assert.Throws<DomainException>(() => NzbParser.Parse(Nzb("file.mkv", ["one@test\r\nQUIT"])));
        Assert.Throws<DomainException>(() => NzbParser.Parse(Nzb("file.mkv", ["one@test", "one@test"])));
        var valid = NzbParser.Parse(Nzb("file.mkv", ["one@test"]));
        Assert.StartsWith("nzb:", valid.Fingerprint);
        Assert.Equal(valid.Fingerprint, NzbParser.Parse(Nzb("renamed.mkv", ["one@test"])).Fingerprint);
        var entity = Encoding.UTF8.GetBytes("<!DOCTYPE nzb [<!ENTITY secret SYSTEM 'file:///C:/Windows/win.ini'>]><nzb><file subject='&secret;'/></nzb>");
        Assert.Throws<DomainException>(() => NzbParser.Parse(entity));
    }

    [Fact]
    public void YencChecksEveryByteEscapeOffsetsAndCrc()
    {
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var lines = Yenc(bytes, "Türkçe.mkv", 1, 0, bytes.Length);
        var result = YencDecoder.Decode(lines, 1024);
        Assert.Equal(bytes, result.Bytes); Assert.Equal("Türkçe.mkv", result.Name);
        Assert.Equal(0xcbf43926u, Crc32.Hash("123456789"u8));
        lines[2][2] ^= 1;
        Assert.Throws<DomainException>(() => YencDecoder.Decode(lines, 1024));
        Assert.Throws<DomainException>(() => YencDecoder.Decode(Yenc(bytes, "../x.mkv", 1, 0, bytes.Length), 1024));
    }

    [Theory]
    [InlineData("tls")]
    [InlineData("starttls")]
    public async Task NntpTlsValidatesCertificatesBeforeSendingCredentials(string security)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var alternative = new SubjectAlternativeNameBuilder(); alternative.AddDnsName("localhost"); alternative.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(alternative.Build()); request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var temporaryCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(temporaryCertificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        await using var fixture = new NntpFixture(security, certificate);
        var server = Server(fixture.Port, security);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<AuthenticationException>(() => NntpConnection.ConnectAsync(server, "private-password", true, timeout.Token));
        Assert.Equal(0, fixture.Authentications);
        await using var connection = await NntpConnection.ConnectAsync(server, "private-password", true, timeout.Token, certificate);
        Assert.Contains("READER", await connection.CapabilitiesAsync(timeout.Token));
        Assert.Equal(1, fixture.Authentications);
    }

    [Fact]
    public async Task NzbResumePreservesVerifiedPartsAndImportsOnlyCompletePayloads()
    {
        using var home = new TestWorkspace(); await using var fixture = new NntpFixture();
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        fixture.Articles["one@test"] = Yenc(bytes[..128], "Fixture.mkv", 1, 0, 256);
        var config = Configuration(); var protection = new EphemeralDataProtectionProvider();
        await using var servers = new UsenetService(home.Store, protection, config);
        await servers.SaveAsync(null, new UsenetServerInput("Local NNTP", "127.0.0.1", fixture.Port, "plain", "fixture", "private-password", Connections: 1));
        var paths = new DownloadFiles(home.Paths);
        var engine = new NzbEngine(paths, home.Store, servers, new Par2Tool(config, home.Store), new PackageExtractor(), protection, config);
        var input = Nzb("Fixture.mkv", ["one@test", "two@test"]);
        var record = engine.AddNzb(input); Assert.Equal(record.Id, engine.AddNzb(input).Id);
        Assert.DoesNotContain("one@test", home.Store.GetDownloadSource(record.Id).Source);
        await using (var first = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None))
        {
            await Finish(first); Assert.False(first.Snapshot.Complete); Assert.Equal("failed", first.Snapshot.Status);
            Assert.Empty(home.Store.GetLibraries());
        }
        fixture.Articles["two@test"] = Yenc(bytes[128..], "Fixture.mkv", 2, 128, 256);
        await using (var resumed = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None))
        {
            await Finish(resumed); Assert.True(resumed.Snapshot.Complete, resumed.Snapshot.Message);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Assert.Single(resumed.Files).FullPath));
            var importer = new DownloadImporter(paths, home.Store, new EventHub());
            await importer.ImportAsync(record, resumed, CancellationToken.None);
            var job = home.Store.TryClaimJob(); Assert.NotNull(job); await home.Scanner.ScanAsync(job, CancellationToken.None);
            await importer.ImportAsync(record, resumed, CancellationToken.None);
            Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        }
        Assert.Equal(1, fixture.Reads["one@test"]); Assert.Equal(2, fixture.Reads["two@test"]);
        await using (var ready = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None))
        { await Finish(ready); Assert.True(ready.Snapshot.Complete); }
        Assert.Equal(1, fixture.Reads["one@test"]);
        Assert.DoesNotContain("private-password", home.Store.GetUsenetServers()[0].ProtectedPassword);
    }

    [Fact]
    public async Task NntpBandwidthWaitIsCancellableAndNewUnlimitedPolicyTakesEffect()
    {
        using var home = new TestWorkspace(); await using var fixture = new NntpFixture();
        var bytes = Enumerable.Repeat((byte)80, 4096).ToArray();
        fixture.Articles["one@test"] = Yenc(bytes, "Fixture.mkv", 1, 0, bytes.Length);
        home.Store.SaveTransferDefaults(new TransferDefaults(DownloadKiB: 1));
        await using var servers = new UsenetService(home.Store, new EphemeralDataProtectionProvider(), Configuration());
        await servers.SaveAsync(null, new UsenetServerInput("Local", "127.0.0.1", fixture.Port, "plain", "fixture", "private-password", Connections: 1));
        Assert.NotNull(await servers.ReadPartAsync(new NzbSegment(1, bytes.Length, "one@test"), [], 10000, CancellationToken.None));
        using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servers.ReadPartAsync(new NzbSegment(1, bytes.Length, "one@test"), [], 10000, cancelled.Token));
        home.Store.SaveTransferDefaults(new TransferDefaults());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(bytes, (await servers.ReadPartAsync(new NzbSegment(1, bytes.Length, "one@test"), [], 10000, timeout.Token))!.Bytes);
    }

    [Fact]
    public async Task MissingArticlesFallBackToTheNextConfiguredServer()
    {
        using var home = new TestWorkspace(); await using var first = new NntpFixture(); await using var fill = new NntpFixture();
        fill.Articles["one@test"] = Yenc([4, 1, 2, 3], "Fixture.mkv", 1, 0, 4);
        await using var servers = new UsenetService(home.Store, new EphemeralDataProtectionProvider(), Configuration());
        await servers.SaveAsync(null, new UsenetServerInput("Primary", "127.0.0.1", first.Port, "plain", "fixture", "private-password", Priority: 1));
        await servers.SaveAsync(null, new UsenetServerInput("Fill", "127.0.0.1", fill.Port, "plain", "fixture", "private-password", Priority: 2));
        var part = await servers.ReadPartAsync(new NzbSegment(1, 4, "one@test"), [], 1024, CancellationToken.None);
        Assert.NotNull(part); Assert.Equal(new byte[] { 4, 1, 2, 3 }, part.Bytes);
        Assert.Equal(1, first.Reads["one@test"]); Assert.Equal(1, fill.Reads["one@test"]);
    }
    [Fact]
    public async Task OwnedNzbChecksBudgetBeforeAnyArticleAndCommitsItBeforePayload()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "fixture")); var login = home.Store.CreateLoginSession("1", 1, "Fixture"); var actor = home.Store.DownloadActor("1", login.Id);
        await using var fixture = new NntpFixture(); fixture.Articles["one@test"] = Yenc([4, 1, 2, 3], "Fixture.mkv", 1, 0, 4);
        var config = Configuration(); var protection = new EphemeralDataProtectionProvider(); await using var servers = new UsenetService(home.Store, protection, config);
        await servers.SaveAsync(null, new UsenetServerInput("Local", "127.0.0.1", fixture.Port, "plain", "fixture", "private-password", Connections: 1));
        var engine = new NzbEngine(new DownloadFiles(home.Paths), home.Store, servers, new Par2Tool(config, home.Store), new PackageExtractor(), protection, config);
        home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 1)); var record = engine.AddNzb(Nzb("Fixture.mkv", ["one@test"]), principal: actor);
        var denied = home.Store.BeginDownloadBudgetRun(record.Id, record.Revision, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DownloadBudgetException>(() => engine.OpenAsync(home.Store.GetDownloadSource(record.Id) with { Budget = denied }, CancellationToken.None)); Assert.Empty(fixture.Reads);
        home.Store.CloseDownloadBudgetRun(denied, DateTimeOffset.UtcNow); home.Store.SaveDownloadBudget("1", new(MonthlyPayloadBytes: 128, Revision: 1)); record = home.Store.GetDownload(record.Id)!;
        var budget = home.Store.BeginDownloadBudgetRun(record.Id, record.Revision, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await using var session = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id) with { Budget = budget }, CancellationToken.None);
        Assert.Empty(fixture.Reads); Assert.Equal(128, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).ReservedBytes);
        await Finish(session); Assert.True(session.Snapshot.Complete, session.Snapshot.Message); Assert.Equal(128, home.Store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).CommittedBytes);
        Assert.Equal(new byte[] { 4, 1, 2, 3 }, await File.ReadAllBytesAsync(Assert.Single(session.Files).FullPath));
        home.Store.CloseDownloadBudgetRun(budget, DateTimeOffset.UtcNow);
    }
    [Fact]
    public async Task NzbCannotUnderstateItsDecodedPartSize()
    {
        using var home = new TestWorkspace(); await using var fixture = new NntpFixture(); fixture.Articles["one@test"] = Yenc([4, 1, 2, 3], "Fixture.mkv", 1, 0, 4);
        await using var servers = new UsenetService(home.Store, new EphemeralDataProtectionProvider(), Configuration());
        await servers.SaveAsync(null, new UsenetServerInput("Local", "127.0.0.1", fixture.Port, "plain", "fixture", "private-password"));
        var error = await Assert.ThrowsAsync<DomainException>(() => servers.ReadPartAsync(new(1, 2, "one@test"), [], 1024, CancellationToken.None));
        Assert.Contains("boyutu", error.Message); Assert.Empty(home.Store.GetDownloads());
    }

    [Fact]
    public async Task CancellingBlockedBodyKeepsEarlierReceiptsAndReleasesTheConnection()
    {
        using var home = new TestWorkspace(); await using var fixture = new NntpFixture();
        fixture.Articles["one@test"] = Yenc([4, 1], "Fixture.mkv", 1, 0, 4);
        fixture.Articles["two@test"] = Yenc([2, 3], "Fixture.mkv", 2, 2, 4); fixture.StallId = "two@test";
        var config = Configuration(); var protection = new EphemeralDataProtectionProvider();
        await using var servers = new UsenetService(home.Store, protection, config);
        await servers.SaveAsync(null, new UsenetServerInput("Local", "127.0.0.1", fixture.Port, "plain", "fixture", "private-password", Connections: 1));
        var engine = new NzbEngine(new DownloadFiles(home.Paths), home.Store, servers, new Par2Tool(config, home.Store), new PackageExtractor(), protection, config);
        var record = engine.AddNzb(Nzb("Fixture.mkv", ["one@test", "two@test"]));
        await using (var session = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None))
        {
            await session.StartAsync(); await fixture.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.Snapshot.Complete); Assert.Empty(home.Store.GetLibraries());
        }
        fixture.StallId = null;
        await using var resumed = await engine.OpenAsync(home.Store.GetDownloadSource(record.Id), CancellationToken.None);
        await Finish(resumed); Assert.True(resumed.Snapshot.Complete, resumed.Snapshot.Message);
        Assert.Equal(new byte[] { 4, 1, 2, 3 }, await File.ReadAllBytesAsync(Assert.Single(resumed.Files).FullPath));
        Assert.Equal(1, fixture.Reads["one@test"]);
    }

    [Fact]
    public async Task SolidEncryptedRarUsesOrderedExtractionAndRejectsWrongPassword()
    {
        using var home = new TestWorkspace();
        var source = Path.Combine(home.Root, "rar-source"); var output = Path.Combine(home.Root, "rar-output"); Directory.CreateDirectory(source); Directory.CreateDirectory(output);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "archives", "Rar5.solid.encrypted_headers.rar"), Path.Combine(source, "package.rar"));
        await Assert.ThrowsAsync<DomainException>(() => new PackageExtractor().ExtractAsync(source, output, "wrong", 1024, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(output));
        await new PackageExtractor().ExtractAsync(source, output, "test", 1024, CancellationToken.None);
        Assert.Equal(3, Directory.GetFiles(output).Length);
        for (var index = 0; index < 3; index++) Assert.Equal("Solid encrypted header regression test.\n", await File.ReadAllTextAsync(Path.Combine(output, $"f{index:000}.txt")));
    }

    [Fact]
    public async Task ParityFileNamesAreValidatedBeforeTheRepairProgramRuns()
    {
        using var home = new TestWorkspace(); var name = Encoding.UTF8.GetBytes("../outside.mkv");
        var packet = new byte[64 + 56 + ((name.Length + 3) / 4 * 4)];
        "PAR2\0PKT"u8.CopyTo(packet); BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(8), packet.Length);
        "PAR 2.0\0FileDesc"u8.CopyTo(packet.AsSpan(48)); name.CopyTo(packet, 64 + 56);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(64 + 48), 5);
        MD5.HashData(packet.AsSpan(32)).CopyTo(packet, 16);
        var path = Path.Combine(home.Source, "unsafe.par2"); await File.WriteAllBytesAsync(path, packet);
        await Assert.ThrowsAsync<DomainException>(() => new Par2Tool(Configuration(), home.Store).RepairAsync(home.Source, 1024, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(home.Root, "outside.mkv")));
    }

    [Fact]
    public async Task ArchiveExtractionRejectsTraversalAndChecksRealZipPayload()
    {
        using var home = new TestWorkspace();
        var source = Path.Combine(home.Root, "zip-source"); var output = Path.Combine(home.Root, "zip-output"); Directory.CreateDirectory(source); Directory.CreateDirectory(output);
        var file = Path.Combine(source, "package.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            await using var stream = zip.CreateEntry("folder/Fixture.mkv").Open(); await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
        }
        await new PackageExtractor().ExtractAsync(source, output, null, 1024, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(Path.Combine(output, "folder", "Fixture.mkv")));
        File.Delete(file);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) { await using var stream = zip.CreateEntry("../outside.mkv").Open(); await stream.WriteAsync(new byte[] { 1 }); }
        await Assert.ThrowsAsync<DomainException>(() => new PackageExtractor().ExtractAsync(source, output, null, 1024, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(home.Root, "outside.mkv")));
    }

    [Par2Fact]
    public async Task NativeParityRepairsCorruptionAndVerifiesTheWholeFile()
    {
        using var home = new TestWorkspace(); var source = Path.Combine(home.Source, "Fixture.mkv");
        var bytes = RandomNumberGenerator.GetBytes(16 * 1024); await File.WriteAllBytesAsync(source, bytes);
        var executable = Environment.GetEnvironmentVariable("HOME_TEST_PAR2")!;
        await MediaProcess.RunAsync(executable, ["create", "-q", "-s1024", "-c4", "-t2", "--", Path.Combine(home.Source, "recovery.par2"), source], TimeSpan.FromSeconds(20), CancellationToken.None, home.Source);
        var damaged = bytes.ToArray(); Array.Clear(damaged, 1024, 1024); await File.WriteAllBytesAsync(source, damaged);
        var tool = new Par2Tool(Configuration(), home.Store);
        var files = await tool.RepairAsync(home.Source, 1024 * 1024, CancellationToken.None);
        Assert.Equal("Fixture.mkv", Assert.Single(files).Name);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public void TableRebuildPreservesDownloadHashesAndForeignKeys()
    {
        using var home = new TestWorkspace();
        var other = Path.Combine(home.Root, "schema11"); Directory.CreateDirectory(other);
        var paths = new HomePaths(other, Path.Combine(other, "thumbnails"), null);
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(other, "home.db"), ForeignKeys = true, Pooling = false }.ToString();
        using (var db = new SqliteConnection(cs))
        {
            db.Open();
            foreach (var resource in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains("Migrations.")).Order().Take(11))
            {
                using var reader = new StreamReader(typeof(MediaStore).Assembly.GetManifestResourceStream(resource)!);
                using var command = db.CreateCommand(); command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            }
            using var data = db.CreateCommand(); data.CommandText = """
                PRAGMA user_version=11;
                INSERT INTO downloads(id,info_hash,name,source_kind,source,desired_state,status,created_at,updated_at) VALUES('old','hash','Original','torrent','secret','paused','paused','2026','2026');
                INSERT INTO download_hashes(hash,download_id) VALUES('hash','old');
                """; data.ExecuteNonQuery();
        }
        var store = new MediaStore(paths); store.Initialize();
        Assert.Equal("old", store.AddDownload(["hash"], "Another", "nzb", "unused", 1).Id);
        using var check = new SqliteConnection(cs); check.Open(); using var verify = check.CreateCommand(); verify.CommandText = "PRAGMA foreign_key_check";
        using var rows = verify.ExecuteReader(); Assert.False(rows.Read());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Home:Downloads:LoopbackOnly"] = "true", ["Home:Par2Path"] = Environment.GetEnvironmentVariable("HOME_TEST_PAR2") }).Build();
    private static UsenetServer Server(int port, string security) => new("fixture", "Fixture", "127.0.0.1", port, security, "fixture", true, 1, 1, true, 1, null, null, null, 0);
    private static byte[] Nzb(string name, string[] ids) => Encoding.UTF8.GetBytes(new XDocument(new XElement("nzb",
        new XElement("file", new XAttribute("subject", $"\"{name}\" yEnc"), new XElement("groups", new XElement("group", "alt.test")),
            new XElement("segments", ids.Select((id, index) => new XElement("segment", new XAttribute("number", index + 1), new XAttribute("bytes", 128), id)))))).ToString());
    internal static byte[][] Yenc(byte[] bytes, string name, int number, int offset, int total)
    {
        var lines = new List<byte[]> { Encoding.UTF8.GetBytes($"=ybegin part={number} line=64 size={total} name={name}"), Encoding.ASCII.GetBytes($"=ypart begin={offset + 1} end={offset + bytes.Length}") };
        foreach (var chunk in bytes.Chunk(64))
        {
            var encoded = new List<byte>();
            foreach (var value in chunk)
            {
                var next = unchecked((byte)(value + 42));
                if (next is 0 or 10 or 13 or 61) { encoded.Add((byte)'='); next = unchecked((byte)(next + 64)); }
                encoded.Add(next);
            }
            lines.Add(encoded.ToArray());
        }
        lines.Add(Encoding.ASCII.GetBytes($"=yend size={bytes.Length} part={number} pcrc32={Crc32.Hash(bytes):x8}"));
        return lines.ToArray();
    }
    private static async Task Finish(ITorrentSession session)
    {
        await session.StartAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!session.Snapshot.Complete && session.Snapshot.Status is not ("failed" or "paused")) await Task.Delay(25, timeout.Token);
    }

    private sealed class NntpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _connections = [];
        private readonly Task _accept;
        private readonly string _security;
        private readonly X509Certificate2? _certificate;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public ConcurrentDictionary<string, byte[][]> Articles { get; } = new();
        public ConcurrentDictionary<string, int> Reads { get; } = new();
        public string? StallId { get; set; }
        public TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _authentications;
        public int Authentications => _authentications;
        public NntpFixture(string security = "plain", X509Certificate2? certificate = null)
        { _security = security; _certificate = certificate; _listener.Start(); _accept = AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _connections.Add(Handle(client)); } }
            catch (OperationCanceledException) { }
        }
        private async Task Handle(TcpClient client)
        {
            using (client)
            {
                Stream stream = client.GetStream();
                try
                {
                    if (_security == "tls") stream = await Secure(stream);
                    await Send("200 NNTP fixture ready");
                    while (!_stop.IsCancellationRequested)
                    {
                        var command = await ReadLine(stream, _stop.Token); if (command is null) break;
                        if (command == "STARTTLS") { await Send("382 Continue"); stream = await Secure(stream); }
                        else if (command.StartsWith("AUTHINFO USER ", StringComparison.Ordinal)) await Send("381 Password required");
                        else if (command == "AUTHINFO PASS private-password") { Interlocked.Increment(ref _authentications); await Send("281 Accepted"); }
                        else if (command == "CAPABILITIES") { await Send("101 Capabilities"); await Send("VERSION 2"); await Send("READER"); await Send("."); }
                        else if (command.StartsWith("BODY <", StringComparison.Ordinal))
                        {
                            var id = command[6..^1]; Reads.AddOrUpdate(id, 1, (_, count) => count + 1);
                            if (id == StallId) { Stalled.TrySetResult(); await Task.Delay(Timeout.Infinite, _stop.Token); }
                            if (!Articles.TryGetValue(id, out var lines)) { await Send("430 Missing"); continue; }
                            await Send("222 0 <" + id + "> article");
                            foreach (var line in lines)
                            {
                                if (line.Length > 0 && line[0] == '.') await stream.WriteAsync(new byte[] { (byte)'.' }, _stop.Token);
                                await stream.WriteAsync(line, _stop.Token); await stream.WriteAsync("\r\n"u8.ToArray(), _stop.Token);
                            }
                            await Send(".");
                        }
                        else await Send("500 Unsupported");
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or AuthenticationException) { }
                finally { await stream.DisposeAsync(); }
                async Task Send(string line) { await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), _stop.Token); await stream.FlushAsync(_stop.Token); }
            }
        }
        private async Task<Stream> Secure(Stream stream)
        {
            var ssl = new SslStream(stream); await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token); return ssl;
        }
        private static async Task<string?> ReadLine(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>(); var next = new byte[1];
            while (await stream.ReadAsync(next, token) > 0) { if (next[0] == '\n') return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r'); bytes.Add(next[0]); }
            return null;
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _accept; await Task.WhenAll(_connections); _stop.Dispose(); }
    }
}
public sealed class Par2FactAttribute : FactAttribute
{
    public Par2FactAttribute() { if (!File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_PAR2"))) Skip = "Set HOME_TEST_PAR2 to the PAR2 verification executable."; }
}
