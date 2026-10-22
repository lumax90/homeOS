using System.Net;
using System.Net.Sockets;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public interface IGamePackageSource { Task<GameSourceResponse> OpenAsync(GameStoreDefinition game, CancellationToken token); }
public sealed class GameSourceResponse(Stream stream, long? length, IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public long? Length { get; } = length;
    public async ValueTask DisposeAsync() { await Stream.DisposeAsync(); owner?.Dispose(); }
}
public sealed class GamePackageSource : IGamePackageSource, IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = async (context, token) =>
        {
            if (context.DnsEndPoint.Host != "codeload.github.com" || context.DnsEndPoint.Port != 443) throw new HttpRequestException("Unapproved game package host.");
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            foreach (var address in addresses.Where(address => RemoteMediaHttp.Allowed(address, false)))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), token); return new NetworkStream(socket, true); } catch { socket.Dispose(); }
            }
            throw new HttpRequestException("The game package host did not resolve to a permitted public address.");
        }
    }) { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<GameSourceResponse> OpenAsync(GameStoreDefinition game, CancellationToken token)
    {
        if (game.ArchiveUrl != $"https://codeload.github.com/{game.Repository}/zip/{game.Commit}") throw new DomainException("Oyun indirme kaynağı geçersiz.");
        using var request = new HttpRequestMessage(HttpMethod.Get, game.ArchiveUrl); request.Headers.UserAgent.ParseAdd("Home/" + BuildInfo.Version);
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try
        {
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 128 * 1024 * 1024) throw new DomainException("Oyun indirme kaynağı uygun yanıt vermedi.", 502);
            return new(await response.Content.ReadAsStreamAsync(token), response.Content.Headers.ContentLength, response);
        }
        catch { response.Dispose(); throw; }
    }
    public void Dispose() => _client.Dispose();
}
