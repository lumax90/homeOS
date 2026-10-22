using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MusicListTests
{
    private const string Collection = "eeeeeeee-1111-4111-8111-111111111111";
    private static string ArtistId(int index) => $"aaaaaaaa-1111-4111-8111-{index:000000000000}";
    private static Account Owner(TestWorkspace home) { home.Store.CreateOwner(new("Owner", "isolated")); return home.Store.GetAccount("1")!; }
    [Fact]
    public async Task PublicCollectionSurvivesRecoveryAndAppliesOnceWithoutChangingExistingMonitors()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var handler = new Handler(); var provider = new MusicBrainzProvider(new Clients(handler), cache); using var worker = Worker(home.Store, provider);
        var job = home.Store.QueueMusicList(new("collection", Collection, "artist"), owner);
        Assert.True(await worker.RunOnceAsync(CancellationToken.None)); Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        var page = home.Store.GetMusicListJob(job.Id, owner); Assert.Equal(50, page.Cursor); Assert.Empty(home.Store.FollowedMusicArtists(owner.Id));
        var stale = home.Store.ClaimMusicList()!; var recovered = new MediaStore(home.Paths); recovered.Initialize(); recovered.RecoverMusicLists();
        Assert.Throws<OperationCanceledException>(() => recovered.SaveMusicListStep(stale, [], [], 51, 51, complete: true));
        using var resumed = Worker(recovered, provider); Assert.True(await resumed.RunOnceAsync(CancellationToken.None));
        var ready = recovered.GetMusicListJob(job.Id, owner); Assert.Equal("ready", ready.Status); Assert.Equal(51, ready.Items.Length);
        recovered.FollowMusicArtist(ArtistId(0), "Existing name", owner.Id);
        var existing = recovered.SaveMusicMonitor("artist", ArtistId(0), "Existing profile", new(new(Enabled: false, Mode: "all")), owner);
        var input = new MusicListApply(ready.Revision, ready.Items.Select(item => item.Mbid).ToArray(), true); var result = recovered.ApplyMusicList(job.Id, input, owner);
        Assert.Equal(50, result.Followed); Assert.Equal(1, result.AlreadyFollowed); Assert.Equal(50, result.MonitorsCreated); Assert.Equal(1, result.MonitorsPreserved);
        Assert.Equal(result, recovered.ApplyMusicList(job.Id, input, owner)); Assert.Equal(existing.Revision, recovered.GetMusicMonitor(existing.Id)!.Revision); Assert.False(recovered.GetMusicMonitor(existing.Id)!.Settings.Enabled);
        Assert.All(recovered.GetMusicMonitors(owner.Id), monitor => { Assert.False(monitor.Settings.Search); Assert.False(monitor.Settings.AutoGrab); });
        Assert.Empty(recovered.GetMusicAutomationJobs(owner.Id)); Assert.Empty(recovered.GetDownloads()); Assert.Equal(51, recovered.FollowedMusicArtists(owner.Id).Count);
        Assert.All(handler.Urls, url => Assert.StartsWith("https://musicbrainz.org/ws/2/", url));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("short")]
    [InlineData("changed-total")]
    public async Task IncompleteOrChangingCollectionsNeverPartiallyFollow(string mode)
    {
        using var home = new TestWorkspace(); var owner = Owner(home); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); using var worker = Worker(home.Store, new(new Clients(new Handler(mode)), cache));
        var job = home.Store.QueueMusicList(new("collection", Collection, "artist"), owner);
        for (var index = 0; index < 3; index++) await worker.RunOnceAsync(CancellationToken.None);
        var current = home.Store.GetMusicListJob(job.Id, owner); Assert.NotEqual("ready", current.Status);
        Assert.Throws<DomainException>(() => home.Store.ApplyMusicList(job.Id, new(current.Revision, [ArtistId(0)]), owner)); Assert.Empty(home.Store.FollowedMusicArtists(owner.Id));
    }
    [Fact]
    public async Task FollowCapacityIsAtomicAtApplyTimeAndForeignAccountsCannotReadTheSnapshot()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); using var worker = Worker(home.Store, new(new Clients(new Handler()), cache));
        var job = home.Store.QueueMusicList(new("collection", Collection, "artist"), owner); for (var index = 0; index < 3; index++) await worker.RunOnceAsync(CancellationToken.None);
        var ready = home.Store.GetMusicListJob(job.Id, owner); var stranger = home.Store.SaveAccount(null, new("Other"), "isolated"); Assert.Throws<DomainException>(() => home.Store.GetMusicListJob(job.Id, stranger));
        using (var db = new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data, "home.db")}"))
        {
            db.Open(); using var tx = db.BeginTransaction(); using var insert = db.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO music_follows(user_id,artist_mbid,name,created_at) VALUES('1',$id,'Capacity fixture','2026-10-15')"; var id = insert.Parameters.Add("$id", SqliteType.Text);
            for (var index = 1000; index < 1999; index++) { id.Value = ArtistId(index); insert.ExecuteNonQuery(); } tx.Commit();
        }
        Assert.Throws<DomainException>(() => home.Store.ApplyMusicList(job.Id, new(ready.Revision, [ArtistId(0), ArtistId(1)]), owner));
        Assert.Equal(999, home.Store.FollowedMusicArtists(owner.Id).Count); Assert.DoesNotContain(home.Store.FollowedMusicArtists(owner.Id), artist => artist.Id == ArtistId(0));
        home.Store.ApplyMusicList(job.Id, new(ready.Revision, [ArtistId(0)]), owner); Assert.Equal(1000, home.Store.FollowedMusicArtists(owner.Id).Count);
        home.Store.FollowMusicArtist(ArtistId(0), "Duplicate", owner.Id); Assert.Equal(1000, home.Store.FollowedMusicArtists(owner.Id).Count);
    }
    [Fact]
    public async Task PlaylistUsesVerifiedIdsAndRejectsLateListOrAccountChanges()
    {
        using var home = new TestWorkspace(); var owner = Owner(home); home.Write("Known.flac"); home.Write("Unidentified.mp3"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var files = home.Store.GetCatalog(null, null, null, 0, 10).Items; var known = files.Single(file => file.RelativePath == "Known.flac");
        home.Store.SaveMusicTags(known.Id, new("Track", "Wrong local spelling", "", "Album", null, 1, 1, ArtistId(0), null, null, null, null, null, null));
        var playlist = home.Store.SavePlaylist(null, new("My list", files.Select(file => file.Id).ToArray()), owner);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }); using var worker = Worker(home.Store, new(new Clients(new Handler()), cache));
        var job = home.Store.QueueMusicList(new("playlist", playlist.Id, "artist"), owner); await worker.RunOnceAsync(CancellationToken.None);
        var ready = home.Store.GetMusicListJob(job.Id, owner); Assert.Equal("ready", ready.Status); Assert.Equal("Provider artist 0", Assert.Single(ready.Items).Name); Assert.Equal(1, ready.Skipped);
        home.Store.SavePlaylist(playlist.Id, new("Edited list", [known.Id]), owner);
        Assert.Throws<DomainException>(() => home.Store.ApplyMusicList(job.Id, new(ready.Revision, [ArtistId(0)]), owner)); Assert.Empty(home.Store.FollowedMusicArtists(owner.Id));
        var pending = home.Store.QueueMusicList(new("playlist", playlist.Id, "artist"), owner); var work = home.Store.ClaimMusicList()!;
        home.Store.CancelMusicList(pending.Id, work.Job.Revision, owner); Assert.Throws<OperationCanceledException>(() => home.Store.RequireMusicListWork(work));
        var member = home.Store.SaveAccount(null, new("Member"), "isolated"); var plan = home.Store.QueueMusicList(new("collection", Collection, "artist"), member);
        var changed = home.Store.SaveAccount(member.Id, new("Member", CanDiscover: false)); Assert.Throws<DomainException>(() => home.Store.GetMusicListJob(plan.Id, changed));
    }
    private static MusicListWorker Worker(MediaStore store, MusicBrainzProvider provider) => new(store, provider, new EventHub(), NullLogger<MusicListWorker>.Instance);
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://musicbrainz.org/ws/2/") }; }
    private sealed class Handler(string mode = "valid") : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!; Urls.Add(uri.ToString()); object body;
            if (uri.AbsolutePath.Contains("collection/")) body = new { collections = new[] { new Dictionary<string, object> { ["id"] = Collection, ["name"] = "Isolated public collection", ["entity-type"] = "artist" } } };
            else if (uri.AbsolutePath.Contains("artist/")) body = new { id = uri.Segments[^1], name = "Provider artist 0" };
            else
            {
                var offset = int.Parse(QueryHelpers.ParseQuery(uri.Query)["offset"].ToString()); var count = mode == "short" ? 49 : offset == 0 ? 50 : 1;
                body = new Dictionary<string, object> { ["artist-count"] = mode == "changed-total" && offset > 0 ? 52 : 51, ["artist-offset"] = offset,
                    ["artists"] = Enumerable.Range(0, count).Select(index => new { id = ArtistId(mode == "duplicate" && offset > 0 ? 0 : offset + index), name = "Provider artist " + (offset + index) }).ToArray() };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }
}
