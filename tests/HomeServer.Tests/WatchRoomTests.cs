using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Watching;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class WatchRoomTests
{
    private static string Id() => Guid.NewGuid().ToString("N");
    [Fact]
    public async Task InvitationsRespectLibraryAccessAndRemovalEndsOnlyTheInvitedPlayback()
    {
        using var f = await Fixture.Create(); var member = f.Account("Guest"); var outsider = f.Account("Outsider"); var blocked = f.Account("Restricted", all: false);
        var hostLease = f.Play(f.Owner); var host = f.CreateRoom(hostLease);
        Assert.Empty(f.Store.GetWatchRooms(outsider, f.Now));
        Assert.Equal(404, Assert.Throws<DomainException>(() => f.Store.GetWatchRoom(host.Room.Id, outsider, f.Now)).StatusCode);
        Assert.Equal(404, Assert.Throws<DomainException>(() => f.Command(host, f.Owner, "invite", name: "Restricted")).StatusCode);
        f.Command(host, f.Owner, "invite", name: "Guest"); Assert.Single(f.Store.GetWatchRooms(member, f.Now));
        var guest = f.Join(host.Room.Id, member); var guestLease = f.Play(member, guest.ClientId); f.Pulse(guest, member, guestLease.Id, 1);
        Assert.Equal(403, Assert.Throws<DomainException>(() => f.Command(guest, member, "seek", position: 30)).StatusCode);
        f.Command(host, f.Owner, "control", enabled: true); f.Command(guest, member, "seek", position: 30);
        f.Command(host, f.Owner, "remove", user: member.UserId);
        Assert.Equal("closed", f.Store.GetPlaybackLease(guestLease.Id)!.Status); Assert.Equal("active", f.Store.GetPlaybackLease(hostLease.Id)!.Status);
        Assert.Empty(f.Store.GetWatchRooms(member, f.Now));
        Assert.Equal(404, Assert.Throws<DomainException>(() => f.Join(host.Room.Id, member)).StatusCode);
        Assert.Equal(404, Assert.Throws<DomainException>(() => f.Pulse(guest, member, guestLease.Id, 2)).StatusCode);
    }
    [Fact]
    public async Task AllPlayersAcknowledgeTheSameBarrierBeforeScheduledPlaybackAndBufferingPausesEveryone()
    {
        using var f = await Fixture.Create(); var hostLease = f.Play(f.Owner); var host = f.CreateRoom(hostLease); var actor = f.Account("Guest");
        f.Command(host, f.Owner, "invite", name: "Guest"); var guest = f.Join(host.Room.Id, actor); var guestLease = f.Play(actor, guest.ClientId);
        var waiting = f.Command(host, f.Owner, "play").Room; Assert.Equal("waiting", waiting.State);
        var first = f.Pulse(host, f.Owner, hostLease.Id, 1); Assert.Equal("waiting", first.State);
        var start = f.Pulse(guest, actor, guestLease.Id, 1); Assert.Equal("playing", start.State); Assert.Equal(f.Now.AddSeconds(2).ToUnixTimeMilliseconds(), start.AnchorUnixMs);
        Assert.All(start.Participants, participant => Assert.True(participant.Ready));
        f.Now = f.Now.AddSeconds(5);
        var buffer = f.Pulse(guest, actor, guestLease.Id, 2, ready: false); Assert.Equal("waiting", buffer.State); Assert.InRange(buffer.PositionSeconds, 2.99, 3.01);
        Assert.True(buffer.SyncRevision > start.SyncRevision);
        var old = f.Store.PulseWatchRoom(host.Room.Id, host.MemberId, f.Owner, new(host.ClientRevision, 2, start.Epoch, start.SyncRevision, hostLease.Id, 3, true, true), f.Now);
        Assert.Equal("waiting", old.State); Assert.False(old.Participants.Single(value => value.Id == host.MemberId).Ready);
        f.Pulse(host, f.Owner, hostLease.Id, 3); var resumed = f.Pulse(guest, actor, guestLease.Id, 3); Assert.Equal("playing", resumed.State);
        f.Now = f.Now.AddSeconds(16); f.Store.ReconcileWatchRooms(f.Now);
        var paused = f.Store.GetWatchRoom(host.Room.Id, f.Owner, f.Now, host.MemberId); Assert.Equal("paused", paused.State); Assert.Contains("bağlantı", paused.Message);
    }
    [Fact]
    public async Task ConcurrentCommandsUseCompareAndSwapAndReceiptsNeverReplayAnOldSeek()
    {
        using var f = await Fixture.Create(); var host = f.CreateRoom(f.Play(f.Owner)); var initial = f.Store.GetWatchRoom(host.Room.Id, f.Owner, f.Now, host.MemberId);
        var input = new WatchRoomCommand(host.MemberId, host.ClientRevision, Id(), initial.Revision, "seek", PositionSeconds: 25);
        var commands = await Task.WhenAll(new[] { input, input with { CommandId = Id(), PositionSeconds = 50 } }.Select(value => Task.Run(() =>
        {
            try { return (Input: value, Result: f.Store.CommandWatchRoom(host.Room.Id, f.Owner, value, f.Now)); }
            catch (DomainException ex) { Assert.Equal(409, ex.StatusCode); return (Input: value, Result: (WatchRoomCommandResult?)null); }
        })));
        var applied = Assert.Single(commands, value => value.Result is not null); Assert.Equal(applied.Input.PositionSeconds, applied.Result!.Room.PositionSeconds);
        f.Command(host, f.Owner, "seek", position: 70);
        var replay = f.Store.CommandWatchRoom(host.Room.Id, f.Owner, applied.Input, f.Now); Assert.True(replay.Duplicate); Assert.Equal(70, replay.Room.PositionSeconds); Assert.Equal(applied.Result.AppliedRevision, replay.AppliedRevision);
        Assert.Equal(409, Assert.Throws<DomainException>(() => f.Store.CommandWatchRoom(host.Room.Id, f.Owner, applied.Input with { PositionSeconds = 10 }, f.Now)).StatusCode);
        Assert.Equal(2, f.Store.GetWatchRoomEvents(host.Room.Id, f.Owner, f.Now).Count);
    }
    [Fact]
    public async Task LateJoinLeaveAndHeartbeatCannotReplaceANewerClientIntent()
    {
        using var f = await Fixture.Create(); var host = f.CreateRoom(f.Play(f.Owner)); var actor = f.Account("Guest"); f.Command(host, f.Owner, "invite", name: "Guest");
        var client = Id(); var oldInput = new WatchRoomJoin(client, 1); var guest = f.Store.JoinWatchRoom(host.Room.Id, actor, oldInput, f.Now);
        Assert.Equal(guest.MemberId, f.Store.JoinWatchRoom(host.Room.Id, actor, oldInput, f.Now).MemberId);
        f.Store.LeaveWatchClient(actor, client, 2, f.Now);
        Assert.Equal(409, Assert.Throws<DomainException>(() => f.Store.JoinWatchRoom(host.Room.Id, actor, oldInput, f.Now)).StatusCode);
        var current = f.Store.JoinWatchRoom(host.Room.Id, actor, oldInput with { ClientRevision = 3 }, f.Now);
        f.Store.LeaveWatchClient(actor, client, 2, f.Now);
        var lease = f.Play(actor, client); var present = f.Pulse(current, actor, lease.Id, 5);
        var stale = f.Store.PulseWatchRoom(host.Room.Id, current.MemberId, actor, new(3, 4, present.Epoch, present.SyncRevision, null, 80, false, false), f.Now);
        Assert.True(stale.Participants.Single(value => value.Id == current.MemberId).Ready);
        Assert.Equal(410, Assert.Throws<DomainException>(() => f.Pulse(guest, actor, lease.Id, 6)).StatusCode);
        var otherLogin = f.Login(actor.UserId); Assert.Equal(404, Assert.Throws<DomainException>(() => f.Pulse(current, otherLogin, lease.Id, 6)).StatusCode);
    }
    [Fact]
    public async Task RemoteControlIsExplicitSameAccountAndConsumesNoAdditionalPlaybackSlot()
    {
        using var f = await Fixture.Create(); f.Store.SaveUserPlaybackLimits("1", new(1, 1)); var renderer = f.Play(f.Owner);
        var controllerActor = f.Login("1"); var outsider = f.Account("Other"); Assert.Empty(f.Store.GetWatchRooms(controllerActor, f.Now));
        var remote = f.CreateRoom(renderer, "remote"); var control = f.Join(remote.Room.Id, controllerActor, "controller");
        Assert.Single(f.Store.ActivePlaybackLeases()); Assert.Empty(f.Store.GetWatchRooms(outsider, f.Now));
        Assert.Equal(404, Assert.Throws<DomainException>(() => f.Join(remote.Room.Id, outsider, "controller")).StatusCode);
        Assert.Equal(409, Assert.Throws<DomainException>(() => f.Join(remote.Room.Id, controllerActor, "player")).StatusCode);
        var changed = f.Command(control, controllerActor, "volume", volume: .4, muted: true); Assert.Equal(.4, changed.Room.Volume); Assert.True(changed.Room.Muted);
        f.Command(control, controllerActor, "play"); f.Pulse(remote, f.Owner, renderer.Id, 1); Assert.Single(f.Store.ActivePlaybackLeases());
        f.Store.LeaveWatchClient(controllerActor, control.ClientId, 2, f.Now); Assert.Equal("active", f.Store.GetPlaybackLease(renderer.Id)!.Status);
        f.Store.RevokeLoginSession(f.Owner.LoginId, "1"); f.Store.ReconcileWatchRooms(f.Now);
        Assert.Empty(f.Store.ActivePlaybackLeases()); Assert.Empty(f.Store.GetWatchRooms(controllerActor, f.Now));
    }
    [Fact]
    public async Task QualityReplacementRemainsLiveButRoomClosureFencesEvenAnUnpublishedReplacement()
    {
        using var f = await Fixture.Create(); var original = f.Play(f.Owner); var host = f.CreateRoom(original);
        var binding = new WatchPlaybackBinding(host.Room.Id, host.MemberId, host.ClientRevision, host.Room.Epoch);
        var replacing = f.Store.AdmitPlayback(f.Owner, host.ClientId, 2, f.Media.Id, "preparing", Id(), f.Now, original.Id, binding).Lease;
        f.Store.ReconcileWatchRooms(f.Now); Assert.Equal("starting", f.Store.GetPlaybackLease(replacing.Id)!.Status);
        var ready = f.Store.BindPlayback(replacing, new(null, f.Media.Id, "video-transcode", "", 0, 120, [], null, "ready"), f.Now);
        f.Pulse(host, f.Owner, ready.Id, 1);
        var pending = f.Store.AdmitPlayback(f.Owner, host.ClientId, 3, f.Media.Id, "preparing", Id(), f.Now, ready.Id, binding).Lease;
        f.Store.LeaveWatchClient(f.Owner, host.ClientId, 2, f.Now);
        Assert.Equal("closed", f.Store.GetPlaybackLease(pending.Id)!.Status);
        Assert.Equal(410, Assert.Throws<DomainException>(() => f.Store.BindPlayback(pending, new(null, f.Media.Id, "direct", "", 0, 120, [], null, "ready"), f.Now)).StatusCode);
        Assert.Equal(410, Assert.Throws<DomainException>(() => f.Store.AdmitPlayback(f.Owner, host.ClientId, 4, f.Media.Id, "preparing", Id(), f.Now, watch: binding)).StatusCode);
        Assert.Empty(f.Store.ActivePlaybackLeases());
    }
    [Fact]
    public async Task RestartFreezesTheLastObservedPositionAndOldEpochCannotDeclareAPlayerReady()
    {
        using var f = await Fixture.Create(); var lease = f.Play(f.Owner); var host = f.CreateRoom(lease); f.Command(host, f.Owner, "play"); var playing = f.Pulse(host, f.Owner, lease.Id, 1);
        f.Now = f.Now.AddSeconds(6); f.Pulse(host, f.Owner, lease.Id, 2); f.Now = f.Now.AddSeconds(30);
        var reopened = new MediaStore(f.Home.Paths); reopened.Initialize(); reopened.RecoverPlaybackLeases(f.Now); reopened.ReconcileWatchRooms(f.Now, restart: true);
        var recovered = reopened.GetWatchRoom(host.Room.Id, f.Owner, f.Now, host.MemberId); Assert.Equal("paused", recovered.State); Assert.Equal(4, recovered.PositionSeconds); Assert.True(recovered.Epoch > playing.Epoch); Assert.Empty(reopened.ActivePlaybackLeases());
        var old = reopened.PulseWatchRoom(host.Room.Id, host.MemberId, f.Owner, new(host.ClientRevision, 3, playing.Epoch, playing.SyncRevision, lease.Id, 5, true, true), f.Now);
        Assert.False(Assert.Single(old.Participants).Ready);
        Assert.Equal(409, Assert.Throws<DomainException>(() => reopened.CommandWatchRoom(host.Room.Id, f.Owner, new(host.MemberId, host.ClientRevision, Id(), playing.Revision, "play"), f.Now)).StatusCode);
    }
    [Fact]
    public async Task MediaChangeRequiresEveryInvitedAccountAccessAndRebindsTheExactSource()
    {
        using var f = await Fixture.Create(); var secondPath = f.Home.Write("Second.mp4"); await f.Home.Scan(f.Library.Id); var second = f.Store.GetCatalog(null, null, null, 0, 10).Items.Single(item => item.RelativePath == "Second.mp4");
        var lease = f.Play(f.Owner); var host = f.CreateRoom(lease); f.Account("Guest", all: false, libraries: [f.Library.Id]); f.Command(host, f.Owner, "invite", name: "Guest");
        var privateFolder = Path.Combine(f.Home.Root, "private"); Directory.CreateDirectory(privateFolder); var privatePath = Path.Combine(privateFolder, "Private.mp4"); await File.WriteAllBytesAsync(privatePath, [1, 2, 3, 4]);
        var privateLibrary = f.Store.AddLibrary(new("Private", privateFolder, "movies")); await f.Home.Scan(privateLibrary.Id);
        var privateMedia = Assert.Single(f.Store.GetCatalog(null, null, privateLibrary.Id, 0, 10).Items);
        var denied = new AnalyzedMedia(privateMedia, privatePath, MediaAnalysisService.Fingerprint(new FileInfo(privatePath)), new(60, 320, 180, "h264", "aac"));
        var before = f.Store.GetWatchRoom(host.Room.Id, f.Owner, f.Now, host.MemberId);
        Assert.Equal(403, Assert.Throws<DomainException>(() => f.Store.CommandWatchRoom(before.Id, f.Owner, new(host.MemberId, host.ClientRevision, Id(), before.Revision, "media", MediaId: privateMedia.Id), f.Now, denied)).StatusCode);
        Assert.Equal("active", f.Store.GetPlaybackLease(lease.Id)!.Status); Assert.Equal(f.Media.Id, f.Store.GetWatchRoom(before.Id, f.Owner, f.Now).MediaId);
        var selected = new AnalyzedMedia(second, secondPath, MediaAnalysisService.Fingerprint(new FileInfo(secondPath)), new(60, 320, 180, "h264", "aac"));
        var room = f.Store.GetWatchRoom(host.Room.Id, f.Owner, f.Now, host.MemberId);
        var changed = f.Store.CommandWatchRoom(room.Id, f.Owner, new(host.MemberId, host.ClientRevision, Id(), room.Revision, "media", MediaId: second.Id), f.Now, selected).Room;
        Assert.Equal(second.Id, changed.MediaId); Assert.Equal(room.Epoch + 1, changed.Epoch); Assert.Empty(f.Store.ActivePlaybackLeases());
        var late = f.Store.PulseWatchRoom(room.Id, host.MemberId, f.Owner, new(host.ClientRevision, 2, room.Epoch, room.SyncRevision, lease.Id, 20, true, true), f.Now);
        Assert.False(Assert.Single(late.Participants).Ready);
        var fingerprint = StateFolders.Hash(secondPath); File.SetLastWriteTimeUtc(secondPath, DateTime.UtcNow.AddMinutes(2));
        f.Store.ReconcileWatchRooms(f.Now); Assert.Empty(f.Store.GetWatchRooms(f.Owner, f.Now)); Assert.Equal(fingerprint, StateFolders.Hash(secondPath));
    }
    [MediaFact]
    public async Task HttpRoomUsesIndependentDirectAndHlsLeasesAndRejectsCsrfAndStaleIdentity()
    {
        using var home = new TestWorkspace(); var tools = SubtitleBurnTests.Tools(home); var file = Path.Combine(home.Source, "Isolated.mkv");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=12", "-t", "8", "-c:v", "libx264", "-threads", "1", "-g", "12", file], TimeSpan.FromSeconds(15), CancellationToken.None);
        var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        await using var app = new Factory(home); using var host = app.CreateClient(); await Session(host);
        (await host.PostAsJsonAsync("/api/setup", new Credentials("Room owner", "room-fixture-password"))).EnsureSuccessStatusCode(); var hostIdentity = await Session(host);
        var created = await host.PostAsJsonAsync("/api/users", new CreateAccount(new("Guest"), "room-fixture-password")); created.EnsureSuccessStatusCode();
        using var guest = app.CreateClient(); await Session(guest); (await guest.PostAsJsonAsync("/api/auth/login", new Credentials("Guest", "room-fixture-password"))).EnsureSuccessStatusCode(); var guestIdentity = await Session(guest);
        var client = Id(); var start = new PlaybackStart(media.Id, new(["mkv"], ["h264"], [], true), ClientId: client, ClientRevision: 1);
        var played = await host.PostAsJsonAsync("/api/playback", start); played.EnsureSuccessStatusCode(); var direct = (await played.Content.ReadFromJsonAsync<PlaybackSession>())!;
        var create = new WatchRoomCreate(client, 1, direct.Id!, "together", 0, Identity: hostIdentity);
        var csrf = host.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single(); host.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsJsonAsync("/api/watch/rooms", create)).StatusCode); host.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsJsonAsync("/api/watch/rooms", create with { Identity = guestIdentity })).StatusCode);
        var connected = await host.PostAsJsonAsync("/api/watch/rooms", create); connected.EnsureSuccessStatusCode(); var connection = (await connected.Content.ReadFromJsonAsync<WatchRoomConnection>())!;
        Assert.Empty((await guest.GetFromJsonAsync<WatchRoomSummary[]>("/api/watch/rooms"))!);
        var invite = await host.PostAsJsonAsync($"/api/watch/rooms/{connection.Room.Id}/commands", new WatchRoomCommand(connection.MemberId, connection.ClientRevision, Id(), connection.Room.Revision, "invite", Name: "Guest", Identity: hostIdentity)); invite.EnsureSuccessStatusCode();
        var guestClient = Id(); var joined = await guest.PostAsJsonAsync($"/api/watch/rooms/{connection.Room.Id}/join", new WatchRoomJoin(guestClient, 1, Identity: guestIdentity)); joined.EnsureSuccessStatusCode(); var participant = (await joined.Content.ReadFromJsonAsync<WatchRoomConnection>())!;
        var hlsResponse = await guest.PostAsJsonAsync("/api/playback", start with { ClientId = guestClient, ForceCompatible = true }); hlsResponse.EnsureSuccessStatusCode(); var hls = (await hlsResponse.Content.ReadFromJsonAsync<PlaybackSession>())!;
        Assert.Equal("direct", direct.Mode); Assert.Equal("video-transcode", hls.Mode); Assert.Contains("#EXTM3U", await guest.GetStringAsync(hls.Url));
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync(direct.Url)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync(hls.Url)).StatusCode);
        var pulse = await guest.PostAsJsonAsync($"/api/watch/rooms/{connection.Room.Id}/members/{participant.MemberId}/heartbeat", new WatchRoomHeartbeat(1, 1, participant.Room.Epoch, participant.Room.SyncRevision, hls.Id, 0, true, false, Identity: guestIdentity)); pulse.EnsureSuccessStatusCode();
        Assert.Equal(2, app.Services.GetRequiredService<MediaStore>().ActivePlaybackLeases().Count);
        (await host.PostAsJsonAsync($"/api/watch/clients/{client}/leave", new WatchRoomLeave(2, hostIdentity))).EnsureSuccessStatusCode();
        Assert.Empty(app.Services.GetRequiredService<MediaStore>().ActivePlaybackLeases()); Assert.Equal(HttpStatusCode.Gone, (await guest.GetAsync(hls.Url)).StatusCode);
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "playback")));
    }
    private static async Task<WatchIdentity> Session(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        return new(session.GetProperty("userId").GetString() ?? "", session.GetProperty("sessionId").GetString() ?? "");
    }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(SubtitleBurnTests.Configuration()).AddInMemoryCollection(new Dictionary<string, string?> { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true" })); }
    }
    private sealed class Fixture : IDisposable
    {
        public TestWorkspace Home { get; } = new(); public MediaStore Store => Home.Store;
        public PlaybackActor Owner = null!; public MediaItem Media = null!; public Library Library = null!;
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Home.Write("One.mp4"); f.Library = f.Home.AddLibrary(); await f.Home.Scan(f.Library.Id); f.Media = Assert.Single(f.Store.GetCatalog(null, null, null, 0, 10).Items);
            f.Store.CreateOwner(new("Owner", "isolated")); f.Owner = f.Login("1"); return f;
        }
        public PlaybackActor Login(string id) { var login = Store.CreateLoginSession(id, Store.GetAccountSecret(id)!.Version, "Isolated device"); return Store.GetPlaybackActor(id, login.Id); }
        public PlaybackActor Account(string name, bool all = true, string[]? libraries = null) { var account = Store.SaveAccount(null, new(name, AllLibraries: all, LibraryIds: libraries), "isolated"); return Login(account.Id); }
        public PlaybackLease Play(PlaybackActor actor, string? client = null, long revision = 1)
        {
            var lease = Store.AdmitPlayback(actor, client ?? Id(), revision, Media.Id, "preparing", Id(), Now).Lease;
            return Store.BindPlayback(lease, new(null, Media.Id, "direct", "", 0, 120, [], null, "ready"), Now);
        }
        public WatchRoomConnection CreateRoom(PlaybackLease lease, string mode = "together") => Store.CreateWatchRoom(Owner, new(lease.ClientId, 1, lease.Id, mode, 0), Now);
        public WatchRoomConnection Join(string id, PlaybackActor actor, string role = "player") => Store.JoinWatchRoom(id, actor, new(Id(), 1, role), Now);
        public WatchRoomCommandResult Command(WatchRoomConnection connection, PlaybackActor actor, string action, double? position = null, string? name = null, string? user = null, bool? enabled = null, double? volume = null, bool? muted = null)
        {
            var room = Store.GetWatchRoom(connection.Room.Id, actor, Now, connection.MemberId);
            return Store.CommandWatchRoom(room.Id, actor, new(connection.MemberId, connection.ClientRevision, Id(), room.Revision, action, PositionSeconds: position, Name: name, UserId: user, Enabled: enabled, Volume: volume, Muted: muted), Now);
        }
        public WatchRoomSnapshot Pulse(WatchRoomConnection connection, PlaybackActor actor, string? lease, long sequence, bool ready = true)
        {
            var room = Store.GetWatchRoom(connection.Room.Id, actor, Now, connection.MemberId);
            return Store.PulseWatchRoom(room.Id, connection.MemberId, actor, new(connection.ClientRevision, sequence, room.Epoch, room.SyncRevision, lease, room.PositionSeconds, ready, room.State == "playing", Revision: room.Revision), Now);
        }
        public void Dispose() => Home.Dispose();
    }
}
