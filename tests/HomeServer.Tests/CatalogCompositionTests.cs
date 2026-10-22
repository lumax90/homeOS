using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Sources;

namespace HomeServer.Tests;

public sealed class CatalogCompositionTests
{
    private static ProviderContent Show => new("tmdb", 9100, "show", "Fixture Show", "Fixture Show", "", "2020-01-01", null, null, "https://www.themoviedb.org/tv/9100", []);
    private static ProviderEpisode Ep(int number) => new(91000 + number, 1, number, "Fixture Episode " + number, "", "2020-01-0" + number, null);
    [Theory]
    [InlineData("Fixture.Show.S01E01E02.1080p.mkv", 2, null, null)]
    [InlineData("Fixture.Show.1x01-03.720p.mkv", 3, null, null)]
    [InlineData("Fixture.Show.2020.02.29.1080p.mkv", 0, "2020-02-29", null)]
    [InlineData("[Fixture] Fixture Anime - 012-013v2 [1080p].mkv", 0, null, 12)]
    public void FilenameNumberingIsExplicitAndBounded(string name, int count, string? date, int? absolute)
    {
        var hint = FilenameHints.Parse(name); Assert.False(hint.Ambiguous); Assert.Equal(count, hint.EpisodeNumbers!.Length); Assert.Equal(date, hint.AirDate); Assert.Equal(absolute, hint.AbsoluteNumber);
    }
    [Theory]
    [InlineData("Fixture.S01E01-20000.mkv")]
    [InlineData("Fixture.S01E03-E02.mkv")]
    [InlineData("Fixture.S01E01-S02E01.mkv")]
    [InlineData("Fixture.2020.02.30.mkv")]
    public void AmbiguousEpisodeNamesCannotBecomeAutomaticMatches(string name) => Assert.True(FilenameHints.Parse(name).Ambiguous);
    [Fact]
    public async Task EpisodeMembershipDrivesAvailabilityProgressAndSharedResumeWithoutMarkingFutureEpisodes()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var content = home.Store.RequestContent(Show, "all", "balanced"); home.Store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, Show, [Ep(1), Ep(2)]);
        home.Write("Pack.mp4"); home.Write("Second.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items; var pack = files.Single(file => file.RelativePath == "Pack.mp4"); var second = files.Single(file => file.RelativePath == "Second.mp4");
        home.Store.SaveProbe(pack.Id, new(100, 1920, 1080, "h264", "aac"), false); home.Store.SaveProbe(second.Id, new(50, 1920, 1080, "h264", "aac"), false);
        var matched = home.Store.MatchMediaEpisodes(pack.Id, Show, [(Ep(1), 0d, 50d), (Ep(2), 50d, 100d)], "Double", pack.MatchRevision);
        home.Store.MatchMedia(second.Id, Show, Ep(2), "Single");
        Assert.Equal(0, home.Store.GetContent(content.Id)!.MissingCount); Assert.All(home.Store.GetEpisodes(content.Id), ep => Assert.Equal("available", ep.State));
        home.Store.SaveProgress(pack.Id, new(25, 100, ExpectedMatchRevision: matched.MatchRevision)); Assert.False(home.Store.GetMedia(second.Id)!.IsWatched); Assert.Equal(0, home.Store.GetMedia(second.Id)!.PositionSeconds);
        home.Store.SaveProgress(pack.Id, new(75, 100, ExpectedMatchRevision: matched.MatchRevision)); Assert.Equal(25, home.Store.GetMedia(second.Id)!.PositionSeconds); Assert.Equal(75, home.Store.GetMedia(pack.Id)!.PositionSeconds);
        Assert.Equal(pack.Id, Assert.Single(home.Store.GetContinueWatching()).Id);
        var episodeId = home.Store.GetEpisodes(content.Id).Single(ep => ep.EpisodeNumber == 2).Id;
        Assert.Equal(75, Assert.Single(home.Store.GetVideoSequence(pack.Id, owner, episodeId).Items).PositionSeconds);
        Assert.Equal(2, home.Store.GetVersions(second.Id).Count);
        home.Store.UnmatchMedia(pack.Id); var newMatch = home.Store.MatchMedia(pack.Id, Show, Ep(1), "Single"); Assert.True(newMatch.MatchRevision > matched.MatchRevision);
        Assert.Throws<DomainException>(() => home.Store.SaveProgress(pack.Id, new(40, 100, ExpectedMatchRevision: matched.MatchRevision)));
        Assert.Throws<DomainException>(() => home.Store.AddManagedSubtitle(pack.Id, Guid.NewGuid().ToString("N"), "tr", "Stale fixture", "srt", expectedMatchRevision: matched.MatchRevision));
        Assert.Empty(home.Store.GetManagedSubtitles(pack.Id));
    }
    [Fact]
    public async Task MoviePartsUseOneTimelineAndRequireEveryAuthorizedAvailablePart()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var movie = Show with { Kind = "movie", Title = "Fixture Movie", OriginalTitle = "Fixture Movie" }; var content = home.Store.RequestContent(movie, "all", "balanced");
        home.Write("Part1.mp4"); var firstLibrary = home.AddLibrary(); await home.Scan(firstLibrary.Id);
        var otherRoot = Path.Combine(home.Root, "other"); Directory.CreateDirectory(otherRoot); var secondPath = Path.Combine(otherRoot, "Part2.mp4"); File.WriteAllBytes(secondPath, [1, 2, 3]);
        var secondLibrary = home.Store.AddLibrary(new AddLibrary("Other", otherRoot, "movies")); await home.Scan(secondLibrary.Id);
        var first = Assert.Single(home.Store.GetCatalog(null, null, firstLibrary.Id, 0, 60).Items); var second = Assert.Single(home.Store.GetCatalog(null, null, secondLibrary.Id, 0, 60).Items);
        home.Store.SaveProbe(first.Id, new(40, 1920, 1080, "h264", "aac"), false); home.Store.SaveProbe(second.Id, new(60, 1920, 1080, "h264", "aac"), false);
        home.Store.MatchMedia(first.Id, movie, null, ""); home.Store.MatchMedia(second.Id, movie, null, ""); home.Store.SaveProgress(second.Id, new(10, 60));
        var edition = home.Store.SetMovieParts(content.Id, new("Two parts", [first.Id, second.Id])); Assert.True(edition.Complete);
        var sequence = home.Store.GetVideoSequence(first.Id, owner); Assert.Equal(1, sequence.Index); Assert.Equal(10, sequence.Items[1].PositionSeconds);
        home.Store.SaveProgress(second.Id, new(20, 60)); Assert.Equal(20, home.Store.GetMedia(second.Id)!.PositionSeconds); Assert.Equal(second.Id, Assert.Single(home.Store.GetContinueWatching()).Id);
        var restricted = home.Store.SaveAccount(null, new AccountInput("Restricted", AllLibraries: false, LibraryIds: [firstLibrary.Id]), "fixture");
        Assert.Equal(0, home.Store.GetContent(content.Id, restricted)!.FileCount); Assert.Throws<DomainException>(() => home.Store.GetVideoSequence(first.Id, restricted));
        File.Move(secondPath, secondPath + ".offline"); Assert.False(home.Store.GetMovieEdition(edition.Id)!.Complete); Assert.Throws<DomainException>(() => home.Store.GetVideoSequence(first.Id, owner));
        File.Move(secondPath + ".offline", secondPath); home.Store.SaveProgress(second.Id, new(60, 60)); Assert.True(home.Store.GetMedia(first.Id)!.IsWatched);
        Assert.Throws<DomainException>(() => home.Store.RemoveMovieEdition(edition.Id, edition.Revision + 1));
        home.Store.RemoveMovieEdition(edition.Id, edition.Revision); Assert.Null(home.Store.GetMedia(first.Id)!.Content); Assert.True(File.Exists(secondPath));
    }
    [Fact]
    public async Task UnknownEpisodeBoundariesWaitForTheEndEvenForShortFiles()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); home.Write("Short.mp4"); home.Write("Second.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items; var first = files.Single(file => file.RelativePath == "Short.mp4"); var second = files.Single(file => file.RelativePath == "Second.mp4");
        home.Store.SaveProbe(first.Id, new(.25, 1920, 1080, "h264", "aac"), false); home.Store.SaveProbe(second.Id, new(.1, 1920, 1080, "h264", "aac"), false);
        home.Store.MatchMediaEpisodes(first.Id, Show, [(Ep(1), null, null), (Ep(2), null, null)], "", first.MatchRevision); home.Store.MatchMedia(second.Id, Show, Ep(2), "");
        home.Store.SaveProgress(first.Id, new(.1, .25)); Assert.False(home.Store.GetMedia(second.Id)!.IsWatched);
        home.Store.SaveProgress(first.Id, new(.25, .25)); Assert.True(home.Store.GetMedia(second.Id)!.IsWatched);
    }
    [Fact]
    public void DailyAndAbsoluteNumberingNeverGuessAndRetainAnAcquisitionSnapshot()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var content = home.Store.RequestContent(Show, "all", "balanced"); home.Store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, Show, [Ep(1), Ep(2)]);
        var numbering = home.Store.SaveEpisodeNumbering(content.Id, new("absolute", "user-provided-label", [new(12, 1, 1), new(13, 1, 2)])); Assert.Equal("manual", numbering.Source);
        Assert.Equal(new[] { 1, 2 }, home.Store.LocateEpisodes(content.Id, new("absolute", AbsoluteNumber: 12, EndEpisode: 13)).Episodes.Select(ep => ep.EpisodeNumber));
        Assert.Throws<DomainException>(() => home.Store.LocateEpisodes(content.Id, new("absolute", AbsoluteNumber: 1)));
        var raw = new RawRelease("fixture", "Fixture Show - 012-013 1080p WEB-DL", "torrent", 1000, 10, null, "", null, []);
        var identity = home.Store.ReleaseIdentity(content.Id, 1, 1); var rules = home.Store.GetQualityProfile("balanced"); var evaluation = ReleaseEvaluator.Evaluate(raw, Show, rules.Rules, 1, 1, identity); Assert.True(evaluation.Eligible);
        var indexer = home.Store.SaveIndexer(null, new("Fixture", "https://fixture.invalid/api", "torznab", null), "protected");
        var result = home.Store.SaveRelease(indexer, home.Store.GetContent(content.Id)!, new(content.Id, 1, 1), rules, raw, evaluation, "protected");
        var download = home.Store.AddDownload(["fixture-absolute"], raw.Title, "torrent", "protected", 1000, new(content.Id, 1, 1, result.Id));
        var oldSearchTarget = home.Store.GetContent(content.Id)!;
        home.Store.SaveEpisodeNumbering(content.Id, new("seasonal", "", ExpectedRevision: numbering.Revision));
        var work = home.Store.GetAcquisition(download.Id)!; Assert.Equal("absolute", work.Numbering!.Mode); Assert.Equal(2, home.Store.ResolveAcquisitionNumbers(work, evaluation.Parsed).Length);
        Assert.Throws<DomainException>(() => home.Store.SaveEpisodeNumbering(content.Id, new("daily", "", ExpectedRevision: numbering.Revision)));
        Assert.Equal(1, Assert.Single(home.Store.LocateEpisodes(content.Id, new("daily", AirDate: "2020-01-01")).Episodes).EpisodeNumber);
        var late = home.Store.SaveRelease(indexer, oldSearchTarget, new(content.Id, 1, 1), rules, raw, evaluation, "protected");
        Assert.Throws<DomainException>(() => home.Store.AddDownload(["stale-scope-fixture"], raw.Title, "torrent", "protected", 1000, new(content.Id, 1, 1, late.Id)));
        Assert.Single(home.Store.GetDownloads());
    }
}
