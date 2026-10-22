using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class EpisodeCatalogApiTests
{
    [Fact]
    public async Task CatalogRepairRequiresAdministratorCsrfRevisionAndItsOriginalActor()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var admin = app.CreateClient(); await Session(admin);
        (await admin.PostAsJsonAsync("/api/setup", new Credentials("Admin", "episode-fixture-password"))).EnsureSuccessStatusCode(); await Session(admin);
        foreach (var input in new[] { new AccountInput("Member"), new AccountInput("Other admin", "admin") }) (await admin.PostAsJsonAsync("/api/users", new CreateAccount(input, "episode-fixture-password"))).EnsureSuccessStatusCode();
        var store = app.Services.GetRequiredService<MediaStore>(); var owner = store.GetAccount("1")!;
        var show = new ProviderContent("tmdb", 7200, "show", "Isolated", "Isolated", "", null, null, null, "https://www.themoviedb.org/tv/7200", []); var ep = new ProviderEpisode(72001, 1, 1, "One", "", null, null);
        var content = store.SaveContent(show, owner); store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, show, [ep]); content = store.GetContent(content.Id)!;
        var plan = store.CreateEpisodeRefreshPlan(content.Id, content.CatalogRevision, show, [ep with { EpisodeNumber = 2 }], owner);
        using var member = app.CreateClient(); await Login(member, "Member");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/content/{content.Id}/episode-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/content/{content.Id}/episode-refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/episode-refresh/apply", new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision))).StatusCode);
        using var other = app.CreateClient(); await Login(other, "Other admin"); Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/episode-refresh/preview", new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/episode-refresh/apply", new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision))).StatusCode);
        await Session(admin); Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/episode-refresh/apply", new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision + 1))).StatusCode);
        (await admin.PostAsJsonAsync("/api/episode-refresh/apply", new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision))).EnsureSuccessStatusCode();
        Assert.Equal(2, Assert.Single(store.GetEpisodeCatalog(content.Id)).Metadata.EpisodeNumber);
    }
    private static async Task Login(HttpClient client, string name) { await Session(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials(name, "episode-fixture-password"))).EnsureSuccessStatusCode(); await Session(client); }
    private static async Task Session(HttpClient client) { var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = "absent", ["Home:FfprobePath"] = "absent" })); }
    }
}
