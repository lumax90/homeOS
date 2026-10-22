using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class ProductCatalogTests
{
    private static ProviderContent Show => new("tvmaze",335,"show","Sherlock","Sherlock","Actual provider shape","2010-07-25",null,null,"https://www.tvmaze.com/shows/335",["Drama"]);
    [Fact]
    public void RequestExistsWithoutFilesAndNeverStartsADownload()
    {
        using var home=new TestWorkspace();
        var first=home.Store.RequestContent(Show,"all","balanced");
        var duplicate=home.Store.RequestContent(Show,"all","balanced");
        Assert.Equal(first.Id,duplicate.Id);Assert.Single(home.Store.GetTracked());
        Assert.Equal(0,first.FileCount);Assert.Empty(home.Store.GetDownloads());
        var store=new MediaStore(home.Paths);store.Initialize();
        Assert.Equal(first.Id,Assert.Single(store.GetTracked()).Id);
        store.CancelContentRequest(first.Id);Assert.Empty(store.GetTracked());
        Assert.NotNull(store.GetContent(first.Id));
    }
    [Fact]
    public void StaleTrackingSyncCannotOverwriteNewIntent()
    {
        using var home=new TestWorkspace();
        var entry=home.Store.RequestContent(Show,"all","balanced");
        var work=home.Store.ClaimTrackingSync()!.Value;
        home.Store.UpdateTracking(entry.Id,new TrackingUpdate("none","archive"));
        home.Store.CompleteTrackingSync(entry.Id,work.Revision,Show,[new ProviderEpisode(1,1,1,"Episode","","2010-01-01",null)]);
        Assert.Empty(home.Store.GetEpisodes(entry.Id));
        Assert.Equal("none",home.Store.GetContent(entry.Id)!.MonitorMode);
    }
    [Fact]
    public void FutureMonitoringDoesNotLabelOldEpisodesWanted()
    {
        using var home=new TestWorkspace();var entry=home.Store.RequestContent(Show,"future","balanced");
        var work=home.Store.ClaimTrackingSync()!.Value;
        home.Store.CompleteTrackingSync(entry.Id,work.Revision,Show,[new ProviderEpisode(1,1,1,"Past","","2010-01-01",null),new ProviderEpisode(2,1,2,"Future","",DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),null)]);
        var episodes=home.Store.GetEpisodes(entry.Id);
        Assert.Contains(episodes,e=>e.State=="unmonitored");Assert.Contains(episodes,e=>e.State=="upcoming");
        Assert.Equal(0,home.Store.GetContent(entry.Id)!.MissingCount);
    }
    [Fact]
    public void EpisodePaginationAndRestartRecoveryPreserveScope()
    {
        using var home = new TestWorkspace();
        var entry = home.Store.RequestContent(Show, "all", "balanced");
        var work = home.Store.ClaimTrackingSync()!.Value;
        home.Store.RecoverTrackingSync();
        var recovered = home.Store.ClaimTrackingSync()!.Value;
        Assert.Equal(work.Revision, recovered.Revision);
        home.Store.CompleteTrackingSync(entry.Id, recovered.Revision, Show,
            Enumerable.Range(1, 230).Select(n => new ProviderEpisode(n, n < 210 ? 1 : 2, n, "Episode " + n, "", "2010-01-01", null)).ToArray());
        var page = home.Store.GetEpisodePage(entry.Id, null, null, 1, "missing", 100, 100);
        Assert.Equal(209, page.Total);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(101, page.Items[0].EpisodeNumber);
        Assert.Equal(new[] { 1, 2 }, page.Seasons);
        Assert.Equal(230, home.Store.GetContent(entry.Id)!.MissingCount);
    }
    [Fact]
    public async Task ArchivedLibraryIsExcludedFromCatalogAndPlaybackLookup()
    {
        using var home=new TestWorkspace();home.Write("Actual.mp4");var library=home.AddLibrary();await home.Scan(library.Id);
        var item=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}"))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="UPDATE libraries SET enabled=0 WHERE id=$id";command.Parameters.AddWithValue("$id",library.Id);command.ExecuteNonQuery();
        }
        Assert.Empty(home.Store.GetLibraries());Assert.Empty(home.Store.GetCatalog(null,null,null,0,60).Items);
        Assert.Null(home.Store.GetMedia(item.Id));Assert.Equal(0,home.Store.GetStats().Items);
        Assert.Single(home.Store.GetLibraries(true));Assert.True(File.Exists(Path.Combine(home.Source,"Actual.mp4")));
    }
}
