using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeServer.Tests;

public sealed class MusicCatalogApiTests
{
    [Fact]
    public async Task MusicListCommandsRequireCsrfOwnershipAndUnratedDiscoveryWhileVideoLinksRequireAdmin()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Session(admin);
        (await admin.PostAsJsonAsync("/api/setup", new Credentials("Admin", "music-fixture-password"))).EnsureSuccessStatusCode(); await Session(admin);
        foreach (var input in new[] { new AccountInput("Member"), new AccountInput("Restricted", MaximumAge: 13, AllowUnrated: false) })
            (await admin.PostAsJsonAsync("/api/users", new CreateAccount(input, "music-fixture-password"))).EnsureSuccessStatusCode();
        using var member = app.CreateClient(); await Login(member, "Member"); using var restricted = app.CreateClient(); await Login(restricted, "Restricted");
        var request = new MusicListInput("collection", "aaaaaaaa-1111-4111-8111-111111111111", "artist");
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsJsonAsync("/api/music/list-imports", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/music/media/unknown/videos", new MusicVideoInput("unknown", null))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/music/list-imports", request)).StatusCode); await Session(admin);
        var response = await admin.PostAsJsonAsync("/api/music/list-imports", request); response.EnsureSuccessStatusCode(); var job = (await response.Content.ReadFromJsonAsync<MusicListJob>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/music/list-imports/" + job.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/music/list-imports/{job.Id}/apply", new MusicListApply(job.Revision, [request.Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/music/list-imports/{job.Id}/apply", new MusicListApply(job.Revision, [request.Id]))).StatusCode);
        (await admin.PostAsJsonAsync($"/api/music/list-imports/{job.Id}/cancel", new MusicListApply(job.Revision, []))).EnsureSuccessStatusCode();
        Assert.Equal("cancelled", (await admin.GetFromJsonAsync<MusicListJob>($"/api/music/list-imports/{job.Id}"))!.Status);
        Assert.Empty(app.Services.GetRequiredService<MediaStore>().GetDownloads());
    }
    private static async Task Login(HttpClient client, string name) { await Session(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials(name, "music-fixture-password"))).EnsureSuccessStatusCode(); await Session(client); }
    private static async Task Session(HttpClient client) { var value = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("csrfToken").GetString()); }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent" }));
            builder.ConfigureServices(services => { foreach (var registration in services.Where(registration => registration.ServiceType == typeof(IHostedService) && registration.ImplementationType == typeof(MusicListWorker)).ToArray()) services.Remove(registration); });
        }
    }
}
