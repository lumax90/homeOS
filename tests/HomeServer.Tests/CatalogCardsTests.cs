using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Tests;

public sealed class CatalogCardsTests
{
    [Fact]
    public async Task ALongSeriesIsOneCardBeforePaginationAndItsFilesKeepTheirIdentity()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("ISOLATED catalogue owner","preserved"));
        home.Write("ORIGINAL Movie One.mp4");home.Write("ORIGINAL Movie Two.mp4");var library=home.AddLibrary();await home.Scan(library.Id);
        for(var number=1;number<=80;number++)home.Write($"ORIGINAL Isolated Series.S01E{number:D2}.mkv");await home.Scan(library.Id);
        var actor=home.Store.GetAccount("1")!;var original=home.Store.GetCatalog(null,null,null,0,120).Items;
        var cards=home.Store.GetCatalogCards(null,null,null,0,6,actor);
        Assert.Equal(3,cards.Total);Assert.Equal(82,cards.FileTotal);Assert.Equal(3,cards.Items.Count);
        var series=Assert.Single(cards.Items,item=>item.CatalogGroup?.Kind=="show");Assert.Equal(80,series.CatalogGroup!.FileCount);Assert.Equal(80,series.CatalogGroup.EpisodeCount);Assert.Equal(1,series.CatalogGroup.SeasonCount);
        var page=home.Store.GetLibrarySeries(series.Id,1,0,120,actor);Assert.Equal(80,page.Total);Assert.Equal(new[]{1},page.Seasons);
        Assert.Equal(Enumerable.Range(1,80),page.Items.Select(item=>item.Episode!.Value));
        Assert.All(page.Items,item=>Assert.Contains(original,existing=>existing.Id==item.File.Id&&existing.RelativePath==item.File.RelativePath));
        Assert.Null(home.Store.GetMedia(series.Id)!.CatalogGroup);
        var first=home.Store.GetCatalogCards(null,null,null,0,1,actor);var next=home.Store.GetCatalogCards(null,null,null,1,2,actor);
        Assert.Equal(3,first.Total);Assert.Equal(2,next.Items.Count);Assert.DoesNotContain(next.Items,item=>item.CatalogGroup!.Key==first.Items[0].CatalogGroup!.Key);
    }
    [Fact]
    public void LocalIdentityRequiresActualEpisodeEvidenceAndKeepsRemakeYearsSeparate()
    {
        Assert.Null(CatalogSeriesIdentity.Local("ORIGINAL movie (2024).mp4",null));
        Assert.Null(CatalogSeriesIdentity.Local("S01E01.mkv",null));
        var first=CatalogSeriesIdentity.Local("ORIGINAL Series (2023).S01E01.mkv",null)!;
        var second=CatalogSeriesIdentity.Local("ORIGINAL Series (2023).S02E20.mkv",null)!;
        var remake=CatalogSeriesIdentity.Local("ORIGINAL Series (2024).S01E01.mkv",null)!;
        Assert.Equal(first.Key,second.Key);Assert.NotEqual(first.Key,remake.Key);
        var manual=CatalogSeriesIdentity.Local("episode.mkv","""{"showTitle":"ORIGINAL manual series","season":2,"episode":3,"year":null}""")!;
        Assert.Equal("ORIGINAL manual series",manual.Title);Assert.Equal(2,manual.Season);Assert.Equal(3,manual.Episode);
    }
    [Fact]
    public async Task ContinueWatchingSelectsOnlyTheLatestEpisodeOfALocalSeriesWithoutDeletingProgress()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("ISOLATED progress owner","preserved"));
        home.Write("ORIGINAL Series.S01E01.mp4");home.Write("ORIGINAL Series.S01E02.mp4");var library=home.AddLibrary();await home.Scan(library.Id);
        var files=home.Store.GetCatalog(null,null,null,0,60).Items.OrderBy(item=>item.Title).ToArray();
        home.Store.SaveProgress(files[0].Id,new(20,100));home.Store.SaveProgress(files[1].Id,new(30,100));
        var continuing=home.Store.GetContinueWatching(home.Store.GetAccount("1"),groupSeries:true);
        Assert.Equal(files[1].Id,Assert.Single(continuing).Id);Assert.Equal(20,home.Store.GetMedia(files[0].Id)!.PositionSeconds);Assert.Equal(30,home.Store.GetMedia(files[1].Id)!.PositionSeconds);
    }
}
