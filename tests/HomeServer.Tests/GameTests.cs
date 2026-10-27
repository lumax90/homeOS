using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class GameTests
{
    private static GameManifest Definition(string version = "fixture.1") => new("fixture-game", "Isolated game fixture", "Tests", "https://fixture.invalid/game", "https://fixture.invalid/source", "fixture", version, "isolated-test", "MIT", "web", "1/1", ["arcade"], ["home-progress.json"], []);
    private static (Account Actor, LoginSession Login) Login(TestWorkspace home)
    { home.Store.CreateOwner(new("Owner", "isolated-password")); var actor = home.Store.GetAccount("1")!; return (actor, home.Store.CreateLoginSession(actor.Id, home.Store.GetAccountSecret(actor.Id)!.Version, "isolated-game-test")); }
    private static GameStart Start() => new(Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    [Fact]
    public void SessionStartIsIdempotentAndOnlyNewestUserSessionCanWrite()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var input = Start();
        var first = home.Store.StartGameSession(game, input, actor, login.Id);
        Assert.Equal(first.Id, home.Store.StartGameSession(game, input, actor, login.Id).Id);
        Assert.Single(home.Store.GetActiveGameSessions(actor));
        var secondInput = Start(); var second = home.Store.StartGameSession(game, secondInput, actor, login.Id);
        Assert.Null(home.Store.GetGameRuntimeSession(first.Id, input.Ticket));
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.PulseGameSession(first.Id, game, new(false, 0, new() { ["home-progress.json"] = "{\"map\":9}" }), actor, login.Id)).StatusCode);
        Assert.Equal(second.Id, Assert.Single(home.Store.GetActiveGameSessions(actor)).Id);
    }
    [Fact]
    public void SavesHaveCasRetryReceiptsAndPreferenceRemovalDoesNotDeleteProgress()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var session = home.Store.StartGameSession(game, Start(), actor, login.Id);
        var pulse = new GamePulse(false, 0, new() { ["home-progress.json"] = "{\"map\":3}" });
        Assert.Equal(1, home.Store.PulseGameSession(session.Id, game, pulse, actor, login.Id).Revision);
        Assert.Equal(1, home.Store.PulseGameSession(session.Id, game, pulse, actor, login.Id).Revision);
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.PulseGameSession(session.Id, game, pulse with { Files = new() { ["home-progress.json"] = "{\"map\":2}" } }, actor, login.Id)).StatusCode);
        var preference = home.Store.SetGamePreference(game.Id, new(false, true), actor); Assert.False(preference.InLibrary); Assert.True(preference.Favorite); Assert.True(preference.HasSave);
        Assert.Equal("{\"map\":3}", home.Store.GetGameCheckpoint(game.Id, game.Version, actor.Id).Files["home-progress.json"]);
    }
    [Fact]
    public void SaveBuildsArePartitionedAndSurviveStoreRestart()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var original = Definition();
        var session = home.Store.StartGameSession(original, Start(), actor, login.Id);
        home.Store.PulseGameSession(session.Id, original, new(false, 0, new() { ["home-progress.json"] = "{\"map\":4}" }), actor, login.Id);
        var next = Definition("fixture.2"); var second = home.Store.StartGameSession(next, Start(), actor, login.Id); Assert.Empty(home.Store.GetGameCheckpoint(next.Id, next.Version, actor.Id).Files);
        home.Store.PulseGameSession(second.Id, next, new(false, 0, new() { ["home-progress.json"] = "{\"map\":1}" }), actor, login.Id);
        var restarted = new MediaStore(home.Paths); restarted.Initialize();
        Assert.Equal("{\"map\":4}", restarted.GetGameCheckpoint(original.Id, original.Version, actor.Id).Files["home-progress.json"]);
        Assert.Equal("{\"map\":1}", restarted.GetGameCheckpoint(next.Id, next.Version, actor.Id).Files["home-progress.json"]);
    }
    [Theory]
    [InlineData("../home.db", "{}", 400)]
    [InlineData("keys.tres", "{}", 400)]
    [InlineData("home-progress.json", "not json", 400)]
    [InlineData("home-progress.json", "[]", 400)]
    public void SaveBoundariesRejectPathsAndMalformedContent(string path, string content, int status)
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var session = home.Store.StartGameSession(game, Start(), actor, login.Id);
        Assert.Equal(status, Assert.Throws<DomainException>(() => home.Store.PulseGameSession(session.Id, game, new(false, 0, new() { [path] = content }), actor, login.Id)).StatusCode);
        Assert.Empty(home.Store.GetGameCheckpoint(game.Id, game.Version, actor.Id).Files);
    }
    [Fact]
    public void OversizedSaveCannotTouchExistingCheckpoint()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var session = home.Store.StartGameSession(game, Start(), actor, login.Id);
        Assert.Equal(413, Assert.Throws<DomainException>(() => home.Store.PulseGameSession(session.Id, game, new(false, 0, new() { ["home-progress.json"] = "{\"data\":\"" + new string('x', 33000) + "\"}" }), actor, login.Id)).StatusCode);
        Assert.Equal(0, home.Store.GetGameCheckpoint(game.Id, game.Version, actor.Id).Revision);
    }
    [Fact]
    public void PageExitWritesTheLatestCheckpointAndReleasesTheSessionInOneCommand()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var input = Start(); var session = home.Store.StartGameSession(game, input, actor, login.Id);
        var saved = home.Store.EndGameSession(session.Id, game, new(false, 0, new() { ["home-progress.json"] = "{\"map\":7}" }), actor, login.Id);
        Assert.Equal(1, saved.Revision); Assert.Equal("{\"map\":7}", saved.Files["home-progress.json"]); Assert.Empty(home.Store.GetActiveGameSessions(actor)); Assert.Null(home.Store.GetGameRuntimeSession(session.Id, input.Ticket));
    }
    [Fact]
    public void StaleExitCannotOverwriteProgressButStillReleasesItsOwnSession()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var session = home.Store.StartGameSession(game, Start(), actor, login.Id);
        home.Store.PulseGameSession(session.Id, game, new(false, 0, new() { ["home-progress.json"] = "{\"map\":8}" }), actor, login.Id);
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.EndGameSession(session.Id, game, new(false, 0, new() { ["home-progress.json"] = "{\"map\":1}" }), actor, login.Id)).StatusCode);
        Assert.Empty(home.Store.GetActiveGameSessions(actor)); Assert.Equal("{\"map\":8}", home.Store.GetGameCheckpoint(game.Id, game.Version, actor.Id).Files["home-progress.json"]);
    }
    [Fact]
    public void ExitCannotReleaseADifferentLoginSession()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var game = Definition(); var session = home.Store.StartGameSession(game, Start(), actor, login.Id);
        var other = home.Store.CreateLoginSession(actor.Id, home.Store.GetAccountSecret(actor.Id)!.Version, "other-login");
        Assert.Equal(409, Assert.Throws<DomainException>(() => home.Store.EndGameSession(session.Id, game, new(false), actor, other.Id)).StatusCode); Assert.Single(home.Store.GetActiveGameSessions(actor));
    }
    [Fact]
    public void LoginRevocationExpiryAndUnratedPolicyInvalidateTickets()
    {
        using var home = new TestWorkspace(); var (actor, login) = Login(home); var input = Start(); var session = home.Store.StartGameSession(Definition(), input, actor, login.Id);
        Assert.NotNull(home.Store.GetGameRuntimeSession(session.Id, input.Ticket));
        home.Store.RevokeLoginSession(login.Id, actor.Id); Assert.Null(home.Store.GetGameRuntimeSession(session.Id, input.Ticket)); Assert.Empty(home.Store.GetActiveGameSessions(actor));
        Assert.False(GameCatalog.Allowed(actor with { Role = "viewer", MaximumAge = 12, AllowUnrated = false }));
        Assert.False(GameCatalog.Allowed(actor with { Enabled = false }));
        Assert.True(GameCatalog.Allowed(actor with { Role = "viewer", MaximumAge = 12, AllowUnrated = true }));
    }
    [Fact]
    public void SchemaFortyMigrationPreservesExistingMediaAndPersonalState()
    {
        using var home = new TestWorkspace(); var data = AppFiles.DirectoryIn(home.Root, "actual-schema39");
        var paths = new HomePaths(data, Path.Combine(data, "thumbnails"), null);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(data, "home.db")}"))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=OFF"; command.ExecuteNonQuery();
            foreach (var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.") && int.TryParse(name.Split(".Migrations.")[1][..3], out var number) && number <= 39).Order())
            { using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery(); }
            command.CommandText = "PRAGMA user_version=39"; command.ExecuteNonQuery();
        }
        var store = new MediaStore(paths); store.CreateOwner(new("Owner", "isolated-hash")); var actor = store.GetAccount("1")!;
        var library = store.AddLibrary(new("Isolated migration library", home.Source, "movies")); var file = home.Write("original.mp4");
        var media = store.UpsertFile(library.Id, MediaPaths.Inspect(home.Source, file)!, "isolated"); store.SaveProgress(media.Id, new(37, 100), actor);
        store.Initialize(); store.Initialize(); Assert.Equal(37, store.GetMedia(media.Id, actor)!.PositionSeconds); Assert.Single(store.GetLibraries(viewer: actor));
        using var verified = new SqliteConnection($"Data Source={Path.Combine(data, "home.db")}"); verified.Open(); using var check = verified.CreateCommand(); check.CommandText = "PRAGMA user_version"; Assert.Equal((long)MediaStore.LatestSchema, check.ExecuteScalar()); check.CommandText = "PRAGMA foreign_key_check"; using var rows = check.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public async Task ApiUsesCsrfAndOpaqueSandboxWithoutExposingPackageRoots()
    {
        using var home = new TestWorkspace(); var packageRoot = CreatePackage(home);
        await using var app = new Factory(home, packageRoot); using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games")).StatusCode);
        await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Owner", "isolated-password-2026"))).EnsureSuccessStatusCode(); await Csrf(client);
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/games"); Assert.False(catalog.GetProperty("serverRunnerAvailable").GetBoolean()); Assert.Single(catalog.GetProperty("items").EnumerateArray());
        var input = Start(); var response = await client.PostAsJsonAsync("/api/games/fixture-game/sessions", input); response.EnsureSuccessStatusCode(); var launch = (await response.Content.ReadFromJsonAsync<GameLaunch>())!;
        client.DefaultRequestHeaders.Remove("X-Home-User"); client.DefaultRequestHeaders.Add("X-Home-User", "other-profile");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/games/fixture-game/preference", new GamePreference(true, true))).StatusCode); await Csrf(client);
        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var entry = await anonymous.GetAsync(launch.Url); entry.EnsureSuccessStatusCode(); var csp = Assert.Single(entry.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("sandbox allow-scripts", csp); Assert.DoesNotContain("allow-same-origin", csp); Assert.DoesNotContain("/api", csp);
        Assert.Equal("*", Assert.Single(entry.Headers.GetValues("Access-Control-Allow-Origin"))); Assert.True(entry.Headers.CacheControl!.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(launch.Url.Replace("index.html", "manifest.json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/GamePacks/fixture-game/index.html")).StatusCode);
        using var range = new HttpRequestMessage(HttpMethod.Get, launch.Url.Replace("index.html", "index.wasm")); range.Headers.Range = new RangeHeaderValue(1, 2);
        using var ranged = await anonymous.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, ranged.StatusCode); Assert.Equal(new byte[] { 1, 2 }, await ranged.Content.ReadAsByteArrayAsync());
        (await client.DeleteAsync("/api/games/sessions/" + launch.Id)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(launch.Url)).StatusCode);
        var exitLaunchResponse = await client.PostAsJsonAsync("/api/games/fixture-game/sessions", Start()); exitLaunchResponse.EnsureSuccessStatusCode(); var exitLaunch = (await exitLaunchResponse.Content.ReadFromJsonAsync<GameLaunch>())!;
        (await client.PostAsJsonAsync("/api/games/sessions/" + exitLaunch.Id + "/end", new GamePulse(false, 0, new() { ["home-progress.json"] = "{\"map\":5}" }))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(exitLaunch.Url)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/fixture-game/sessions", Start())).StatusCode);
    }
    [Fact]
    public void CorruptOrLinkedPackageFilesAreNotPublished()
    {
        using var home = new TestWorkspace(); var root = CreatePackage(home);
        var environment = new TestEnvironment(root); var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Games:PackagePath"] = root }).Build();
        var catalog = new GameCatalog(environment, configuration, home.Paths, new GameStore(environment, configuration)); var (actor, _) = Login(home); var game = catalog.Require("fixture-game", actor);
        File.WriteAllBytes(Path.Combine(root, "fixture-game", "index.wasm"), [9, 9, 9, 9]); Assert.Equal(409, Assert.Throws<DomainException>(() => catalog.OpenVerified(game, "index.wasm")).StatusCode);
        Assert.Equal(404, Assert.Throws<DomainException>(() => catalog.OpenVerified(game, "../home.db")).StatusCode);
    }
    private static string CreatePackage(TestWorkspace home)
    {
        var root = Path.Combine(home.Root, "game-fixtures"); var folder = Path.Combine(root, "fixture-game"); Directory.CreateDirectory(Path.Combine(folder, "licenses"));
        var names = new[] { "index.html", "index.js", "index.wasm", "index.pck", "home-game.js", "cover.png", "backdrop.png", "licenses/Game.txt" };
        var files = names.Select(name => { var bytes = new byte[] { 0, 1, 2, 3 }; File.WriteAllBytes(Path.Combine(folder, name), bytes); return new GameAsset(name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))); }).ToArray();
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(Definition() with { Files = files })); return root;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedWebUninstallKeepsSourceAndCheckpointAndRecoversAfterAtomicTrashMove(bool interrupted)
    {
        using var home=new TestWorkspace();var(actor,login)=Login(home);var source=CreatePackage(home);var managed=AppFiles.DirectoryIn(home.Paths.State,"games","library","fixture-game","original");
        foreach(var input in Directory.EnumerateFiles(Path.Combine(source,"fixture-game"),"*",SearchOption.AllDirectories))
        {var target=Path.Combine(managed,Path.GetRelativePath(Path.Combine(source,"fixture-game"),input));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(input,target);}
        var environment=new TestEnvironment(home.Root);var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Home:Games:PackagePath",Path.Combine(home.Root,"empty-packages")},{"Home:Games:StorePath",Path.Combine(home.Root,"empty-store")}}).Build();
        var catalog=new GameCatalog(environment,config,home.Paths,new GameStore(environment,config));catalog.Register(managed,"fixture-game");var game=catalog.Require("fixture-game",actor);
        var session=home.Store.StartGameSession(game,Start(),actor,login.Id);home.Store.EndGameSession(session.Id,game,new(false,0,new(){{"home-progress.json","{\"map\":11}"}}),actor,login.Id);home.Store.SetGamePreference(game.Id,new(true,true),actor);
        using var roms=new NativeRomLibrary(home.Paths,home.Store);var events=new EventHub();var native=new NativeGameManager(home.Store,home.Paths,roms,new(home.Paths),events,NullLogger<NativeGameManager>.Instance);var removal=new GameRemoval(home.Paths,home.Store,catalog,roms,native,events);
        var queued=removal.Queue(game.Id,new(true),actor);var job=home.Store.ClaimProductJob(["game-uninstall"])!;
        if(interrupted)
        {
            var trashParent=AppFiles.DirectoryIn(home.Paths.State,"games","removal-trash");
            Assert.Throws<IOException>(()=>home.Store.ApplyGameRemoval(job,null,()=>{Directory.Move(managed,Path.Combine(trashParent,job.Id));throw new IOException("Original isolated interruption after atomic rename");}));
            home.Store.RecoverProductJobs(["game-uninstall"]);job=home.Store.ClaimProductJob(["game-uninstall"])!;
        }
        await removal.RunAsync(job,CancellationToken.None);
        Assert.False(catalog.Installed(game.Id));Assert.False(File.Exists(Path.Combine(managed,"manifest.json")));Assert.True(File.Exists(Path.Combine(source,"fixture-game","manifest.json")));
        var state=home.Store.GetGameStates(actor.Id).Single();Assert.False(state.InLibrary);Assert.True(state.Favorite);Assert.True(state.HasSave);
        Assert.Equal("{\"map\":11}",home.Store.GetGameCheckpoint(game.Id,game.Version,actor.Id).Files["home-progress.json"]);Assert.Equal("completed",home.Store.GetProductJob(queued.Id)!.Status);
    }
    private static async Task Csrf(HttpClient client)
    { var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); foreach (var (header, property) in new[] { ("X-Home-User", "userId"), ("X-Home-Session", "sessionId") }) { client.DefaultRequestHeaders.Remove(header); if (session.GetProperty(property).ValueKind == JsonValueKind.String) client.DefaultRequestHeaders.Add(header, session.GetProperty(property).GetString()); } }
    private sealed class Factory(TestWorkspace home, string packages) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Home:DataPath"] = home.Paths.Data, ["Home:Games:PackagePath"] = packages, ["Home:Games:StorePath"] = Path.Combine(home.Root, "empty-store"), ["Home:Downloads:LoopbackOnly"] = "true", ["Home:FfmpegPath"] = Path.Combine(home.Root, "absent"), ["Home:FfprobePath"] = Path.Combine(home.Root, "absent"), ["Home:DemoPath"] = "" }));
    }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests"; public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root; public string WebRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
