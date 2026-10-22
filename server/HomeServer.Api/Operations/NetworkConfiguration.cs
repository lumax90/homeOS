using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HomeServer.Operations;

public sealed partial class NetworkConfiguration(HomePaths paths, IDataProtectionProvider protection, IConfiguration configuration)
{
    public NetworkPreferences Current { get; } = Validate(configuration.GetSection("Home:Network").Get<NetworkPreferences>() ?? new());
    public bool HasCertificate => File.Exists(Path.Combine(paths.State, "https-certificate.protected"));
    public NetworkPreferences? Pending
    {
        get
        {
            var file = Path.Combine(paths.Data, "home-settings.json"); if (!File.Exists(file)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(MediaPaths.ResolveFile(paths.Data, "home-settings.json")));
            return json.RootElement.GetProperty("Home").GetProperty("Network").Deserialize<NetworkPreferences>();
        }
    }
    public static NetworkPreferences Validate(NetworkPreferences input)
    {
        if (input.PathBase is null || input.PathBase.Length > 120 || !BasePath().IsMatch(input.PathBase)
            || !IPAddress.TryParse(input.BindAddress, out _) || input.HttpPort is < 1 or > 65535 || input.HttpsPort is < 1 or > 65535 || input.HttpPort is not null && input.HttpPort == input.HttpsPort)
            throw new DomainException("Ağ adresi, port veya alt yol geçersiz.");
        if (input.TrustedProxies?.Length > 10 || input.TrustedProxies?.Any(value => !IPAddress.TryParse(value, out _)) == true) throw new DomainException("Proxy adreslerini tekil IP olarak gir.");
        if (!string.IsNullOrEmpty(input.PublicUrl) && (!Uri.TryCreate(input.PublicUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 || url.AbsolutePath.TrimEnd('/') != input.PathBase))
            throw new DomainException("Genel adres http(s)://sunucu/alt-yol biçiminde ve seçilen alt yolla aynı olmalı.");
        return input;
    }
    public void Save(NetworkPreferences input)
    {
        input = Validate(input);
        if (input.HttpsPort is not null && !HasCertificate) throw new DomainException("HTTPS portunu açmadan önce TLS sertifikasını yükle.");
        if (input.RequireHttps && input.HttpsPort is null && !(input.PublicUrl?.StartsWith("https://", StringComparison.Ordinal) == true && input.TrustedProxies is { Length: > 0 }))
            throw new DomainException("HTTPS zorunluluğu için TLS portu veya güvenilen HTTPS proxy yapılandır.");
        var file = Path.Combine(paths.Data, "home-settings.json"); if (File.Exists(file)) AppFiles.RejectLink(file);
        var temporary = Path.Combine(paths.Data, "home-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { Home = new { Network = input } }));
        File.Move(temporary, file, true);
    }
    public object SaveCertificate(CertificateInput input)
    {
        if (input.Base64 is null || input.Base64.Length > 256 * 1024 || input.Password is null || input.Password.Length > 256) throw new DomainException("Sertifika bilgisi geçersiz.");
        try
        {
            var bytes = Convert.FromBase64String(input.Base64);
            using var certificate = X509CertificateLoader.LoadPkcs12(bytes, input.Password, X509KeyStorageFlags.Exportable);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) throw new DomainException("Sertifika özel anahtar içermeli ve süresi geçmemiş olmalı.");
            var encrypted = protection.CreateProtector("Home.Https.Certificate.v1").Protect(certificate.Export(X509ContentType.Pfx));
            var file = Path.Combine(paths.State, "https-certificate.protected"); if (File.Exists(file)) AppFiles.RejectLink(file);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllBytes(temporary, encrypted); File.Move(temporary, file, true);
            return new { subject = certificate.Subject, expiresAt = certificate.NotAfter.ToUniversalTime(), thumbprint = certificate.Thumbprint, requiresRestart = true };
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { throw new DomainException("PKCS#12/PFX sertifikası veya parolası okunamadı."); }
    }
    public static void ConfigureTls(KestrelServerOptions server, HomePaths paths, IDataProtectionProvider protection, IConfiguration configuration)
    {
        var network = Validate(configuration.GetSection("Home:Network").Get<NetworkPreferences>() ?? new());
        if (network.HttpPort is { } http) server.Listen(IPAddress.Parse(network.BindAddress), http);
        if (network.HttpsPort is not { } https) return;
        var path = MediaPaths.ResolveFile(paths.State, "https-certificate.protected");
        var bytes = protection.CreateProtector("Home.Https.Certificate.v1").Unprotect(File.ReadAllBytes(path));
        var certificate = X509CertificateLoader.LoadPkcs12(bytes, null);
        server.Listen(IPAddress.Parse(network.BindAddress), https, endpoint => endpoint.UseHttps(certificate));
    }
    public static void UseNetwork(WebApplication app, NetworkPreferences input)
    {
        if (input.TrustedProxies is { Length: > 0 })
        {
            var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost, ForwardLimit = 1 };
            options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
            foreach (var value in input.TrustedProxies) options.KnownProxies.Add(IPAddress.Parse(value));
            app.UseForwardedHeaders(options);
        }
        if (input.PathBase.Length > 0)
        {
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments(input.PathBase)) { context.Response.StatusCode = 404; return; }
                await next(context);
            });
            app.UsePathBase(input.PathBase);
        }
        app.Use(async (context, next) =>
        {
            if (input.RequireHttps && !context.Request.IsHttps && context.Request.Path != "/api/health")
            {
                var target = input.PublicUrl ?? $"https://{context.Request.Host.Host}:{input.HttpsPort}{input.PathBase}";
                context.Response.StatusCode = 307; context.Response.Headers.Location = target.TrimEnd('/') + context.Request.Path + context.Request.QueryString; return;
            }
            await next(context);
        });
        app.UseRouting();
    }
    public static string Html(string html, string pathBase) => html.Replace("=\"/", "=\"" + pathBase + "/", StringComparison.Ordinal)
        .Replace("<head>", $"<head><meta name=\"home-base\" content=\"{WebUtility.HtmlEncode(pathBase)}\">", StringComparison.Ordinal);
    public static string CookieName(string prefix, string pathBase) => pathBase.Length == 0 ? prefix : prefix + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathBase)))[..12].ToLowerInvariant();
    [GeneratedRegex(@"^(?:/[A-Za-z0-9_-]+)*$", RegexOptions.NonBacktracking)] private static partial Regex BasePath();
}
