using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Sources;

namespace HomeServer.Tests;

public sealed class EpisodeCatalogTests
{
    private static ProviderContent Show => new("tmdb", 5100, "show", "Isolated Show", "Isolated Show", "", "2020-01-01", null, null, "https://www.themoviedb.org/tv/5100", [], "tt7654321", 5100);
    private static ProviderContent Alias => Show with { Provider = "tvmaze", ProviderId = 5200, ProviderUrl = "https://www.tvmaze.com/shows/5200" };
    private static ProviderEpisode Ep(int id, int number, int season = 1) => new(id, season, number, "Episode " + number, "", "2020-01-01", null, 1);
    private static Account Owner(TestWorkspace home) { home.Store.CreateOwner(new("Owner", "isolated")); return home.Store.GetAccount("1")!; }
    private static ContentEntry Catalog(TestWorkspace home, Account actor, ProviderContent metadata, params ProviderEpisode[] episodes)
    {
        var content = home.Store.SaveContent(metadata, actor); home.Store.RefreshCatalogIdentity(content.Id, content.CatalogRevision, metadata, episodes); return home.Store.GetContent(content.Id)!;
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderEpisodeCanExpandToTwoCanonicalEpisodesWithoutLosingFileResume(bool timed)
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var target = Catalog(home, actor, Show, Ep(101, 1), Ep(102, 2)); var combined = Ep(201, 1) with { Title = "Combined" };
        var source = Catalog(home, actor, Alias, combined); home.Store.RequestContent(Alias, "all", "balanced", actor, 1, 1);
        home.Write("Combined.mp4"); home.Write("Second.mp4"); home.Write("New.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 10).Items; foreach (var file in files) home.Store.SaveProbe(file.Id, new(100, 320, 180, "h264", "aac"), false);
        var pack = files.Single(file => file.RelativePath == "Combined.mp4"); var second = files.Single(file => file.RelativePath == "Second.mp4"); var next = files.Single(file => file.RelativePath == "New.mp4");
        home.Store.MatchMedia(pack.Id, Alias, combined, ""); home.Store.MatchMedia(second.Id, Show, Ep(102, 2), ""); home.Store.SaveProgress(pack.Id, new(75, 100));
        var sourceId = Assert.Single(home.Store.GetEpisodeCatalog(source.Id)).Id; var canonical = home.Store.GetEpisodeCatalog(target.Id);
        var order = home.Store.SaveCatalogEpisodeOrder(source.Id, new("Source digital order", "digital", [new(2, 9, [sourceId])], ExpectedCatalogRevision: home.Store.GetContent(source.Id)!.CatalogRevision), actor);
        var maps = new[] { new CatalogEpisodeMap(sourceId, canonical[0].Id, timed ? 0 : null, timed ? 50 : null), new CatalogEpisodeMap(sourceId, canonical[1].Id, timed ? 50 : null, timed ? 100 : null) };
        var preview = home.Store.PreviewCatalogMerge(target.Id, Alias, maps); Assert.True(preview.CanApply, string.Join(" ", preview.Conflicts));
        var hash = StateFolders.Hash(Path.Combine(home.Source, "Combined.mp4"));
        home.Store.MergeCatalog(target.Id, Alias, new(Alias.Provider, "show", Alias.ProviderId, preview.TargetRevision, preview.SourceRevision, maps), actor);
        var merged = home.Store.GetMedia(pack.Id)!; Assert.Equal(2, merged.Episodes!.Length); Assert.Equal(75, merged.PositionSeconds); Assert.Equal(hash, StateFolders.Hash(Path.Combine(home.Source, "Combined.mp4")));
        Assert.Contains(home.Store.GetRequests(), request => request.ContentId == target.Id && request.ScopeEpisodeCount == 2);
        var movedOrder = Assert.Single(home.Store.GetCatalogEpisodeOrders(target.Id)); Assert.Equal(order.Id, movedOrder.Id); Assert.Equal(canonical.Select(episode => episode.Id), movedOrder.Items[0].EpisodeIds);
        Assert.Equal(timed ? 25 : 0, home.Store.GetMedia(second.Id)!.PositionSeconds);
        var rematched = home.Store.MatchMedia(next.Id, Alias, combined, ""); Assert.Equal(2, rematched.Episodes!.Length); Assert.Equal(target.Id, rematched.Content!.Id);
        Assert.Equal("Episode 1", home.Store.GetEpisodeCatalog(target.Id)[0].Metadata.Title);
        Assert.Throws<DomainException>(() => home.Store.SaveProgress(pack.Id, new(1, 100, ExpectedMatchRevision: merged.MatchRevision - 1)));
    }
    [Fact]
    public async Task ProviderNumberSwapRetainsEpisodeIdsFilesRequestsAndPersonalPosition()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var first = Ep(101, 1); var second = Ep(102, 2); var content = Catalog(home, actor, Show, first, second);
        home.Store.RequestContent(Show, "all", "balanced", actor, 1, 1); home.Store.RequestContent(Show, "all", "balanced", actor, 1, 2);
        var requests = home.Store.GetRequests().ToDictionary(request => request.Episode!.Value, request => request.Id); var ids = home.Store.GetEpisodeCatalog(content.Id).ToDictionary(row => row.Metadata.ProviderId, row => row.Id);
        home.Write("Episode.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var file = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.SaveProbe(file.Id, new(100, 320, 180, "h264", "aac"), false);
        home.Store.MatchMedia(file.Id, Show, first, ""); home.Store.SaveProgress(file.Id, new(37, 100));
        var current = home.Store.GetContent(content.Id)!; var plan = home.Store.CreateEpisodeRefreshPlan(content.Id, current.CatalogRevision, Show, [first with { EpisodeNumber = 2 }, second with { EpisodeNumber = 1 }], actor);
        Assert.True(plan.CanApply); Assert.All(plan.Changes, change => Assert.Equal("renumber", change.Action));
        var input = new EpisodeRepairInput(plan.PlanId, plan.CatalogRevision); var updated = home.Store.ApplyEpisodeRefresh(input, actor);
        Assert.Equal(37, home.Store.GetMedia(file.Id)!.PositionSeconds); Assert.Equal(2, home.Store.GetMedia(file.Id)!.Episodes![0].Number); Assert.Equal(ids[101], home.Store.GetMedia(file.Id)!.Episodes![0].EpisodeId);
        var moved = home.Store.GetRequests(); Assert.Equal(2, moved.Single(request => request.Id == requests[1]).Episode); Assert.Equal(1, moved.Single(request => request.Id == requests[2]).Episode); Assert.DoesNotContain(moved, request => request.Status == "cancelled");
        Assert.Equal(updated.CatalogRevision, home.Store.ApplyEpisodeRefresh(input, actor).CatalogRevision);
    }
    [Fact]
    public async Task ReplacedProviderIdRequiresAChoiceAndRetiresTheOldIncomingIdentity()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var old = Ep(101, 1); var content = Catalog(home, actor, Show, old); var canonical = Assert.Single(home.Store.GetEpisodeCatalog(content.Id));
        var changed = Ep(901, 1); var plan = home.Store.CreateEpisodeRefreshPlan(content.Id, content.CatalogRevision, Show, [changed], actor); Assert.False(plan.CanApply); Assert.True(Assert.Single(plan.Changes).NeedsChoice);
        Assert.Throws<DomainException>(() => home.Store.ApplyEpisodeRefresh(new(plan.PlanId, plan.CatalogRevision), actor));
        var selected = new[] { new EpisodeRepairChoice(changed.ProviderId, canonical.Id) }; Assert.True(home.Store.PreviewEpisodeRefresh(plan.PlanId, actor, selected).CanApply);
        home.Store.ApplyEpisodeRefresh(new(plan.PlanId, plan.CatalogRevision, selected), actor); Assert.Equal(canonical.Id, Assert.Single(home.Store.GetEpisodeCatalog(content.Id)).Id);
        home.Write("Stale.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var file = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        Assert.Throws<DomainException>(() => home.Store.MatchMedia(file.Id, Show, old, "")); Assert.Null(home.Store.GetMedia(file.Id)!.Content);
        Assert.NotNull(home.Store.MatchMedia(file.Id, Show, changed, "").Content);
    }
    [Fact]
    public async Task NewEpisodeAtTheOldNumberKeepsRetiredFileAndRequestIdentity()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var old = Ep(101, 1); var content = Catalog(home, actor, Show, old); home.Store.RequestContent(Show, "all", "balanced", actor, 1, 1);
        home.Write("Historical.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var file = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.MatchMedia(file.Id, Show, old, "");
        var oldId = home.Store.GetMedia(file.Id)!.Episodes![0].EpisodeId; var now = home.Store.GetContent(content.Id)!;
        var plan = home.Store.CreateEpisodeRefreshPlan(content.Id, now.CatalogRevision, Show, [Ep(901, 1)], actor);
        home.Store.ApplyEpisodeRefresh(new(plan.PlanId, plan.CatalogRevision, [new(901, null)]), actor);
        Assert.Equal(2, home.Store.GetEpisodeCatalog(content.Id, true).Count); Assert.NotEqual(oldId, Assert.Single(home.Store.GetEpisodeCatalog(content.Id)).Id);
        Assert.Equal(oldId, home.Store.GetMedia(file.Id)!.Episodes![0].EpisodeId); Assert.Equal(1, Assert.Single(home.Store.GetRequests()).ScopeEpisodeCount); Assert.True(File.Exists(Path.Combine(home.Source, "Historical.mp4")));
    }
    [Fact]
    public void AlternativeOrderDrivesResolutionEvaluationAndTheFrozenAcquisitionSnapshot()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var content = Catalog(home, actor, Show, Ep(101, 1), Ep(102, 2)); var episodes = home.Store.GetEpisodeCatalog(content.Id);
        var order = home.Store.SaveCatalogEpisodeOrder(content.Id, new("DVD fixture", "dvd", [new(2, 10, [episodes[0].Id]), new(2, 11, [episodes[1].Id])], ExpectedCatalogRevision: content.CatalogRevision), actor);
        var numbering = home.Store.SelectCatalogEpisodeOrder(content.Id, new(order.Id, 0), actor); Assert.Equal("alternative", numbering.Mode);
        Assert.Equal(1, Assert.Single(home.Store.LocateEpisodes(content.Id, new("alternative", 2, 10)).Episodes).EpisodeNumber);
        var identity = home.Store.ReleaseIdentity(content.Id, 1, 1)!; Assert.Equal(2, identity.SourceSeason); Assert.Equal(10, identity.SourceEpisode);
        var raw = new RawRelease("fixture", "Isolated.Show.S02E10E11.1080p.WEB-DL", "torrent", 1000, 10, null, "", null, []); var rules = home.Store.GetQualityProfile("balanced");
        var evaluation = ReleaseEvaluator.Evaluate(raw, Show, rules.Rules, 1, 1, identity); Assert.True(evaluation.Eligible, string.Join(" ", evaluation.Rejections));
        Assert.NotEmpty(ReleaseEvaluator.IdentityRejections(raw with { Title = "Isolated.Show.S01E01.1080p" }, Show, 1, 1, identity));
        var indexer = home.Store.SaveIndexer(null, new("Fixture", "https://fixture.invalid/api", "torznab", null), "protected");
        var result = home.Store.SaveRelease(indexer, home.Store.GetContent(content.Id)!, new(content.Id, 1, 1), rules, raw, evaluation, "protected");
        var download = home.Store.AddDownload(["order-fixture"], raw.Title, "torrent", "protected", 1000, new(content.Id, 1, 1, result.Id));
        var work = home.Store.GetAcquisition(download.Id)!;
        home.Store.SelectCatalogEpisodeOrder(content.Id, new(null, numbering.Revision), actor);
        Assert.Equal(new[] { 1, 2 }, home.Store.ResolveAcquisitionNumbers(work, evaluation.Parsed).Select(episode => episode.Episode));
        Assert.Throws<DomainException>(() => home.Store.SaveCatalogEpisodeOrder(content.Id, new("Stale", "dvd", order.Items, order.Id, order.Revision, content.CatalogRevision), actor));
    }
    [Fact]
    public void RetiredOrderEntriesBecomeExplicitlyInvalidAndCanBeDeselected()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var content = Catalog(home, actor, Show, Ep(101, 1), Ep(102, 2)); var episodes = home.Store.GetEpisodeCatalog(content.Id);
        var order = home.Store.SaveCatalogEpisodeOrder(content.Id, new("Digital fixture", "digital", [new(1, 1, [episodes[0].Id]), new(1, 2, [episodes[1].Id])], ExpectedCatalogRevision: content.CatalogRevision), actor);
        home.Store.SelectCatalogEpisodeOrder(content.Id, new(order.Id, 0), actor); content = home.Store.GetContent(content.Id)!;
        var plan = home.Store.CreateEpisodeRefreshPlan(content.Id, content.CatalogRevision, Show, [Ep(101, 1)], actor); home.Store.ApplyEpisodeRefresh(new(plan.PlanId, plan.CatalogRevision), actor);
        var numbering = home.Store.GetEpisodeNumbering(content.Id); Assert.False(numbering.Valid); Assert.Throws<DomainException>(() => home.Store.ReleaseIdentity(content.Id, 1, 1));
        Assert.Equal("seasonal", home.Store.SelectCatalogEpisodeOrder(content.Id, new(null, numbering.Revision), actor).Mode);
    }
    [Fact]
    public void StalePlansAndCrossSeriesOrderTargetsAreRejectedWithoutChangingTheCatalog()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var content = Catalog(home, actor, Show, Ep(101, 1)); var other = Catalog(home, actor, Show with { ProviderId = 6100, ImdbId = "tt6100000" }, Ep(601, 1));
        var foreign = Assert.Single(home.Store.GetEpisodeCatalog(other.Id)).Id;
        Assert.Throws<DomainException>(() => home.Store.SaveCatalogEpisodeOrder(content.Id, new("Invalid", "dvd", [new(1, 1, [foreign])], ExpectedCatalogRevision: content.CatalogRevision), actor));
        var plan = home.Store.CreateEpisodeRefreshPlan(content.Id, content.CatalogRevision, Show, [Ep(101, 2)], actor);
        home.Store.SaveCatalogNames(content.Id, ["New local name"]);
        Assert.Throws<DomainException>(() => home.Store.ApplyEpisodeRefresh(new(plan.PlanId, plan.CatalogRevision), actor)); Assert.Equal(1, Assert.Single(home.Store.GetEpisodeCatalog(content.Id)).Metadata.EpisodeNumber);
        var empty = home.Store.CreateEpisodeRefreshPlan(content.Id, home.Store.GetContent(content.Id)!.CatalogRevision, Show, [], actor); Assert.False(empty.CanApply);
        var member = home.Store.SaveAccount(null, new("Member"), "isolated"); Assert.Equal(403, Assert.Throws<DomainException>(() => home.Store.PreviewEpisodeRefresh(plan.PlanId, member)).StatusCode);
    }
    [Fact]
    public void NestedAliasMergeCannotIntroduceTwoConflictingIdsFromOneProvider()
    {
        using var home = new TestWorkspace(); var actor = Owner(home); var target = home.Store.SaveContent(Show, actor); var source = home.Store.SaveContent(Alias, actor);
        var third = Show with { ProviderId = 5300, ProviderUrl = "https://www.themoviedb.org/tv/5300" };
        var link = home.Store.PreviewCatalogMerge(source.Id, third); Assert.True(link.CanApply);
        home.Store.MergeCatalog(source.Id, third, new(third.Provider, third.Kind, third.ProviderId, link.TargetRevision, link.SourceRevision), actor);
        Assert.False(home.Store.PreviewCatalogMerge(target.Id, Alias).CanApply); Assert.Single(home.Store.GetCatalogAliases(target.Id));
    }
    [Fact]
    public void ChapterAndSignalDetectionNeverTreatEqualDivisionAsEvidence()
    {
        BoundaryEpisode[] episodes = [new("one", 1, 1, "One", null), new("two", 1, 2, "Two", null)];
        var none = EpisodeBoundaryDetector.Detect(episodes, 100, [], [], [], true); Assert.False(none.CanApply); Assert.Empty(none.Segments);
        var chapter = EpisodeBoundaryDetector.Detect(episodes, 100, [new(0, 0, 40, "S01E01"), new(1, 40, 100, "S01E02")], [], [], true);
        Assert.True(chapter.CanApply); Assert.Equal(40, chapter.Segments[1].StartSeconds);
        var ambiguous = EpisodeBoundaryDetector.Detect(episodes, 100, [], [new(48, 49), new(50, 51)], [new(47, 52)], true); Assert.False(ambiguous.CanApply);
        var unique = EpisodeBoundaryDetector.Detect(episodes, 100, [], [new(48, 49)], [new(47, 50)], true); Assert.True(unique.CanApply); Assert.Equal(49, unique.Segments[1].StartSeconds);
    }
}
