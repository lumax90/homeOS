using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace HomeServer.Tests;

public sealed class GameStoreTests
{
    [Fact]
    public void BrowsingStoreDoesNotDownloadInstallOrCreatePersonalRecords()
    {
        using var fixture = new StoreFixture();
        var game = Assert.Single(fixture.Catalog.List(fixture.Home.Store, fixture.Actor));
        Assert.True(game.InStore); Assert.False(game.Installed); Assert.Equal(0, fixture.Source.Requests); Assert.Empty(fixture.Home.Store.GetGameStates(fixture.Actor.Id));
        Assert.Equal(409, Assert.Throws<DomainException>(() => fixture.Catalog.Require(game.Id, fixture.Actor)).StatusCode);
        Assert.Empty(fixture.Home.Store.GetProductJobs(fixture.Actor, "game-install"));
    }
    [Fact]
    public async Task ExplicitInstallVerifiesThePinnedSourcePublishesAtomicallyAndSurvivesCatalogRestart()
    {
        using var fixture = new StoreFixture(); var job = fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor);
        Assert.Equal(0, fixture.Source.Requests); Assert.Equal(job.Id, fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor).Id);
        var claimed = fixture.Home.Store.ClaimProductJob(["game-install"])!; await fixture.Installer.RunAsync(claimed, CancellationToken.None);
        Assert.Equal(1, fixture.Source.Requests); Assert.True(fixture.Catalog.Installed(fixture.Definition.Id));
        var manifest = fixture.Catalog.Require(fixture.Definition.Id, fixture.Actor); Assert.Equal("html", manifest.Runtime); Assert.Equal(["web-profile.json"], manifest.SavePaths);
        Assert.True(Assert.Single(fixture.Home.Store.GetGameStates(fixture.Actor.Id)).InLibrary); Assert.Equal("completed", fixture.Home.Store.GetProductJob(job.Id)!.Status);
        using var entry = fixture.Catalog.OpenVerified(manifest, "index.html"); using var reader = new StreamReader(entry); var html = await reader.ReadToEndAsync();
        Assert.Contains("home-web.js", html); Assert.DoesNotContain("tracker.invalid", html); Assert.DoesNotContain("<script src=\"main.js\"", html);
        var restarted = new GameCatalog(fixture.Environment, fixture.Configuration, fixture.Home.Paths, fixture.Store); Assert.True(restarted.Installed(fixture.Definition.Id));
        var second = fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor); await fixture.Installer.RunAsync(fixture.Home.Store.ClaimProductJob(["game-install"])!, CancellationToken.None); Assert.Equal(1, fixture.Source.Requests); Assert.Equal("completed", fixture.Home.Store.GetProductJob(second.Id)!.Status);
    }
    [Fact]
    public async Task TamperedSourceCannotBePublishedOrMarkedInstalled()
    {
        using var fixture = new StoreFixture(); fixture.Source.Archive = fixture.MakeArchive(tampered: true);
        fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor); var job = fixture.Home.Store.ClaimProductJob(["game-install"])!;
        Assert.Equal(422, (await Assert.ThrowsAsync<DomainException>(() => fixture.Installer.RunAsync(job, CancellationToken.None))).StatusCode);
        Assert.False(fixture.Catalog.Installed(fixture.Definition.Id)); Assert.Empty(fixture.Home.Store.GetGameStates(fixture.Actor.Id)); Assert.False(Directory.Exists(fixture.Catalog.InstalledDirectory(fixture.Definition)));
    }
    [Theory]
    [InlineData("../outside.js")]
    [InlineData("a/../../outside.js")]
    [InlineData("C:/outside.js")]
    public async Task ArchiveTraversalIsRejected(string relative)
    {
        using var fixture = new StoreFixture(); var archive = Path.Combine(fixture.Home.Root, "malicious.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { var entry = zip.CreateEntry(fixture.Definition.ArchiveRoot + relative); using var writer = new StreamWriter(entry.Open()); writer.Write("unsafe"); }
        var target = Path.Combine(fixture.Home.Root, "extract"); Directory.CreateDirectory(target);
        await Assert.ThrowsAsync<DomainException>(() => GameInstaller.ExtractReviewedAsync(archive, target, fixture.Definition, CancellationToken.None)); Assert.Empty(Directory.GetFiles(target));
    }
    [Fact]
    public async Task ConflictingTargetIsPreserved()
    {
        using var fixture = new StoreFixture(); var destination = fixture.Catalog.InstalledDirectory(fixture.Definition); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "manifest.json"), "unrelated target");
        fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor); var job = fixture.Home.Store.ClaimProductJob(["game-install"])!;
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => fixture.Installer.RunAsync(job, CancellationToken.None))).StatusCode);
        Assert.Equal("unrelated target", File.ReadAllText(Path.Combine(destination, "manifest.json"))); Assert.False(fixture.Catalog.Installed(fixture.Definition.Id));
    }
    [Fact]
    public void ViewerCannotQueueAcquisitionAndUnratedPolicyHidesTheStore()
    {
        using var fixture = new StoreFixture(); var viewer = fixture.Actor with { Role = "viewer", CanAcquire = false };
        Assert.Equal(403, Assert.Throws<DomainException>(() => fixture.Installer.Queue(fixture.Definition.Id, viewer)).StatusCode);
        Assert.Empty(fixture.Catalog.List(fixture.Home.Store, viewer with { MaximumAge = 12, AllowUnrated = false })); Assert.Equal(0, fixture.Source.Requests);
    }
    [Fact]
    public async Task CancellationDoesNotCreateAnInstalledGame()
    {
        using var fixture = new StoreFixture(); fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor); var job = fixture.Home.Store.ClaimProductJob(["game-install"])!;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Installer.RunAsync(job, cancelled.Token)); Assert.False(fixture.Catalog.Installed(fixture.Definition.Id));
    }
    private sealed class StoreFixture : IDisposable
    {
        public TestWorkspace Home { get; } = new();
        public Account Actor { get; }
        public GameStoreDefinition Definition { get; }
        public EnvironmentStub Environment { get; }
        public IConfiguration Configuration { get; }
        public GameStore Store { get; }
        public GameCatalog Catalog { get; }
        public SourceStub Source { get; } = new();
        public GameInstaller Installer { get; }
        private readonly Dictionary<string, byte[]> _files = new() { ["index.html"] = Encoding.UTF8.GetBytes("<!doctype html><html><head><script src=\"https://tracker.invalid/a.js\"></script></head><body><button>Play</button><script src=\"main.js\"></script></body></html>"), ["main.js"] = Encoding.UTF8.GetBytes("window.fixtureGame=true;"), ["LICENSE"] = Encoding.UTF8.GetBytes("Isolated fixture license, not production game content.") };
        public StoreFixture(bool spacedAssets = false)
        {
            if (spacedAssets) _files.Add("textures/particles/cloud - Copie.png", [137,80,78,71]);
            Home.Store.CreateOwner(new("Owner", "isolated")); Actor = Home.Store.GetAccount("1")!;
            var root = Path.Combine(Home.Root, "server"); var storePath = Path.Combine(root, "Gaming", "Store"); var artwork = Path.Combine(storePath, "artwork"); var runtime = Path.Combine(root, "Gaming", "Runtime"); Directory.CreateDirectory(artwork); Directory.CreateDirectory(runtime);
            var image = new byte[] { 137, 80, 78, 71 }; File.WriteAllBytes(Path.Combine(artwork, "fixture-static.png"), image);
            var commit = new string('a', 40); var sourceFiles = _files.Select(pair => new GameSourceFile(pair.Key, pair.Value.Length, GitHash(pair.Value))).ToArray();
            Definition = new("fixture-static", "Isolated store game", "Fixture", "fixture/game", commit, "MIT", "LICENSE", "https://fixture.invalid/", "2048", "digital", "fixture.1", $"https://codeload.github.com/fixture/game/zip/{commit}", "game-" + commit + "/", ["puzzle"], ["main.js"], sourceFiles, Convert.ToHexStringLower(SHA256.HashData(image)), sourceFiles.Sum(file => file.Size));
            File.WriteAllText(Path.Combine(storePath, "catalog.json"), JsonSerializer.Serialize(new GameStoreDocument(1, "2026-10-19", [Definition]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.WriteAllText(Path.Combine(runtime, "home-web.js"), "// isolated fixture runtime"); File.WriteAllText(Path.Combine(runtime, "home-web.css"), "/* isolated fixture style */");
            Environment = new(root); Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Games:PackagePath"] = Path.Combine(root, "empty-bundled") }).Build();
            Store = new(Environment, Configuration); Catalog = new(Environment, Configuration, Home.Paths, Store); Installer = new(Store, Catalog, Home.Store, Home.Paths, Environment, Source, new EventHub()); Source.Archive = MakeArchive();
        }
        public string MakeArchive(bool tampered = false)
        {
            var path = Path.Combine(Home.Root, Guid.NewGuid().ToString("N") + ".zip"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var pair in _files) { var entry = zip.CreateEntry(Definition.ArchiveRoot + pair.Key); using var output = entry.Open(); var bytes = tampered && pair.Key == "main.js" ? Enumerable.Repeat((byte)'x', pair.Value.Length).ToArray() : pair.Value; output.Write(bytes); } return path;
        }
        public void Dispose() => Home.Dispose();
    }
    [Fact]
    public async Task ReviewedAssetWithInternalSpacesSurvivesInstallationAndColdCatalogStartup()
    {
        using var fixture = new StoreFixture(spacedAssets: true);
        fixture.Installer.Queue(fixture.Definition.Id, fixture.Actor); await fixture.Installer.RunAsync(fixture.Home.Store.ClaimProductJob(["game-install"])!, CancellationToken.None);
        var restarted = new GameCatalog(fixture.Environment, fixture.Configuration, fixture.Home.Paths, fixture.Store);
        var manifest = restarted.Require(fixture.Definition.Id, fixture.Actor);
        using var file = restarted.OpenVerified(manifest,"textures/particles/cloud - Copie.png"); Assert.Equal(4,file.Length);
    }
    [Theory]
    [InlineData("../outside.png")]
    [InlineData("textures/../outside.png")]
    [InlineData("textures /cloud.png")]
    [InlineData("textures./cloud.png")]
    [InlineData("textures/CON.png")]
    [InlineData("textures/cloud.exe")]
    [InlineData("textures/cloud.png ")]
    public void AllowingInternalSpacesDoesNotRelaxPortableFilesystemOrFileTypeBoundaries(string path) => Assert.False(GameCatalog.IsSafeAssetPath(path));
    private static string GitHash(byte[] bytes) { var prefix = Encoding.UTF8.GetBytes($"blob {bytes.Length}\0"); return Convert.ToHexStringLower(SHA1.HashData([..prefix, ..bytes])); }
    public sealed class SourceStub : IGamePackageSource
    {
        public string Archive { get; set; } = ""; public int Requests { get; private set; }
        public Task<GameSourceResponse> OpenAsync(GameStoreDefinition game, CancellationToken token) { token.ThrowIfCancellationRequested(); Requests++; return Task.FromResult(new GameSourceResponse(File.OpenRead(Archive), new FileInfo(Archive).Length)); }
    }
    public sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests"; public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root; public string WebRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
