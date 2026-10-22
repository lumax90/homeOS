using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class NetworkTests
{
    [Fact]
    public async Task SubPathHostsApiHtmlAssetsManifestAndScopedCookies()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home, "127.0.0.1");
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/health")).StatusCode);
        (await client.GetAsync("/home/api/health")).EnsureSuccessStatusCode();
        var html = await client.GetStringAsync("/home/");
        Assert.Contains("name=\"home-base\" content=\"/home\"", html); Assert.Contains("/home/assets/", html);
        var manifest = await client.GetFromJsonAsync<JsonElement>("/home/manifest.webmanifest"); Assert.Equal("/home/", manifest.GetProperty("scope").GetString());
        var session = await client.GetFromJsonAsync<JsonElement>("/home/api/session"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        var setup = await client.PostAsJsonAsync("/home/api/setup", new Credentials("Owner", "private-network-password")); setup.EnsureSuccessStatusCode();
        Assert.Contains(setup.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("home.session.", StringComparison.Ordinal) && cookie.Contains("path=/home", StringComparison.OrdinalIgnoreCase));
        var network = await client.GetFromJsonAsync<JsonElement>("/home/api/operations/network"); Assert.False(network.GetProperty("effectiveHttps").GetBoolean());
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.0.2.12", false)]
    public async Task ForwardedSchemeIsAcceptedOnlyFromTheConfiguredProxy(string remote, bool trusted)
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home, remote);
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var result = await client.GetAsync("/home/api/session"); result.EnsureSuccessStatusCode();
        var cookie = result.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("home.csrf.", StringComparison.Ordinal));
        Assert.Equal(trusted, cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TlsPrivateKeyIsProtectedAndNetworkSettingsDoNotEnableAnUnconfiguredEndpoint()
    {
        using var home = new TestWorkspace(); var protection = new EphemeralDataProtectionProvider();
        var network = new NetworkConfiguration(home.Paths, protection, new ConfigurationBuilder().Build());
        Assert.Throws<DomainException>(() => network.Save(new NetworkPreferences(HttpsPort: 5443)));
        using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var bytes = cert.Export(X509ContentType.Pfx, "cert-fixture-password");
        network.SaveCertificate(new CertificateInput(Convert.ToBase64String(bytes), "cert-fixture-password"));
        var stored = File.ReadAllBytes(Path.Combine(home.Paths.State, "https-certificate.protected")); Assert.NotEqual(bytes, stored);
        var decoded = protection.CreateProtector("Home.Https.Certificate.v1").Unprotect(stored);
        using var recovered = X509CertificateLoader.LoadPkcs12(decoded, null); Assert.True(recovered.HasPrivateKey);
        network.Save(new NetworkPreferences("/home", "https://localhost:5443/home", RequireHttps: true, HttpsPort: 5443));
        Assert.Equal("/home", network.Pending!.PathBase);
        Assert.Throws<DomainException>(() => NetworkConfiguration.Validate(new NetworkPreferences("/home\"><script>")));
    }
    private sealed class Factory(TestWorkspace home, string remote) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent",
                ["Home:Network:PathBase"] = "/home", ["Home:Network:TrustedProxies:0"] = "127.0.0.1"
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress(remote)));
        }
    }
    private sealed class RemoteAddress(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        { app.Use((context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Parse(address); return continuation(context); }); next(app); };
    }
}
