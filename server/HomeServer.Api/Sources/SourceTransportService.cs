using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Sources;

public sealed class SourceTransportService(MediaStore store, IHttpClientFactory clients, IDataProtectionProvider protection) : IDisposable
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Source.Network.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class ProxyClient(int revision, HttpClient client)
    {
        public int Revision { get; } = revision;
        public HttpClient Client { get; } = client;
        public int Readers;
        public bool Retired;
    }
    private readonly Dictionary<string, ProxyClient> _proxyClients = new();
    private readonly object _clientGate = new();
    private bool _disposed;
    public SourceTransport Read(string id)
    {
        _ = store.GetIndexer(id); var row = store.GetSourceTransport(id);
        try { return new(row.Adapter, row.Settings is null ? new() : JsonSerializer.Deserialize<SourceNetworkSettings>(_protector.Unprotect(row.Settings), Json)!, row.Revision); }
        catch (CryptographicException) { throw new DomainException("Kaynak bağlantı sırları okunamadı; yeniden kaydet.", 409); }
    }
    public object Public(string id)
    {
        var value = Read(id); return new { value.AdapterId, value.Revision, settings = new { value.Settings.ProxyUrl, value.Settings.ProxyUsername, value.Settings.DailyQueries, value.Settings.DailyGrabs },
            proxyPasswordConfigured = !string.IsNullOrEmpty(value.Settings.ProxyPassword), cookieConfigured = !string.IsNullOrEmpty(value.Settings.Cookie), headerNames = value.Settings.Headers?.Keys.ToArray() ?? [] };
    }
    public void Save(string id, SourceTransportInput input)
    {
        store.RequirePublishedIndexer(id);
        if (input.Settings is null) throw new DomainException("Kaynak ağı ayarları gerekli.");
        var old = Read(id); var settings = input.Settings with
        {
            ProxyPassword = input.ClearProxyPassword ? null : input.Settings.ProxyPassword ?? old.Settings.ProxyPassword,
            Cookie = input.ClearCookie ? null : input.Settings.Cookie ?? old.Settings.Cookie,
            Headers = input.ClearHeaders ? null : input.Settings.Headers ?? old.Settings.Headers
        };
        var protectedSettings=ProtectSettings(settings);
        if (input.AdapterId is not null)
        {
            var manifest = SourceAdapterRegistry.Find(input.AdapterId, store);
            if ((store.GetIndexer(id).Info.Protocol == "newznab") != (manifest.Protocol == "usenet")) throw new DomainException("Adaptörün torrent/Usenet ailesi kaynak protokolüyle eşleşmeli.");
        }
        store.SaveSourceTransport(id, input.AdapterId, protectedSettings, input.Revision);
    }
    public string ProtectSettings(SourceNetworkSettings settings)
    {
        if (settings.ProxyUrl is { Length: > 0 } url && (url.Length>2048||!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "socks4" or "socks5") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")) throw new DomainException("HTTP/SOCKS proxy adresi geçersiz; kimlik bilgilerini ayrı gir.");
        if (settings.DailyQueries is < 1 or > 100000 || settings.DailyGrabs is < 1 or > 10000 || settings.Cookie?.Length > 8192 || settings.ProxyUsername?.Length > 200 || settings.ProxyPassword?.Length > 200 || settings.Headers?.Count > 12 || settings.Cookie?.Any(char.IsControl) == true) throw new DomainException("Kaynak ağı ayarları sınırları geçersiz.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in settings.Headers ?? [])
            if (!names.Add(header.Key) || !(header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) || header.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || header.Value is null || header.Value.Length > 4096 || header.Value.Any(char.IsControl)) throw new DomainException("Kaynak HTTP başlığı geçersiz.");
        return _protector.Protect(JsonSerializer.Serialize(settings, Json));
    }
    private (HttpClient Client, Action Release) Acquire(string id, SourceTransport options)
    {
        lock (_clientGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(options.Settings.ProxyUrl))
            {
                if (_proxyClients.Remove(id, out var previous)) Retire(previous);
                var client = clients.CreateClient("indexer"); return (client, client.Dispose);
            }
            if (!_proxyClients.TryGetValue(id, out var current) || current.Revision != options.Revision)
            {
                var client = new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    Proxy = new WebProxy(options.Settings.ProxyUrl) { Credentials = string.IsNullOrEmpty(options.Settings.ProxyUsername) ? null : new NetworkCredential(options.Settings.ProxyUsername, options.Settings.ProxyPassword) },
                    UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5), MaxConnectionsPerServer = 4
                }) { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/" + BuildInfo.Version);
                var next = new ProxyClient(options.Revision, client);
                if (current is not null && current.Revision > options.Revision) next.Retired = true;
                else { if (current is not null) Retire(current); _proxyClients[id] = next; }
                current = next;
            }
            current.Readers++;
            var borrowed = current; var returned = 0;
            return (current.Client, () =>
            {
                if (Interlocked.Exchange(ref returned, 1) != 0) return;
                lock (_clientGate) { borrowed.Readers--; if (borrowed.Retired && borrowed.Readers == 0) borrowed.Client.Dispose(); }
            });
        }
    }
    private static void Retire(ProxyClient client) { client.Retired = true; if (client.Readers == 0) client.Client.Dispose(); }
    public void PruneRuntime(IReadOnlySet<string> ids)
    {
        lock(_clientGate)foreach(var id in _proxyClients.Keys.Where(id=>!ids.Contains(id)).ToArray())if(_proxyClients.Remove(id,out var client))Retire(client);
    }
    public async Task<HttpResponseMessage> SendAsync(IndexerSecret source, string address, string action, CancellationToken token, bool credentials = true, Dictionary<string, string>? headers = null)
    {
        var uri = new Uri(address);
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) throw new DomainException("Kaynak HTTP adresi geçersiz.");
        token.ThrowIfCancellationRequested(); var options = Read(source.Info.Id); store.RequireIndexerSnapshot(source.Info); store.ReserveSourceRequest(source.Info.Id, action, options.Settings);
        var started = Stopwatch.GetTimestamp(); var borrowed = Acquire(source.Info.Id, options); var handedOff = false;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (credentials)
        {
            if (!string.IsNullOrEmpty(options.Settings.Cookie)) request.Headers.TryAddWithoutValidation("Cookie", options.Settings.Cookie);
            foreach (var header in options.Settings.Headers ?? []) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var header in headers ?? []) { request.Headers.Remove(header.Key); request.Headers.TryAddWithoutValidation(header.Key, header.Value); }
        }
        HttpResponseMessage? response = null;
        try
        {
            response = await borrowed.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            store.RecordSourceRequest(source.Info.Id, action, uri.Host, (int)response.StatusCode, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, response.Content.Headers.ContentLength);
            response.Content = new BorrowedContent(response.Content, borrowed.Release); handedOff = true; return response;
        }
        catch
        {
            response?.Dispose();
            if (response is null) store.RecordSourceRequest(source.Info.Id, action, uri.Host, 0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, null);
            throw;
        }
        finally { if (!handedOff) borrowed.Release(); }
    }
    private sealed class BorrowedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly Action _release;
        private int _disposed;
        public BorrowedContent(HttpContent inner, Action release)
        {
            _inner = inner; _release = release;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _inner.CopyToAsync(stream);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => _inner.CopyToAsync(stream, token);
        protected override Task<Stream> CreateContentReadStreamAsync() => _inner.ReadAsStreamAsync();
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => _inner.ReadAsStreamAsync(token);
        protected override bool TryComputeLength(out long length) { length = _inner.Headers.ContentLength ?? 0; return _inner.Headers.ContentLength is not null; }
        protected override void Dispose(bool disposing)
        {
            try { if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) { try { _inner.Dispose(); } finally { _release(); } } }
            finally { base.Dispose(disposing); }
        }
    }
    public void Dispose()
    {
        lock (_clientGate) { if (_disposed) return; _disposed = true; foreach (var entry in _proxyClients.Values) Retire(entry); _proxyClients.Clear(); }
    }
}
