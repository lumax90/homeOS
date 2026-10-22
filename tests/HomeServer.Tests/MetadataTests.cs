using HomeServer.Core;
using HomeServer.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class MetadataTests
{
    private static ProviderContent Movie => new("tmdb", 42, "movie", "Bir Film", "A Film", "Açıklama", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", ["Drama"]);

    [Fact]
    public async Task MatchingSharesContentIdentityAndSurvivesRescanAndUnlink()
    {
        using var home = new TestWorkspace();
        home.Write("A.Film.2020.1080p.mp4");
        home.Write("A.Film.2020.2160p.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        var first = home.Store.MatchMedia(files[0].Id, Movie, null, "Sinema");
        var second = home.Store.MatchMedia(files[1].Id, Movie, null, "Yönetmen sürümü");
        Assert.Equal(first.Content!.Id, second.Content!.Id);
        Assert.Equal(2, second.Content.VersionCount);
        Assert.Equal(2, home.Store.GetVersions(first.Id).Count);
        home.Store.SaveProgress(first.Id, new PlaybackProgress(30, 120));
        await home.Scan(library.Id);
        var rescanned = home.Store.GetMedia(first.Id)!;
        Assert.Equal("Bir Film", rescanned.Title);
        Assert.Equal(first.SourceTitle, rescanned.SourceTitle);
        Assert.Equal(30, rescanned.PositionSeconds);
        Assert.Equal(2, home.Store.GetCatalog("bir film", null, null, 0, 60).Total);
        var unlinked = home.Store.UnmatchMedia(first.Id);
        Assert.Equal(first.SourceTitle, unlinked.Title);
        Assert.Null(unlinked.Content);
        Assert.Equal(30, unlinked.PositionSeconds);
        Assert.Equal(1, home.Store.GetMedia(second.Id)!.Content!.VersionCount);
    }

    [Fact]
    public async Task EpisodeIdentitiesSeparateEpisodesButShareTheShow()
    {
        using var home = new TestWorkspace();
        home.Write("Show.S01E01.mp4"); home.Write("Show.S01E02.mp4");
        var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        var show = Movie with { Provider = "tvmaze", Kind = "show", Title = "Bir Dizi" };
        var episode = new ProviderEpisode(11, 1, 1, "Birinci bölüm", "Özet", "2020-01-01", null);
        Assert.Throws<DomainException>(() => home.Store.MatchMedia(files[0].Id, show, null, ""));
        var first = home.Store.MatchMedia(files[0].Id, show, episode, "");
        var second = home.Store.MatchMedia(files[1].Id, show, episode with { ProviderId = 12, EpisodeNumber = 2 }, "");
        Assert.Equal(first.Content!.Id, second.Content!.Id);
        Assert.Single(home.Store.GetVersions(first.Id));
        Assert.EndsWith("S01E02", second.Title);
    }

    [Theory]
    [InlineData("Sherlock.S01E02.1080p.WEB-DL.mkv", "Sherlock", null, 1, 2)]
    [InlineData("The_Show_S00E03_720p.mp4", "The Show", null, 0, 3)]
    [InlineData("A.Film.2020.2160p.mkv", "A Film", 2020, null, null)]
    public void FilenameHintsAreSuggestionsWithoutAssigningIdentity(string filename, string query, int? year, int? season, int? episode)
    {
        var hint = FilenameHints.Parse(filename);
        Assert.Equal(query, hint.Query); Assert.Equal(year, hint.Year);
        Assert.Equal(season, hint.SeasonNumber); Assert.Equal(episode, hint.EpisodeNumber);
    }

    [Fact]
    public void TokensAreProtectedAndPublicSettingsNeverReturnThem()
    {
        using var home = new TestWorkspace();
        var settings = new MetadataSettings(home.Store, new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider());
        const string secret = "this-is-a-private-test-token-12345";
        var status = settings.Save(new MetadataPreferences(secret));
        Assert.True(status.TmdbConfigured);
        Assert.DoesNotContain(secret, home.Store.GetSetting("metadata.tmdb.token")!);
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(status));
        Assert.Equal(secret, settings.Token);
        settings.Save(new MetadataPreferences(null, true));
        Assert.False(settings.Status.TmdbConfigured);
    }

    [Theory]
    [InlineData("http://static.tvmaze.com/a.jpg", false)]
    [InlineData("https://static.tvmaze.com.attacker.example/a.jpg", false)]
    [InlineData("https://127.0.0.1/a.jpg", false)]
    [InlineData("https://static.tvmaze.com:444/a.jpg", false)]
    [InlineData("https://static.tvmaze.com/uploads/a.jpg", true)]
    [InlineData("https://image.tmdb.org/t/p/w500/a.jpg", true)]
    public void ArtworkOriginsAreExplicit(string url, bool allowed) => Assert.Equal(allowed, MetadataProvider.AllowedImage(url));
}
