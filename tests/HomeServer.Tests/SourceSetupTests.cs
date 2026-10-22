using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class SourceSetupTests
{
    private const string Password="IsolatedSourceSetup2026!";
    private static string Id()=>Guid.NewGuid().ToString("N");
    [Fact]
    public async Task GuidedSetupStaysOutOfSourcesUntilTestedAndConnectIsIdempotent()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home);using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/source-catalog")).StatusCode);await SignIn(client);
        var catalog=(await client.GetFromJsonAsync<SourceProvider[]>("/api/source-catalog"))!;Assert.Equal(12,catalog.Length);Assert.Equal(6,catalog.Count(value=>value.Protocol=="torznab"));Assert.Contains(catalog,value=>value.Id=="nzbgeek"&&value.RequiresApiKey);Assert.Contains(catalog,value=>value.Id=="yts"&&!value.RequiresApiKey);
        var input=new SourceSetupInput(Id(),"yts");var preview=await Test(client,input);Assert.Equal("ready",preview.Status);Assert.Null(preview.IndexerId);Assert.Equal("https://movies-api.accel.li/",preview.Endpoint);
        Assert.Empty((await client.GetFromJsonAsync<IndexerInfo[]>("/api/indexers"))!);
        var store=app.Services.GetRequiredService<MediaStore>();var draft=store.GetSourceSetup(input.CommandId)!;
        Assert.False(store.GetIndexer(draft.IndexerId!).Info.Enabled);Assert.Throws<DomainException>(()=>store.SaveIndexer(draft.IndexerId,new("Bypass","https://fixture.invalid/api","torznab",null),"protected"));
        using var other=app.CreateClient();await Login(other);
        Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync($"/api/source-setups/{input.CommandId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await other.PostAsJsonAsync("/api/source-setups/connect",new SourceSetupConnect(preview.Id,preview.Revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/source-setups/connect",new SourceSetupConnect(preview.Id,preview.Revision+1))).StatusCode);
        var first=await Connect(client,preview);var repeated=await Connect(client,preview);Assert.Equal(first.Id,repeated.Id);Assert.Equal("yts",first.ProviderId);Assert.True(first.Enabled);Assert.Equal("torznab",first.Protocol);
        Assert.Single((await client.GetFromJsonAsync<IndexerInfo[]>("/api/indexers"))!);Assert.Empty(store.GetDownloads());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/source-setups/test",input with{Name="Different intent"})).StatusCode);ForeignKeys(home);
        var indexers=app.Services.GetRequiredService<IndexerService>();var legacy=indexers.Save(first.Id,new(first.Name,first.Endpoint,first.Protocol,"isolated-unused-key"));
        var repaired=await Test(client,new(Id(),"yts",ExistingIndexerId:legacy.Id,ExpectedRevision:legacy.Revision));Assert.Equal("ready",repaired.Status);Assert.Equal(first.Id,(await Connect(client,repaired)).Id);Assert.Equal("",indexers.ReadKey(first.Id));
    }
    [Fact]
    public async Task NewznabTestsCredentialsBeyondPublicCapsAndEditsPreserveTheOriginalSourceIdentity()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home);using var client=app.CreateClient();await SignIn(client);
        var bad=await Test(client,new(Id(),"nzbgeek",ApiKey:"isolated-bad-key"));Assert.Equal("failed",bad.Status);Assert.Empty((await client.GetFromJsonAsync<IndexerInfo[]>("/api/indexers"))!);
        var good=await Test(client,new(Id(),"nzbgeek",ApiKey:"isolated-good-key",Network:new(Cookie:"isolated-cookie",Headers:new(){["X-Isolated"]="isolated-header"},ProxyPassword:"isolated-proxy-password")));
        Assert.Equal("ready",good.Status);Assert.True(good.NeedsUsenetServer);var source=await Connect(client,good);
        var publicEdit=await client.GetStringAsync($"/api/indexers/{source.Id}/setup");foreach(var secret in new[]{"isolated-good-key","isolated-cookie","isolated-header","isolated-proxy-password"})Assert.DoesNotContain(secret,publicEdit);
        var edited=await Test(client,new(Id(),"nzbgeek",Name:"Isolated renamed",ExistingIndexerId:source.Id,ExpectedRevision:source.Revision));
        Assert.Equal("ready",edited.Status);Assert.Equal(source.Name,Assert.Single((await client.GetFromJsonAsync<IndexerInfo[]>("/api/indexers"))!).Name);
        var updated=await Connect(client,edited);Assert.Equal(source.Id,updated.Id);Assert.Equal("Isolated renamed",updated.Name);Assert.True(updated.Revision>source.Revision);
        var count=app.Handler.Requests.Count;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/source-setups/test",new SourceSetupInput(Id(),"nzbgeek",Endpoint:"https://other.invalid/api",ExistingIndexerId:updated.Id,ExpectedRevision:updated.Revision))).StatusCode);
        Assert.Equal(count,app.Handler.Requests.Count);Assert.Empty(app.Services.GetRequiredService<MediaStore>().GetDownloads());ForeignKeys(home);
    }
    [Fact]
    public async Task CancelledTestsCannotPublishAndLogoutRevokesReadySetup()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home);using var client=app.CreateClient();await SignIn(client);
        app.Handler.Block=true;var input=new SourceSetupInput(Id(),"yts");var pending=client.PostAsJsonAsync("/api/source-setups/test",input);await app.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await client.DeleteAsync($"/api/source-setups/{input.CommandId}")).EnsureSuccessStatusCode();await app.Handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));using var cancelled=await pending;
        Assert.Equal("cancelled",(await cancelled.Content.ReadFromJsonAsync<SourceSetupPreview>())!.Status);Assert.Empty((await client.GetFromJsonAsync<IndexerInfo[]>("/api/indexers"))!);
        app.Handler.Block=false;var ready=await Test(client,new(Id(),"yts"));Assert.Equal("ready",ready.Status);
        (await client.PostAsync("/api/auth/logout",null)).EnsureSuccessStatusCode();await Login(client);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/source-setups/connect",new SourceSetupConnect(ready.Id,ready.Revision))).StatusCode);
        Assert.Empty(app.Services.GetRequiredService<MediaStore>().GetIndexers());
    }
    [Fact]
    public async Task SetupRevisionRejectsLateEditsAndCursorSourcesContinueFromTheActualLastId()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home);using var client=app.CreateClient();await SignIn(client);
        var connected=await Connect(client,await Test(client,new(Id(),"torrents-csv")));var store=app.Services.GetRequiredService<MediaStore>();
        var movie=new ProviderContent("tmdb",7700,"movie","Isolated Movie","Isolated Movie","","2020-01-01",null,null,"https://www.themoviedb.org/movie/7700",[]);
        var target=store.RequestContent(movie,"all","balanced");var indexers=app.Services.GetRequiredService<IndexerService>();
        var first=await indexers.SearchAsync(new(target.Id),CancellationToken.None);Assert.Single(first.Items);Assert.True(first.Sources[0].MoreAvailable);
        var second=await indexers.SearchAsync(new(target.Id,Page:1),CancellationToken.None);Assert.Single(second.Items);
        Assert.Contains(app.Handler.Requests,address=>QueryHelpers.ParseQuery(address.Query).GetValueOrDefault("after")=="501");Assert.NotEqual(first.Items[0].Fingerprint,second.Items[0].Fingerprint);
        var edit=await Test(client,new(Id(),"torrents-csv",Name:"Old proposal",ExistingIndexerId:connected.Id,ExpectedRevision:connected.Revision));
        indexers.Save(connected.Id,new("Newer setting",connected.Endpoint,connected.Protocol,null));
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/source-setups/connect",new SourceSetupConnect(edit.Id,edit.Revision))).StatusCode);
        Assert.Equal("Newer setting",store.GetIndexer(connected.Id).Info.Name);Assert.Empty(store.GetDownloads());
    }
    [Fact]
    public void InterruptedSetupRequiresRetestAndExpiryOnlyRemovesTemporaryConnections()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","isolated"));var user=home.Store.GetAccountSecret("1")!;var login=home.Store.CreateLoginSession("1",user.Version,"Isolated setup");
        var permanent=home.Store.SaveIndexer(null,new("Existing","https://fixture.invalid/api","torznab",null),"preserved");
        var work=home.Store.BeginSourceSetup(new(Id(),"yts"),new("Temporary","https://fixture.invalid/","torznab",null),"protected",null,"protected-network","definition","request","1",login.Id);
        home.Store.RecoverSourceSetups();Assert.Equal("failed",home.Store.GetSourceSetup(work.Id)!.Status);Assert.Throws<OperationCanceledException>(()=>home.Store.CompleteSourceSetup(work));
        using(var db=new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}")){db.Open();using var command=db.CreateCommand();command.CommandText="UPDATE source_setups SET expires_at='2000-01-01T00:00:00.0000000+00:00' WHERE id=$id";command.Parameters.AddWithValue("$id",work.Id);command.ExecuteNonQuery();}
        home.Store.PruneSourceSetups();Assert.Null(home.Store.GetSourceSetup(work.Id));Assert.Equal(permanent.Id,Assert.Single(home.Store.GetIndexers()).Id);Assert.Throws<DomainException>(()=>home.Store.GetIndexer(work.IndexerId!));ForeignKeys(home);
    }
    private static async Task<SourceSetupPreview> Test(HttpClient client,SourceSetupInput input)
    {
        using var response=await client.PostAsJsonAsync("/api/source-setups/test",input);response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<SourceSetupPreview>())!;
    }
    private static async Task<IndexerInfo> Connect(HttpClient client,SourceSetupPreview preview)
    {
        Assert.True(preview.Status=="ready",preview.Error??preview.Status);
        using var response=await client.PostAsJsonAsync("/api/source-setups/connect",new SourceSetupConnect(preview.Id,preview.Revision));response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<IndexerInfo>())!;
    }
    private static async Task SignIn(HttpClient client){await Csrf(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner",Password))).EnsureSuccessStatusCode();await Csrf(client);}
    private static async Task Login(HttpClient client){await Csrf(client);(await client.PostAsJsonAsync("/api/auth/login",new Credentials("Owner",Password))).EnsureSuccessStatusCode();await Csrf(client);}
    private static async Task Csrf(HttpClient client){var session=await client.GetFromJsonAsync<JsonElement>("/api/session");client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());}
    private static void ForeignKeys(TestWorkspace home){using var db=new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}");db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA foreign_key_check";using var rows=command.ExecuteReader();Assert.False(rows.Read());}
    private sealed class Factory(TestWorkspace home):WebApplicationFactory<Program>
    {
        public Handler Handler{get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Home:DataPath"]=home.Paths.Data,["Home:Downloads:LoopbackOnly"]="true",["Home:FfmpegPath"]="absent",["Home:FfprobePath"]="absent"}));
            builder.ConfigureTestServices(services=>services.AddHttpClient("indexer").ConfigurePrimaryHttpMessageHandler(()=>Handler));
        }
    }
    private sealed class Handler:HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests{get;}=new();public bool Block{get;set;}public TaskCompletionSource Started{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Cancelled{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests.Enqueue(request.RequestUri!);
            if(Block){Started.TrySetResult();try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}catch(OperationCanceledException){Cancelled.TrySetResult();throw;}}
            var query=QueryHelpers.ParseQuery(request.RequestUri!.Query);string body;var mime="application/json";
            if(request.RequestUri.Host=="api.nzbgeek.info")
            {
                mime="application/xml";body=query["t"]=="caps"?"""<caps><limits max="100"/><searching><search available="yes" supportedParams="q"/><movie-search available="yes" supportedParams="q,imdbid"/></searching><categories><category id="2000" name="Movies"/></categories></caps>"""
                    :query["apikey"]=="isolated-good-key"?"<rss><channel><title>Isolated source</title></channel></rss>":"<error code=\"100\" description=\"invalid isolated-bad-key\"/>";
            }
            else if(request.RequestUri.Host=="torrents-csv.com")
            {
                var next=query.GetValueOrDefault("after")=="501"?502:501;var hash=next==501?new string('a',40):new string('b',40);
                body=JsonSerializer.Serialize(new{torrents=new[]{new{infohash=hash,name="Isolated.Movie.2020.1080p.WEB-DL.x264",size_bytes=1_000_000_000,seeders=10,created_unix=1700000000,id=next}},next});
            }
            else body=SourceNativeTests.Yts;
            return new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,mime)};
        }
    }
}
