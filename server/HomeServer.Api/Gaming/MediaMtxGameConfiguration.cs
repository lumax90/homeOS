using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>One private game source with encrypted ICE media for LAN clients.</summary>
public static class MediaMtxGameConfiguration
{
    public static string Create(string path, string password, int sourcePort, int httpPort, int apiPort, int mediaPort, string[]? hosts=null)
    {
        if (path.Length is < 1 or > 64 || path.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || password.Length is < 32 or > 128 || password.Any(c => !char.IsAsciiHexDigit(c))) throw new DomainException("Yerel oyun yayın kimliği geçersiz.");
        var ports = new[] { sourcePort, httpPort, apiPort, mediaPort };
        if (ports.Distinct().Count() != 4 || ports.Any(port => port is < 1024 or > 65535)) throw new DomainException("Yerel oyun yayın portları geçersiz.");
        hosts??=["127.0.0.1"];
        if(hosts.Length is <1 or >8||hosts.Any(host=>host.Length>253||host.Any(char.IsControl)||host.Contains('%')||Uri.CheckHostName(host)==UriHostNameType.Unknown))throw new DomainException("Oyun yayın adresi geçersiz.");
        var lan=hosts.Any(host=>!IPAddressLoopback(host));
        // JSON is a supported subset of YAML. Avoid hand-interpolated configuration/command injection.
        return JsonSerializer.Serialize(new
        {
            logLevel = "warn", logDestinations = new[] { "stdout" }, logStructured = true,
            readTimeout = "10m", writeTimeout = "10s", writeQueueSize = 64,
            api = true, apiAddress = $"127.0.0.1:{apiPort}", apiAllowOrigins = Array.Empty<string>(),
            metrics = false, pprof = false, playback = false, rtsp = false, rtmp = false, hls = false, srt = false, moq = false,
            webrtc = true, webrtcAddress = $"127.0.0.1:{httpPort}", webrtcAllowOrigins = Array.Empty<string>(),
            webrtcLocalUDPAddress = $"{(lan?"0.0.0.0":"127.0.0.1")}:{mediaPort}", webrtcLocalTCPAddress = lan?$"0.0.0.0:{mediaPort}":"",
            webrtcIPsFromInterfaces = false, webrtcAdditionalHosts = hosts, webrtcICEServers2 = Array.Empty<object>(),
            authMethod = "internal", authInternalUsers = new[] { new { user = "home-native", pass = password, ips = new[] { "127.0.0.1" }, permissions = new object[] { new { action = "read", path }, new { action = "api" } } } },
            paths = new Dictionary<string, object> { [path] = new { source = $"udp+mpegts://127.0.0.1:{sourcePort}", sourceOnDemand = false } }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
    private static bool IPAddressLoopback(string host)=>System.Net.IPAddress.TryParse(host,out var ip)&&System.Net.IPAddress.IsLoopback(ip);
}
