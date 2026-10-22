using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Scanning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class LibraryLifecycleTests
{
    [WindowsIdentityFact]
    public async Task RenameAndFolderMovePreserveIdentityHistoryAndManualMatch()
    {
        using var home = new TestWorkspace(); var file = home.Write("Original.mp4");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var original = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        home.Store.MatchMedia(original.Id, Movie(1), null, "Director"); home.Store.SaveProgress(original.Id, new PlaybackProgress(42, 100));
        Assert.NotNull(StableFileIdentity.Read(file));
        var directory = Path.Combine(home.Source, "Renamed folder"); Directory.CreateDirectory(directory);
        File.Move(file, Path.Combine(directory, "Renamed.mp4"));
        await home.Scan(library.Id);
        var moved = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.Equal(original.Id, moved.Id); Assert.Equal(42, moved.PositionSeconds); Assert.Equal(1, moved.Content!.ProviderId);
        Directory.Move(directory, Path.Combine(home.Source, "Again")); await home.Scan(library.Id);
        Assert.Equal(original.Id, Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items).Id);
    }
    [Fact]
    public async Task ExclusionsArchiveAndRebindKeepRecordsWithoutDeletingSourceFiles()
    {
        using var home = new TestWorkspace(); home.Write("Keep.mp4"); Directory.CreateDirectory(Path.Combine(home.Source, "extras")); File.WriteAllBytes(Path.Combine(home.Source, "extras", "Extra.mp4"), [1, 2, 3]);
        var library = home.AddLibrary(); await home.Scan(library.Id);
        Assert.Equal(2, home.Store.GetCatalog(null, null, null, 0, 60).Total);
        var ids = home.Store.GetCatalog(null, null, null, 0, 60).Items.Select(item => item.Id).ToHashSet();
        home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, true, new LibraryOptions(["extras"]))); await home.Scan(library.Id);
        Assert.Equal(1, home.Store.GetCatalog(null, null, null, 0, 60).Total); Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items, media => media.Available);
        Assert.True(File.Exists(Path.Combine(home.Source, "extras", "Extra.mp4")));
        home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, false, new LibraryOptions()));
        Assert.Empty(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var movedRoot = Path.Combine(home.Root, "relocated"); Directory.Move(home.Source, movedRoot);
        home.Store.UpdateLibrary(library.Id, new LibraryEdit("Rebound", movedRoot, true, new LibraryOptions())); await home.Scan(library.Id);
        Assert.Equal(2, home.Store.GetCatalog(null, null, null, 0, 60).Items.Count(media => media.Available));
        Assert.True(ids.SetEquals(home.Store.GetCatalog(null, null, null, 0, 60).Items.Select(item => item.Id)));
    }
    [Fact]
    public async Task LocalNfoAndArtworkAreReadOnlyAndFailedRefreshPreservesPriorMetadata()
    {
        using var home = new TestWorkspace(); var source = home.Write("Movie.2020.mkv");
        var nfo = Path.Combine(home.Source, "Movie.2020.nfo");
        await File.WriteAllTextAsync(nfo, "<movie><title>Türkçe başlık</title><plot>Gerçek dosya notu</plot><year>2020</year><tmdbid>42</tmdbid><tag>Aile</tag><thumb>poster.png</thumb></movie>", Encoding.UTF8);
        await File.WriteAllBytesAsync(Path.Combine(home.Source, "poster.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfeoAAAAASUVORK5CYII="));
        var library = home.AddLibrary(); home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, true, new LibraryOptions(ReadLocalMetadata: true)));
        await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.Equal("Türkçe başlık", media.Title); Assert.True(media.HasThumbnail); Assert.Equal(42, home.Store.GetLocalMetadata(media.Id)!.TmdbId);
        Assert.Equal("poster.png", home.Store.GetLocalMetadata(media.Id)!.Poster);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(source));
        await File.WriteAllTextAsync(nfo, "<!DOCTYPE movie [<!ENTITY x SYSTEM 'file:///private'>]><movie><title>&x;</title></movie>");
        await home.Scan(library.Id); Assert.Equal("Türkçe başlık", home.Store.GetMedia(media.Id)!.Title); Assert.NotNull(home.Store.GetLocalMetadataError(media.Id));
        home.Store.MatchMedia(media.Id, Movie(77), null, ""); await home.Scan(library.Id); Assert.Equal(77, home.Store.GetMedia(media.Id)!.Content!.ProviderId);
    }
    [Fact]
    public async Task ParentRootsExcludeApplicationStagingAndAmbiguousMatchesRemainManual()
    {
        using var home = new TestWorkspace(); home.Write("Movie.2020.mp4");
        var staging = Path.Combine(home.Paths.Data, "downloads", "staging"); Directory.CreateDirectory(staging); File.WriteAllBytes(Path.Combine(staging, "unverified.mp4"), [9, 8, 7]);
        Assert.Throws<DomainException>(() => home.Store.AddLibrary(new AddLibrary("Unsafe", home.Paths.Data, "movies")));
        var library = home.Store.AddLibrary(new AddLibrary("Parent", home.Root, "movies"));
        home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, true, new LibraryOptions(AutoMatch: true)));
        await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var job = home.Store.ClaimAutoMatch(); Assert.NotNull(job);
        home.Store.MatchMedia(media.Id, Movie(100), null, "User choice");
        Assert.False(home.Store.ApplyAutoMatch(job, Movie(200), null)); Assert.Equal(100, home.Store.GetMedia(media.Id)!.Content!.ProviderId);
    }
    [Fact]
    public async Task OptedInNfoIdentityResolvesThroughTheMetadataAdapter()
    {
        using var home = new TestWorkspace(); home.Write("Movie.mkv");
        await File.WriteAllTextAsync(Path.Combine(home.Source, "Movie.nfo"), "<movie><title>Local name</title><tmdbid>77</tmdbid></movie>");
        var library = home.AddLibrary(); home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, true, new LibraryOptions(ReadLocalMetadata: true, AutoMatch: true)));
        await home.Scan(library.Id);
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Tmdb:Token"] = "fixture-token" }).Build(), new EphemeralDataProtectionProvider());
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var provider = new MetadataProvider(new MetadataClients(), settings, cache);
        using var worker = new AutoMatchWorker(home.Store, provider, settings, new EventHub(), NullLogger<AutoMatchWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (home.Store.GetAutoMatchJobs().Any(job => job.Status is "queued" or "running")) await Task.Delay(50, timeout.Token);
            Assert.Equal("matched", Assert.Single(home.Store.GetAutoMatchJobs()).Status);
            Assert.Equal(77, Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items).Content!.ProviderId);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task FilesystemChangesQueueDebouncedDurableScans()
    {
        using var home = new TestWorkspace(); var library = home.AddLibrary();
        home.Store.UpdateLibrary(library.Id, new LibraryEdit(library.Name, library.Path, true, new LibraryOptions(WatchChanges: true)));
        using var monitor = new LibraryMonitor(home.Store, new EventHub(), NullLogger<LibraryMonitor>.Instance);
        await monitor.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!home.Store.GetJobs().Any()) await Task.Delay(100, timeout.Token);
            var initial = home.Store.TryClaimJob()!; await home.Scanner.ScanAsync(initial, timeout.Token);
            home.Write("New.mp4");
            while (!home.Store.GetJobs().Any(job => job.Status == "queued")) await Task.Delay(100, timeout.Token);
            Assert.Single(home.Store.GetJobs(), job => job.Status == "queued");
        }
        finally { await monitor.StopAsync(CancellationToken.None); }
    }
    private static ProviderContent Movie(int id) => new("tmdb", id, "movie", "Fixture", "Fixture", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/" + id, []);
    private sealed class MetadataClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new MetadataHandler()) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
    }
    private sealed class MetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/3/movie/77", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"id":77,"title":"Resolved fixture","original_title":"Resolved fixture","release_date":"2020-01-01"}""", Encoding.UTF8, "application/json") });
        }
    }
}
public sealed class WindowsIdentityFactAttribute : FactAttribute
{
    public WindowsIdentityFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows file-ID path; platform acceptance is separate."; }
}
