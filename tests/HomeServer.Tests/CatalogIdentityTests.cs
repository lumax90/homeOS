using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Tests;

public sealed class CatalogIdentityTests
{
    private static ProviderContent Target => new("tmdb", 9001, "show", "Fixture Canonical", "Fixture Canonical", "", "2020-01-01", null, null, "https://www.themoviedb.org/tv/9001", [], "tt1234500", 7001, MinimumAge: 13);
    private static ProviderContent Source => new("tvmaze", 8001, "show", "Fixture Alias", "Fixture Alias", "", "2020-01-01", null, null, "https://www.tvmaze.com/shows/8001", [], "tt1234500", 7001);
    private static ProviderEpisode Episode(int id, int number = 1) => new(id, 1, number, "Fixture Episode " + number, "", "2020-01-0" + number, null);
    [Fact]
    public async Task MergePreservesFilesLatestPersonalHistoryRequestsCollectionsAndAliases()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var target = home.Store.RequestContent(Target, "all", "balanced"); var source = home.Store.RequestContent(Source, "all", "balanced");
        home.Store.RefreshCatalogIdentity(target.Id, target.CatalogRevision, Target, [Episode(101), Episode(102, 2)]);
        home.Store.RefreshCatalogIdentity(source.Id, source.CatalogRevision, Source, [Episode(201), Episode(202, 2)]);
        home.Write("Canonical.mp4"); home.Write("Alias.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 60).Items;
        var a = files.Single(file => file.RelativePath == "Canonical.mp4"); var b = files.Single(file => file.RelativePath == "Alias.mp4");
        foreach (var file in files) home.Store.SaveProbe(file.Id, new ProbeResult(100, 1920, 1080, "h264", "aac"), false);
        home.Store.MatchMedia(a.Id, Target, Episode(101), "Main"); home.Store.MatchMedia(b.Id, Source, Episode(201), "Alias");
        home.Store.SaveProgress(a.Id, new(10, 100)); home.Store.SaveProgress(b.Id, new(35, 100));
        home.Store.SetPersonalContent(target.Id, owner, new(false, 8)); home.Store.SetPersonalContent(source.Id, owner, new(true, 4));
        var collection = home.Store.SaveCollection(null, new("Fixture list"), owner); home.Store.ChangeCollectionItem(collection.Id, source.Id, owner, false);
        home.Store.SetContentPresentation(target.Id, new(Title: "Locked target", Tags: ["main"])); home.Store.SetContentPresentation(source.Id, new(MinimumAge: 18, Tags: ["other"]));
        var preview = home.Store.PreviewCatalogMerge(target.Id, Source); Assert.True(preview.CanApply, string.Join(" ", preview.Conflicts)); Assert.All(preview.Episodes, ep => Assert.True(ep.Automatic));
        var merged = home.Store.MergeCatalog(target.Id, Source, new(Source.Provider, Source.Kind, Source.ProviderId, preview.TargetRevision, preview.SourceRevision), owner);
        Assert.Equal(target.Id, merged.Id); Assert.Equal(target.Id, home.Store.GetContent(source.Id)!.Id); Assert.Equal(target.Id, home.Store.FindContent("tvmaze", "show", Source.ProviderId)!.Id);
        Assert.Equal("Locked target", merged.Metadata.Title); Assert.Equal(18, merged.Metadata.MinimumAge);
        Assert.Equal(35, home.Store.GetMedia(a.Id)!.PositionSeconds); Assert.Equal(35, home.Store.GetMedia(b.Id)!.PositionSeconds);
        Assert.True(home.Store.GetPersonalContent(target.Id, owner).Favorite); Assert.Equal(8, home.Store.GetPersonalContent(target.Id, owner).Rating);
        Assert.Equal(target.Id, Assert.Single(home.Store.GetCollectionItems(collection.Id, owner)).Id);
        Assert.Single(home.Store.GetTracked()); Assert.Equal(2, home.Store.GetContentFiles(target.Id).Count);
        Assert.Single(home.Store.GetRequests(), request => request.Status is "approved" or "fulfilled");
        Assert.Contains(home.Store.GetRequests(), request => request.Status == "cancelled" && request.Note.Contains("birleştirildi"));
        var revision = home.Store.GetMedia(b.Id)!.MatchRevision;
        home.Store.MatchMedia(b.Id, Source with { Title = "Refreshed alias" }, Episode(201), "Alias", revision);
        Assert.Equal("Locked target", home.Store.GetContent(target.Id)!.Metadata.Title); Assert.Equal(target.Id, home.Store.GetMedia(b.Id)!.Content!.Id);
        Assert.Contains(home.Store.GetCatalogAliases(target.Id), alias => alias.Title == "Refreshed alias");
    }
    [Fact]
    public void ConflictingIdentityAndStalePreviewCannotMutateTheCatalog()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var target = home.Store.SaveContent(Target, owner); var conflicting = Source with { ImdbId = "tt9999999" };
        var preview = home.Store.PreviewCatalogMerge(target.Id, conflicting); Assert.False(preview.CanApply); Assert.Contains(preview.Conflicts, value => value.Contains("IMDb"));
        Assert.Throws<DomainException>(() => home.Store.MergeCatalog(target.Id, conflicting, new(Source.Provider, Source.Kind, Source.ProviderId, target.CatalogRevision), owner));
        preview = home.Store.PreviewCatalogMerge(target.Id, Source); Assert.True(preview.CanApply);
        home.Store.SetContentPresentation(target.Id, new(Title: "Changed"));
        Assert.Throws<DomainException>(() => home.Store.MergeCatalog(target.Id, Source, new(Source.Provider, Source.Kind, Source.ProviderId, preview.TargetRevision), owner));
        Assert.Null(home.Store.FindContent("tvmaze", "show", Source.ProviderId));
        preview = home.Store.PreviewCatalogMerge(target.Id, Source with { MinimumAge = 18 });
        home.Store.MergeCatalog(target.Id, Source with { MinimumAge = 18 }, new(Source.Provider, Source.Kind, Source.ProviderId, preview.TargetRevision), owner);
        var young = home.Store.SaveAccount(null, new AccountInput("Young", MaximumAge: 15, AllowUnrated: true), "fixture");
        Assert.Null(home.Store.GetContent(target.Id, young));
    }
    [Fact]
    public async Task ExplicitEpisodeMapKeepsNumberingDifferencesAndLocalNameAliases()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var owner = home.Store.GetAccount("1")!;
        var target = home.Store.SaveContent(Target, owner); var source = home.Store.SaveContent(Source, owner);
        var sourceEp = Episode(201) with { SeasonNumber = 2, Title = "Different translated title" };
        home.Store.RefreshCatalogIdentity(target.Id, target.CatalogRevision, Target, [Episode(101)]); home.Store.RefreshCatalogIdentity(source.Id, source.CatalogRevision, Source, [sourceEp]);
        home.Write("Alias.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var file = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); home.Store.MatchMedia(file.Id, Source, sourceEp, "");
        var first = home.Store.PreviewCatalogMerge(target.Id, Source); Assert.False(first.CanApply);
        var oldEp = Assert.Single(home.Store.GetEpisodes(source.Id)); var newEp = Assert.Single(home.Store.GetEpisodes(target.Id));
        var maps = new[] { new CatalogEpisodeMap(oldEp.Id, newEp.Id) }; var preview = home.Store.PreviewCatalogMerge(target.Id, Source, maps); Assert.True(preview.CanApply);
        home.Store.MergeCatalog(target.Id, Source, new(Source.Provider, Source.Kind, Source.ProviderId, preview.TargetRevision, preview.SourceRevision, maps), owner);
        Assert.Equal(1, home.Store.GetMedia(file.Id)!.Content!.SeasonNumber);
        home.Store.SaveCatalogNames(target.Id, ["Fixture local title"]); home.Store.SaveCatalogNames(target.Id, ["Provider alias"], "tmdb");
        Assert.Contains("Fixture local title", home.Store.GetCatalogNames(target.Id));
        var metadata = home.Store.GetContent(target.Id)!.Metadata;
        Assert.True(HomeServer.Sources.ReleaseEvaluator.TitleMatches("Fixture.local.title.S01E01.1080p", metadata));
        Assert.True(HomeServer.Sources.ReleaseEvaluator.TitleMatches("Provider.alias.S01E01.1080p", metadata));
        Assert.Equal(1, home.Store.GetCatalog("Fixture local title", "video", null, 0, 60).Total);
        home.Store.SaveCatalogNames(target.Id, ["Fixture local title", "Provider alias"]);
        home.Store.SaveCatalogNames(target.Id, [], "tmdb");
        Assert.Contains("Provider alias", home.Store.GetCatalogNames(target.Id, localOnly: true));
        Assert.Equal(target.Id, home.Store.RequestContent(Source, "all", "balanced", owner).Id);
    }
}
