using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task SetupRequiresCsrfAndCannotBeRepeated()
    {
        using var workspace = new TestWorkspace();
        await using var app = new HomeFactory(workspace);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/libraries")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/setup", new Credentials("Home", "test-password-2026"))).StatusCode);
        await UpdateCsrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/setup", new Credentials("Home", "test-password-2026"))).StatusCode);
        await UpdateCsrf(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/setup", new Credentials("Other", "test-password-2026"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/libraries")).StatusCode);
        var owner = app.Services.GetRequiredService<MediaStore>().GetOwner()!;
        Assert.Equal("Home", owner.DisplayName);
        Assert.NotEqual("test-password-2026", owner.PasswordHash);
        Assert.Equal(workspace.Paths.Data, app.Services.GetRequiredService<HomePaths>().Data);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/catalog")).StatusCode);
    }

    [Fact]
    public async Task CatalogStreamSupportsByteRangesAndDoesNotExposeArbitraryFiles()
    {
        using var workspace = new TestWorkspace();
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        workspace.Write("range.mp4", bytes);
        await using var app = new HomeFactory(workspace);
        using var client = app.CreateClient();
        await UpdateCsrf(client);
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Home", "test-password-2026"))).EnsureSuccessStatusCode();
        await UpdateCsrf(client);
        var store = app.Services.GetRequiredService<MediaStore>();
        var library = store.AddLibrary(new AddLibrary("Ranges", workspace.Source, "movies"));
        var source = MediaPaths.Inspect(workspace.Source, Path.Combine(workspace.Source, "range.mp4"))!;
        var added = store.UpsertFile(library.Id, source, "test");
        Assert.Equal((HttpStatusCode)428, (await client.GetAsync($"/api/media/{added.Id}/stream")).StatusCode);
        var started = await client.PostAsJsonAsync("/api/playback", new PlaybackStart(added.Id, new(["mp4"], [], [], false))); started.EnsureSuccessStatusCode();
        var playback = (await started.Content.ReadFromJsonAsync<PlaybackSession>())!;
        using var request = new HttpRequestMessage(HttpMethod.Get, playback.Url);
        request.Headers.Range = new RangeHeaderValue(10, 19);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 10-19/256", response.Content.Headers.ContentRange!.ToString());
        Assert.Equal(bytes[10..20], await response.Content.ReadAsByteArrayAsync());
        var malicious = store.UpsertFile(library.Id, source with { RelativePath = "../outside.mp4" }, "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/playback", new PlaybackStart(malicious.Id, new(["mp4"], [], [], false)))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/media/{added.Id}/progress", new PlaybackProgress(10, 20))).StatusCode);
    }

    private static async Task UpdateCsrf(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    }

    private sealed class HomeFactory(TestWorkspace workspace) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = workspace.Paths.Data,
                ["Home:FfmpegPath"] = Path.Combine(workspace.Root, "absent"),
                ["Home:FfprobePath"] = Path.Combine(workspace.Root, "absent"),
                ["Home:DemoPath"] = ""
            }));
        }
    }
}
