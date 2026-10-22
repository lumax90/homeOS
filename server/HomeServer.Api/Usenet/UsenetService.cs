using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using HomeServer.Downloads;

namespace HomeServer.Usenet;

public sealed class UsenetService(MediaStore store, IDataProtectionProvider protection, IConfiguration configuration, TorrentEngine? torrents = null) : IAsyncDisposable
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Usenet.Servers.v1");
    private readonly ConcurrentDictionary<string, Pool> _pools = new();
    private readonly bool _loopbackOnly = configuration.GetValue<bool>("Home:Downloads:LoopbackOnly");
    private readonly SemaphoreSlim _bandwidth = new(1, 1);
    private DateTimeOffset _nextBytes;
    private int _lastByteLimit;
    private async Task ReserveBytesAsync(int bytes, CancellationToken token)
    {
        await _bandwidth.WaitAsync(token);
        try
        {
            while (true)
            {
                var limit = store.GetTransferDefaults(configuration).DownloadKiB;
                if (limit != _lastByteLimit) { _nextBytes = DateTimeOffset.UtcNow; _lastByteLimit = limit; }
                if (limit == 0) { _nextBytes = DateTimeOffset.UtcNow; return; }
                var wait = _nextBytes - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) { await Task.Delay(wait < TimeSpan.FromMilliseconds(250) ? wait : TimeSpan.FromMilliseconds(250), token); continue; }
                var rate = limit * 1024d / (torrents?.HasOpenSessions == true ? 2 : 1);
                _nextBytes = DateTimeOffset.UtcNow.AddSeconds(bytes / rate); return;
            }
        }
        finally { _bandwidth.Release(); }
    }
    private sealed class Pool(UsenetServer server)
    {
        public int Revision { get; } = server.Revision;
        public SemaphoreSlim Slots { get; } = new(server.Connections, server.Connections);
        public ConcurrentBag<NntpConnection> Idle { get; } = [];
        public CancellationTokenSource Lifetime { get; } = new();
        public async Task CloseAsync()
        {
            Lifetime.Cancel(); while (Idle.TryTake(out var connection)) await connection.DisposeAsync();
        }
    }
    public IReadOnlyList<UsenetServer> List() => store.GetUsenetServers().Select(secret => secret.Server with { Configured = secret.Server.Username.Length == 0 || secret.ProtectedPassword.Length > 0 }).ToArray();
    public async Task<UsenetServer> SaveAsync(string? id, UsenetServerInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || string.IsNullOrWhiteSpace(input.Host) || input.Host.Length > 253
            || Uri.CheckHostName(input.Host) == UriHostNameType.Unknown || input.Host.Contains('%') || input.Port is < 1 or > 65535 || input.Security is not ("tls" or "starttls" or "plain")
            || input.Connections is < 1 or > 20 || input.Priority is < 1 or > 50 || input.Username is null || input.Username.Any(char.IsControl) || Encoding.UTF8.GetByteCount(input.Username) > 400
            || input.Password?.Any(char.IsControl) == true || input.Password is not null && Encoding.UTF8.GetByteCount(input.Password) > 400)
            throw new DomainException("NNTP sunucu adı, adresi, TLS veya bağlantı ayarları geçersiz.");
        if (input.Security == "plain" && (!IPAddress.TryParse(input.Host, out var address) || !IPAddress.IsLoopback(address)))
            throw new DomainException("İnternet NNTP bağlantısı için TLS veya STARTTLS seç.");
        var existing = id is null ? null : store.GetUsenetServers().FirstOrDefault(server => server.Server.Id == id) ?? throw new DomainException("NNTP sunucusu bulunamadı.", 404);
        if (id is null && List().Count >= 10) throw new DomainException("En fazla 10 NNTP sunucusu bağlanabilir.");
        var secret = input.Password is null && existing is not null ? existing.ProtectedPassword : input.Password is { Length: > 0 } ? _protector.Protect(input.Password) : "";
        var result = store.SaveUsenetServer(id, input, secret);
        if (_pools.TryRemove(result.Id, out var pool)) await pool.CloseAsync();
        return result;
    }
    public async Task<object> TestAsync(string id, CancellationToken token)
    {
        var server = store.GetUsenetServers().FirstOrDefault(server => server.Server.Id == id) ?? throw new DomainException("NNTP sunucusu bulunamadı.", 404);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await using var connection = await NntpConnection.ConnectAsync(server.Server, Password(server), _loopbackOnly, timeout.Token);
            var capabilities = await connection.CapabilitiesAsync(timeout.Token);
            store.SetUsenetHealth(id, server.Server.Revision, null);
            return new { message = "NNTP bağlantısı ve hesap doğrulandı.", capabilities, security = server.Server.Security };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException)
        {
            var message = Message(ex); store.SetUsenetHealth(id, server.Server.Revision, message, 30); throw new DomainException(message, 502);
        }
    }
    public async Task<YencPart?> ReadPartAsync(NzbSegment segment, string[] groups, long maxSize, CancellationToken token)
    {
        var servers = store.GetUsenetServers().Where(server => server.Server.Enabled).ToArray();
        if (servers.Length == 0) throw new DomainException("Etkin bir Usenet/NNTP sunucusu bağla.", 409);
        string? lastError = null; var missing = false;
        foreach (var entry in servers)
        {
            var server = entry.Server;
            if (DateTimeOffset.TryParse(server.RetryAt, out var retry) && retry > DateTimeOffset.UtcNow) { lastError = server.LastError ?? "NNTP sunucusu yeniden bağlantı bekliyor."; continue; }
            var pool = _pools.GetOrAdd(server.Id, _ => new Pool(server));
            if (pool.Revision != server.Revision) { if (_pools.TryRemove(server.Id, out var old)) await old.CloseAsync(); pool = _pools.GetOrAdd(server.Id, _ => new Pool(server)); }
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, pool.Lifetime.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
            var entered = false; NntpConnection? connection = null; var healthy = false;
            try
            {
                await pool.Slots.WaitAsync(operation.Token); entered = true; timeout.CancelAfter(TimeSpan.FromSeconds(45));
                if (!pool.Idle.TryTake(out connection)) connection = await NntpConnection.ConnectAsync(server, Password(entry), _loopbackOnly, timeout.Token, throttle: ReserveBytesAsync);
                var body = await connection.BodyAsync(segment.MessageId, groups, operation.Token, (int)Math.Min(34 * 1024 * 1024, segment.Bytes * 2 + 65536));
                healthy = true;
                var part = YencDecoder.Decode(body, maxSize);
                if (part.Number != segment.Number) throw new DomainException("yEnc parça numarası NZB ile eşleşmiyor.");
                if (part.Bytes.Length > segment.Bytes) throw new DomainException("yEnc parçası NZB'de bildirilen boyutu aşıyor; paket bütçesi doğrulanamadı.");
                store.SetUsenetHealth(server.Id, server.Revision, null, bytes: part.Bytes.Length);
                return part;
            }
            catch (NntpException ex) when (ex.Code is 430 or 423) { healthy = true; missing = true; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or DomainException)
            {
                lastError = Message(ex);
                store.SetUsenetHealth(server.Id, server.Revision, lastError, ex is NntpException { Code: 481 or 482 or 502 } ? 3600 : 30);
            }
            finally
            {
                if (connection is not null)
                {
                    if (healthy && !pool.Lifetime.IsCancellationRequested) pool.Idle.Add(connection); else await connection.DisposeAsync();
                }
                if (entered) pool.Slots.Release();
            }
        }
        if (!missing && lastError is not null) throw new DomainException(lastError, 502);
        return null;
    }
    private string Password(UsenetServerSecret server)
    {
        if (server.ProtectedPassword.Length == 0) return "";
        try { return _protector.Unprotect(server.ProtectedPassword); }
        catch (CryptographicException) { throw new DomainException("NNTP parolası okunamadı; sunucu parolasını yeniden kaydet.", 409); }
    }
    private static string Message(Exception ex) => ex switch
    {
        AuthenticationException => "NNTP TLS sertifikası veya güvenli bağlantısı doğrulanamadı.",
        NntpException => ex.Message,
        DomainException => ex.Message,
        OperationCanceledException => "NNTP bağlantısı zaman aşımına uğradı veya ayarlar değişti.",
        _ => "NNTP sunucusuna bağlanılamadı veya bağlantı kesildi."
    };
    public async ValueTask DisposeAsync() { foreach (var pool in _pools.Values) await pool.CloseAsync(); _pools.Clear(); }
}
