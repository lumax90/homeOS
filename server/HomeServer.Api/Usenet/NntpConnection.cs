using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HomeServer.Core;

namespace HomeServer.Usenet;

public sealed class NntpException(int code, string message) : IOException(message)
{
    public int Code { get; } = code;
}

public sealed class NntpConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private Stream _stream;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly Func<int, CancellationToken, Task>? _throttle;
    private bool _readingBody;
    private int _position, _count;
    private NntpConnection(TcpClient client, Func<int, CancellationToken, Task>? throttle) { _client = client; _stream = client.GetStream(); _throttle = throttle; }

    public static async Task<NntpConnection> ConnectAsync(UsenetServer server, string password, bool loopbackOnly,
        CancellationToken token, X509Certificate2? trustedRoot = null, Func<int, CancellationToken, Task>? throttle = null)
    {
        var addresses = await Dns.GetHostAddressesAsync(server.Host, token);
        if (addresses.Length == 0) throw new NntpException(400, "NNTP sunucusunun adresi çözülemedi.");
        if ((loopbackOnly || server.Security == "plain") && addresses.Any(address => !IPAddress.IsLoopback(address)))
            throw new NntpException(483, "Bu bağlantı için TLS/STARTTLS gerekli; yerel test sınırı uygulanıyor.");
        var client = new TcpClient { NoDelay = true };
        NntpConnection? connection = null;
        try
        {
            await client.ConnectAsync(addresses, server.Port, token);
            connection = new NntpConnection(client, throttle);
            if (server.Security == "tls") await connection.UpgradeAsync(server.Host, trustedRoot, token);
            var greeting = await connection.ResponseAsync(token);
            if (greeting is not (200 or 201)) throw new NntpException(greeting, "NNTP sunucusu bağlantıyı kabul etmedi.");
            if (server.Security == "starttls")
            {
                if (await connection.CommandAsync("STARTTLS", token) != 382) throw new NntpException(483, "NNTP sunucusu STARTTLS bağlantısını kabul etmedi.");
                await connection.UpgradeAsync(server.Host, trustedRoot, token);
            }
            if (server.Username.Length > 0)
            {
                var code = await connection.CommandAsync("AUTHINFO USER " + server.Username, token);
                if (code == 381) code = await connection.CommandAsync("AUTHINFO PASS " + password, token);
                if (code != 281) throw new NntpException(code, "NNTP kullanıcı adı/parolası veya hesap erişimi reddedildi.");
            }
            return connection;
        }
        catch
        {
            if (connection is not null) await connection.DisposeAsync(); else client.Dispose();
            throw;
        }
    }
    private async Task UpgradeAsync(string host, X509Certificate2? trustedRoot, CancellationToken token)
    {
        if (_position != _count) throw new NntpException(483, "STARTTLS öncesi beklenmeyen veri alındı.");
        var ssl = new SslStream(_stream, leaveInnerStreamOpen: false);
        var options = new SslClientAuthenticationOptions { TargetHost = host, EnabledSslProtocols = SslProtocols.None };
        if (trustedRoot is not null)
        {
            options.CertificateChainPolicy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
            options.CertificateChainPolicy.CustomTrustStore.Add(trustedRoot);
        }
        _stream = ssl;
        await ssl.AuthenticateAsClientAsync(options, token);
        _position = _count = 0;
    }
    public async Task<byte[][]> BodyAsync(string messageId, string[] groups, CancellationToken token, int maximumBodyBytes = 34 * 1024 * 1024)
    {
        NzbParser.ValidateMessageId(messageId);
        var code = await CommandAsync("BODY <" + messageId + ">", token);
        if (code == 412)
        {
            foreach (var group in groups)
            {
                if (await CommandAsync("GROUP " + group, token) != 211) continue;
                code = await CommandAsync("BODY <" + messageId + ">", token);
                if (code != 430) break;
            }
        }
        if (code is 430 or 423) throw new NntpException(code, "Usenet parçası bu sunucuda bulunamadı.");
        if (code != 222) throw new NntpException(code, "NNTP sunucusu parçayı gönderemedi.");
        var lines = new List<byte[]>(); var total = 0;
        _readingBody = true;
        try
        {
        if (_throttle is not null && _count > _position) await _throttle(_count - _position, token);
        while (true)
        {
            var line = await LineAsync(32768, token);
            if (line.Length == 1 && line[0] == '.') break;
            if (line.Length > 0 && line[0] == '.') line = line[1..];
            total += line.Length;
            if (total > Math.Min(34 * 1024 * 1024, maximumBodyBytes) || lines.Count >= 300000) throw new NntpException(502, "NNTP parçası boyut sınırını aşıyor.");
            lines.Add(line);
        }
        return lines.ToArray();
        }
        finally { _readingBody = false; }
    }
    public async Task<string[]> CapabilitiesAsync(CancellationToken token)
    {
        var code = await CommandAsync("CAPABILITIES", token);
        if (code is 500 or 501) return [];
        if (code != 101) throw new NntpException(code, "NNTP yetenek bilgisi alınamadı.");
        var result = new List<string>();
        for (var index = 0; index < 100; index++)
        {
            var line = Encoding.UTF8.GetString(await LineAsync(8192, token));
            if (line == ".") return result.ToArray();
            result.Add(line.TrimStart('.'));
        }
        throw new NntpException(502, "NNTP yetenek yanıtı sınırı aşıyor.");
    }
    private async Task<int> CommandAsync(string command, CancellationToken token)
    {
        if (command.Any(c => c is '\r' or '\n' or '\0') || Encoding.UTF8.GetByteCount(command) > 510) throw new DomainException("NNTP komutu geçersiz.");
        await _stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r\n"), token);
        await _stream.FlushAsync(token);
        return await ResponseAsync(token);
    }
    private async Task<int> ResponseAsync(CancellationToken token)
    {
        var line = await LineAsync(8192, token);
        if (line.Length < 3 || line.Take(3).Any(value => value < '0' || value > '9')) throw new NntpException(502, "NNTP yanıt kodu okunamadı.");
        return (line[0] - '0') * 100 + (line[1] - '0') * 10 + line[2] - '0';
    }
    private async Task<byte[]> LineAsync(int limit, CancellationToken token)
    {
        using var result = new MemoryStream();
        while (true)
        {
            if (_position == _count)
            {
                using var stalled = CancellationTokenSource.CreateLinkedTokenSource(token); stalled.CancelAfter(TimeSpan.FromSeconds(30));
                _count = await _stream.ReadAsync(_buffer, stalled.Token); _position = 0;
                if (_count == 0) throw new NntpException(400, "NNTP bağlantısı yanıt tamamlanmadan kapandı.");
                if (_readingBody && _throttle is not null) await _throttle(_count, token);
            }
            var newline = Array.IndexOf(_buffer, (byte)'\n', _position, _count - _position);
            var end = newline < 0 ? _count : newline;
            result.Write(_buffer, _position, end - _position); _position = end + (newline < 0 ? 0 : 1);
            if (result.Length > limit) throw new NntpException(502, "NNTP satırı izin verilen sınırı aşıyor.");
            if (newline < 0) continue;
            var line = result.ToArray();
            if (line.Length == 0 || line[^1] != '\r') throw new NntpException(502, "NNTP CRLF satır sonu geçersiz.");
            return line[..^1];
        }
    }
    public async ValueTask DisposeAsync() { try { await _stream.DisposeAsync(); } finally { _client.Dispose(); } }
}
