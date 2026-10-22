using System.IO.Compression;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;
using HomeServer.Shelves;
using HomeServer.Sources;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class DevelopmentBoundaryTests
{
    [Fact]
    public void DeviceCodeRequiresItsSecretAndApprovalAndCanOnlyBeConsumedOnce()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","isolated"));var user=home.Store.GetAccountSecret("1")!;
        var login=home.Store.CreateLoginSession("1",user.Version,"Isolated authorizer");var challenge=home.Store.StartDeviceLink("Isolated receiving device");
        Assert.Null(home.Store.ConsumeDeviceLink(new(challenge.Id,challenge.Secret)));
        Assert.Throws<DomainException>(()=>home.Store.ConsumeDeviceLink(new(challenge.Id,new string('0',64))));
        home.Store.ApproveDeviceLink(challenge.Code,"1",login.Id);Assert.Equal("1",home.Store.ConsumeDeviceLink(new(challenge.Id,challenge.Secret))!.Account.Id);
        Assert.Throws<DomainException>(()=>home.Store.ConsumeDeviceLink(new(challenge.Id,challenge.Secret)));
        var revoked=home.Store.StartDeviceLink("Another isolated device");home.Store.ApproveDeviceLink(revoked.Code,"1",login.Id);home.Store.RevokeLoginSession(login.Id,"1");Assert.Throws<DomainException>(()=>home.Store.ConsumeDeviceLink(new(revoked.Id,revoked.Secret)));
    }
    [Fact]
    public async Task ShelfScanKeepsBytesRestrictsAccountsAndSanitizesEpubResources()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","isolated"));var actor=home.Store.GetAccount("1")!;var path=Path.Combine(home.Source,"Isolated.epub");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            Write("META-INF/container.xml","<container><rootfiles><rootfile full-path='book/package.opf'/></rootfiles></container>");
            Write("book/package.opf","<package><metadata><title>Isolated book</title></metadata><manifest><item id='one' href='one.xhtml'/></manifest><spine><itemref idref='one'/></spine></package>");
            Write("book/one.xhtml","<html><body><h1>Readable fixture</h1><script>BAD_SCRIPT()</script><img src='https://outside.invalid/private.png'/><p onclick='BAD_EVENT()'>Safe text</p><iframe src='https://outside.invalid'/></body></html>");
            void Write(string name,string content){using var output=new StreamWriter(zip.CreateEntry(name).Open());output.Write(content);}
        }
        var hash=StateFolders.Hash(path);var library=home.Store.AddLibrary(new("Books",home.Source,"books"));await home.Scan(library.Id);
        var asset=Assert.Single(home.Store.ShelfAssets(actor,null,"books",null,null,0).Items);Assert.Equal(1,home.Store.GetLibrary(library.Id)!.ItemCount);
        var books=new BookService(home.Store);Assert.Equal("Isolated book",books.Info(asset.Id,actor).Title);var html=books.Chapter(asset.Id,0,actor,"");Assert.Contains("Safe text",html);Assert.DoesNotContain("BAD_SCRIPT",html);Assert.DoesNotContain("BAD_EVENT",html);Assert.DoesNotContain("outside.invalid",html);
        var client=Guid.NewGuid().ToString("N");home.Store.SaveShelfProgress(asset.Id,new(asset.Fingerprint,new(Page:3),client,2),actor);home.Store.SaveShelfProgress(asset.Id,new(asset.Fingerprint,new(Page:1),client,1),actor);Assert.Equal(3,home.Store.ShelfProgress(asset.Id,actor).Page);
        var hidden=home.Store.SaveAccount(null,new("Hidden",AllLibraries:false),"isolated");Assert.Throws<DomainException>(()=>home.Store.ShelfSource(asset.Id,hidden));Assert.Equal(hash,StateFolders.Hash(path));
        using var db=new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}");db.Open();using var check=db.CreateCommand();check.CommandText="PRAGMA foreign_key_check";using var rows=check.ExecuteReader();Assert.False(rows.Read());
    }
    [Fact]
    public void SourceRequestBudgetsRollbackFailedReservationsAndAdaptersReturnCanonicalReleases()
    {
        using var home=new TestWorkspace();var source=home.Store.SaveIndexer(null,new("Isolated","https://fixture.invalid/api","torznab",null),"protected");var policy=new SourceNetworkSettings(DailyQueries:1,DailyGrabs:1);
        home.Store.ReserveSourceRequest(source.Id,"query",policy);Assert.Throws<DomainException>(()=>home.Store.ReserveSourceRequest(source.Id,"query",policy));home.Store.ReserveSourceRequest(source.Id,"grab",policy);Assert.Throws<DomainException>(()=>home.Store.ReserveSourceRequest(source.Id,"grab",policy));
        var adapter=new SourceAdapterManifest(1,"isolated.fixture","Isolated adapter","1.0.0","Isolated test fixture","https://fixture.invalid","json","torrent","/search",ItemsPath:"results",Fields:new(){["id"]="id",["title"]="name",["url"]="link",["size"]="size"});SourceAdapterRegistry.Validate(adapter);
        var page=SourceAdapterRegistry.Parse(adapter,Encoding.UTF8.GetBytes("{\"results\":[{\"id\":\"fixture\",\"name\":\"Isolated.Show.S01E01.1080p\",\"link\":\"/one.torrent\",\"size\":12345}]}"),new("https://fixture.invalid/api"),0);
        var release=Assert.Single(page.Items);Assert.Equal("https://fixture.invalid/one.torrent",release.Url);Assert.Equal(12345,release.SizeBytes);
        Assert.False(RemoteMediaHttp.Allowed(System.Net.IPAddress.Loopback,true));Assert.False(RemoteMediaHttp.Allowed(System.Net.IPAddress.Parse("169.254.169.254"),true));Assert.False(RemoteMediaHttp.Allowed(System.Net.IPAddress.Parse("192.168.1.10"),false));Assert.True(RemoteMediaHttp.Allowed(System.Net.IPAddress.Parse("192.168.1.10"),true));
    }
    [Fact]
    public async Task LiveAndLocalPlaybackShareAccountSlotsInBothAdmissionDirections()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","isolated"));var user=home.Store.GetAccountSecret("1")!;var login=home.Store.CreateLoginSession("1",user.Version,"Isolated player");var actor=home.Store.GetPlaybackActor("1",login.Id);
        home.Write("Isolated.mp4");var library=home.AddLibrary();await home.Scan(library.Id);var media=Assert.Single(home.Store.GetCatalog(null,null,null,0,10).Items);home.Store.SaveUserPlaybackLimits("1",new(1,3));
        var source=home.Store.SaveLiveSource(null,new("Isolated TV",PlaylistUrl:"https://fixture.invalid/list.m3u"),"protected",user.Account);var channels=home.Store.SyncLiveChannels(source,[new("fixture","Isolated channel","",null,null,"https://fixture.invalid/live.ts",false,1)],value=>value);
        var local=home.Store.AdmitPlayback(actor,Guid.NewGuid().ToString("N"),1,media.Id,"preparing","fixture",DateTimeOffset.UtcNow);Assert.Throws<DomainException>(()=>home.Store.AdmitLiveSession(channels[0].Id,actor));home.Store.EndPlaybackLease(local.Lease.Id,"isolated",DateTimeOffset.UtcNow);
        var live=home.Store.AdmitLiveSession(channels[0].Id,actor);Assert.Throws<DomainException>(()=>home.Store.AdmitPlayback(actor,Guid.NewGuid().ToString("N"),1,media.Id,"preparing","fixture",DateTimeOffset.UtcNow));home.Store.SetLiveSession(live.Id,"closed");
        var programme=new LiveProgramme(Guid.NewGuid().ToString("N"),channels[0].Id,DateTimeOffset.UtcNow.AddHours(1).ToString("O"),DateTimeOffset.UtcNow.AddHours(2).ToString("O"),"Isolated series","",Guid.NewGuid().ToString("N"),false,null);
        home.Store.SaveLiveGuide(source,[programme]);var rule=home.Store.SaveDvrRule(null,new(channels[0].Id,programme.Title),user.Account);home.Store.ScheduleDvrRules();Assert.Equal("scheduled",Assert.Single(home.Store.DvrRecordings(user.Account)).Status);
        home.Store.SaveDvrRule(rule.Id,new(channels[0].Id,programme.Title,Enabled:false,ExpectedRevision:rule.Revision),user.Account);Assert.Equal("cancelled",Assert.Single(home.Store.DvrRecordings(user.Account)).Status);
    }
}
