namespace HomeServer.Core;

public sealed record NetworkPreferences(string PathBase = "", string? PublicUrl = null, string[]? TrustedProxies = null,
    bool RequireHttps = false, string BindAddress = "127.0.0.1", int? HttpPort = null, int? HttpsPort = null);
public sealed record CertificateInput(string Base64, string Password);
