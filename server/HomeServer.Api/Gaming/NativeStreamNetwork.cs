using System.Net;
using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>Only encrypted ICE media leaves the host; WHEP and engine controls stay behind Home.</summary>
public sealed class NativeStreamNetwork(IConfiguration configuration)
{
    public string[] Hosts(string? requestedHost)
    {
        var configured=configuration["Home:Gaming:PublicHost"];
        var host=string.IsNullOrWhiteSpace(configured)?requestedHost:configured;
        if(string.IsNullOrWhiteSpace(host))return ["127.0.0.1"];
        host=host.Trim().Trim('[',']');
        if(host.Length>253||host.Any(char.IsControl)||host.Contains('%')||Uri.CheckHostName(host)==UriHostNameType.Unknown)
            throw new DomainException("Oyun yayını için geçerli sunucu IP adresi veya adı gerekli.",400);
        if(IPAddress.TryParse(host,out var ip)&&ip.Equals(IPAddress.Any)||host=="::")
            throw new DomainException("Oyun yayınına bağlanılacak sunucu IP adresini gir.",400);
        return host is "localhost" or "127.0.0.1" or "::1"?["127.0.0.1"]:[host,"127.0.0.1"];
    }
}
