using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Tests;

public sealed class PublishedHttpsTests
{
    [PublishedServerFact]
    public async Task RealKestrelRestartsWithTheProtectedCertificateAndHttpsPathBoundary()
    {
        using var home = new TestWorkspace(); using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var httpPort = FreePort(); var httpsPort = FreePort(); while (httpPort == httpsPort) httpsPort = FreePort();
        var executable = Environment.GetEnvironmentVariable("HOME_TEST_SERVER")!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in new[] { "--urls", $"http://127.0.0.1:{httpPort}", "--Home:DataPath", home.Paths.Data, "--Home:Downloads:LoopbackOnly", "true", "--Home:FfmpegPath", "absent", "--Home:FfprobePath", "absent" }) start.ArgumentList.Add(arg);
        using var original = Process.Start(start)!; int? replacement = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.GetCertHashString() == certificate.GetCertHashString() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}"), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            await Healthy(client, "/api/health", timeout.Token);
            await Csrf(client, "/api");
            var credentials = new Credentials("TLS verification", "isolated-https-password-2026");
            (await client.PostAsJsonAsync("/api/setup", credentials, timeout.Token)).EnsureSuccessStatusCode(); await Csrf(client, "/api");
            (await client.PostAsJsonAsync("/api/operations/network/certificate", new CertificateInput(Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "cert-password")), "cert-password"), timeout.Token)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync("/api/operations/network", new NetworkPreferences("/home", $"https://127.0.0.1:{httpsPort}/home", RequireHttps: true, HttpPort: httpPort, HttpsPort: httpsPort), timeout.Token)).EnsureSuccessStatusCode();
            (await client.PostAsync("/api/operations/restart", null, timeout.Token)).EnsureSuccessStatusCode();
            await original.WaitForExitAsync(timeout.Token);
            var secure = $"https://127.0.0.1:{httpsPort}/home/api";
            await Healthy(client, secure + "/health", timeout.Token);
            var state = await client.GetFromJsonAsync<JsonElement>(secure + "/session", timeout.Token);
            Assert.False(state.GetProperty("authenticated").GetBoolean());
            await Csrf(client, secure);
            (await client.PostAsJsonAsync(secure + "/auth/login", credentials, timeout.Token)).EnsureSuccessStatusCode(); await Csrf(client, secure);
            var runtime = await client.GetFromJsonAsync<JsonElement>(secure + "/operations/runtime", timeout.Token); replacement = runtime.GetProperty("processId").GetInt32();
            var network = await client.GetFromJsonAsync<JsonElement>(secure + "/operations/network", timeout.Token); Assert.True(network.GetProperty("effectiveHttps").GetBoolean());
            var redirect = await client.GetAsync($"http://127.0.0.1:{httpPort}/home/api/session", timeout.Token);
            Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode); Assert.Equal("https", redirect.Headers.Location!.Scheme);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"https://127.0.0.1:{httpsPort}/api/health", timeout.Token)).StatusCode);
            Assert.Contains(handler.CookieContainer.GetCookies(new Uri(secure)).Cast<Cookie>(), cookie => cookie.Name.StartsWith("home.session.", StringComparison.Ordinal) && cookie.Path == "/home" && cookie.Secure);
            Assert.Contains(handler.CookieContainer.GetCookies(new Uri(secure)).Cast<Cookie>(), cookie => cookie.Name.StartsWith("home.player-device.", StringComparison.Ordinal) && cookie.Path == "/home" && cookie.Secure && cookie.HttpOnly);
        }
        finally
        {
            if (replacement is null && File.Exists(Path.Combine(home.Paths.Data, "home.pid")) && int.TryParse(File.ReadAllText(Path.Combine(home.Paths.Data, "home.pid")), out var current)) replacement = current;
            if (replacement is { } pid) { try { using var process = Process.GetProcessById(pid); process.Kill(true); process.WaitForExit(5000); } catch (ArgumentException) { } }
            if (!original.HasExited) { original.Kill(true); original.WaitForExit(5000); }
        }
    }
    private static async Task Healthy(HttpClient client, string path, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { using var response = await client.GetAsync(path, token); if (response.IsSuccessStatusCode) return; } catch (HttpRequestException) { }
            await Task.Delay(100, token);
        }
        token.ThrowIfCancellationRequested();
    }
    private static async Task Csrf(HttpClient client, string prefix)
    {
        var state = await client.GetFromJsonAsync<JsonElement>(prefix + "/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", state.GetProperty("csrfToken").GetString());
    }
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
}
public sealed class PublishedServerFactAttribute : FactAttribute
{
    public PublishedServerFactAttribute() { if (!File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_SERVER"))) Skip = "Set HOME_TEST_SERVER to the current self-contained server publication."; }
}
