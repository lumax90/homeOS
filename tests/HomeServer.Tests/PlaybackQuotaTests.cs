using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class PlaybackQuotaTests
{
    private const string Password = "Quota-fixture-2026!";
    private static PlaybackClient Direct => new(["mp4", "mkv", "flac"], ["h264"], ["aac", "flac"], true);
    private static string Id() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task DirectRangesAreLeasedAndTheSameAccountCannotBorrowAnotherLoginStream()
    {
        using var home = new TestWorkspace(); home.Write("Range.mp4", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        await using var app = new Factory(home); using var first = app.CreateClient(); await Setup(first);
        var store = app.Services.GetRequiredService<MediaStore>(); store.SaveUserPlaybackLimits("1", new(1, 1));
        Assert.Equal((HttpStatusCode)428, (await first.GetAsync($"/api/media/{media.Id}/stream")).StatusCode);
        var clientId = Id(); var input = new PlaybackStart(media.Id, Direct, ClientId: clientId, ClientRevision: 1); var playing = await Start(first, input);
        Assert.Equal(playing.Id, (await Start(first, input)).Id); Assert.Equal("direct", playing.Mode);
        using var range = new HttpRequestMessage(HttpMethod.Get, playing.Url); range.Headers.Range = new(10, 29);
        using var response = await first.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(20, (await response.Content.ReadAsByteArrayAsync()).Length);
        using var suffix = new HttpRequestMessage(HttpMethod.Get, playing.Url); suffix.Headers.Range = new(null, 12);
        using var tail = await first.SendAsync(suffix); Assert.Equal(HttpStatusCode.PartialContent, tail.StatusCode); Assert.Equal("bytes 244-255/256", tail.Content.Headers.ContentRange!.ToString());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, playing.Url); conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await first.SendAsync(conditional)).StatusCode);
        using var head = new HttpRequestMessage(HttpMethod.Head, playing.Url); using var headResponse = await first.SendAsync(head); Assert.Equal(256, headResponse.Content.Headers.ContentLength);
        using var invalidRange = new HttpRequestMessage(HttpMethod.Get, playing.Url); invalidRange.Headers.Range = new(999, 1000);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await first.SendAsync(invalidRange)).StatusCode);
        using var second = app.CreateClient(); await Login(second, "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync(playing.Url)).StatusCode);
        using var conditionalOther = new HttpRequestMessage(HttpMethod.Get, playing.Url); conditionalOther.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.Forbidden, (await second.SendAsync(conditionalOther)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await second.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, Direct))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/playback", input with { ClientId = Id() })).StatusCode);
        var next = await Start(first, input with { ClientRevision = 2, ReplaceSessionId = playing.Id });
        Assert.NotEqual(playing.Id, next.Id); Assert.Single(store.ActivePlaybackLeases());
        (await first.PostAsJsonAsync($"/api/playback/clients/{clientId}/close", new PlaybackClientClose(1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(next.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/playback", input)).StatusCode);
        (await second.DeleteAsync($"/api/account/playback/{next.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Gone, (await first.GetAsync(next.Url)).StatusCode); Assert.Empty(store.ActivePlaybackLeases());
        await Start(second, new(media.Id, Direct));
    }
    [Fact]
    public async Task AccountDeviceAndServerAdmissionAreAtomicAcrossProtocolKinds()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); home.Write("Song.flac"); var library = home.AddLibrary(); await home.Scan(library.Id);
        home.Store.CreateOwner(new("Owner", "fixture")); var media = home.Store.GetCatalog(null, null, null, 0, 10).Items;
        var video = media.Single(item => item.Kind == "video"); var audio = media.Single(item => item.Kind == "audio");
        var device = home.Store.CreatePlaybackDevice("Shared browser"); var login = home.Store.CreateLoginSession("1", 1, "fixture", playbackDeviceId: device.Id); var actor = home.Store.GetPlaybackActor("1", login.Id);
        home.Store.SaveUserPlaybackLimits("1", new(1, 1));
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            try { return home.Store.AdmitPlayback(actor, Id(), 1, index % 2 == 0 ? video.Id : audio.Id, index % 2 == 0 ? "preparing" : "pcm", "fixture", DateTimeOffset.UtcNow); }
            catch (DomainException ex) { Assert.Equal(429, ex.StatusCode); return null; }
        })));
        var admitted = Assert.Single(results.OfType<PlaybackAdmission>()); Assert.Single(home.Store.ActivePlaybackLeases()); home.Store.EndPlaybackLease(admitted.Lease.Id, "closed", DateTimeOffset.UtcNow);
        home.Store.SaveUserPlaybackLimits("1", new(3, 3, 1)); home.Store.SavePlaybackDevice(device.Id, new("Shared browser", 1, 0));
        var first = home.Store.AdmitPlayback(actor, Id(), 1, video.Id, "preparing", "fixture", DateTimeOffset.UtcNow);
        var other = home.Store.SaveAccount(null, new("Member"), "fixture"); var memberSession = home.Store.CreateLoginSession(other.Id, 1, "fixture", playbackDeviceId: device.Id); var member = home.Store.GetPlaybackActor(other.Id, memberSession.Id);
        Assert.Equal(429, Assert.Throws<DomainException>(() => home.Store.AdmitPlayback(member, Id(), 1, audio.Id, "pcm", "fixture", DateTimeOffset.UtcNow)).StatusCode);
        Assert.Equal(401, Assert.Throws<DomainException>(() => home.Store.AdmitPlayback(actor with { DeviceId = Id() }, Id(), 1, video.Id, "preparing", "fixture", DateTimeOffset.UtcNow)).StatusCode);
        home.Store.EndPlaybackLease(first.Lease.Id, "closed", DateTimeOffset.UtcNow);
        home.Store.SavePlaybackLimits(new(1, 2));
        home.Store.AdmitPlayback(member, Id(), 1, audio.Id, "pcm", "fixture", DateTimeOffset.UtcNow);
        var anotherLogin = home.Store.CreateLoginSession("1", 1, "Other browser"); var another = home.Store.GetPlaybackActor("1", anotherLogin.Id);
        Assert.Equal(429, Assert.Throws<DomainException>(() => home.Store.AdmitPlayback(another, Id(), 1, video.Id, "preparing", "fixture", DateTimeOffset.UtcNow)).StatusCode);
    }
    [Fact]
    public async Task ActiveDeviceLimitAllowsMultipleTabsButRejectsASecondDevice()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        home.Store.CreateOwner(new("Owner", "fixture")); home.Store.SaveUserPlaybackLimits("1", new(3, 1));
        var first = home.Store.CreateLoginSession("1", 1, "First"); var second = home.Store.CreateLoginSession("1", 1, "Second");
        var actor = home.Store.GetPlaybackActor("1", first.Id); var other = home.Store.GetPlaybackActor("1", second.Id);
        home.Store.AdmitPlayback(actor, Id(), 1, media.Id, "preparing", "fixture", DateTimeOffset.UtcNow);
        home.Store.AdmitPlayback(actor, Id(), 1, media.Id, "preparing", "fixture", DateTimeOffset.UtcNow);
        var error = Assert.Throws<DomainException>(() => home.Store.AdmitPlayback(other, Id(), 1, media.Id, "preparing", "fixture", DateTimeOffset.UtcNow));
        Assert.Equal(429, error.StatusCode); Assert.Contains("cihazda", error.Message); Assert.Equal(2, home.Store.ActivePlaybackLeases().Count);
    }
    [Fact]
    public async Task ExpirationPauseLimitPolicyRevisionAndRestartCannotResurrectOldLeases()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        home.Store.CreateOwner(new("Owner", "fixture")); var login = home.Store.CreateLoginSession("1", 1, "Fixture"); var actor = home.Store.GetPlaybackActor("1", login.Id); var client = Id(); var now = DateTimeOffset.UtcNow;
        PlaybackLease Admit(long revision, DateTimeOffset time) => home.Store.AdmitPlayback(actor, client, revision, media.Id, "preparing", "fixture-" + revision, time).Lease;
        var expired = Admit(1, now); home.Store.ReconcilePlaybackLeases(now.AddMinutes(2)); Assert.Equal("expired", home.Store.GetPlaybackLease(expired.Id)!.EndReason);
        Assert.Throws<DomainException>(() => Admit(1, now.AddMinutes(2)));
        var paused = Admit(2, now.AddMinutes(3)); home.Store.TouchPlayback(paused.Id, actor, new(0, true), now.AddMinutes(3));
        for (var i = 4; i < 18; i++) home.Store.TouchPlayback(paused.Id, actor, new(0, true), now.AddMinutes(i));
        home.Store.ReconcilePlaybackLeases(now.AddMinutes(18)); Assert.Equal("paused-timeout", home.Store.GetPlaybackLease(paused.Id)!.EndReason);
        var beforePolicy = Admit(3, now.AddMinutes(19)); home.Store.SaveUserPlaybackLimits("1", new(2, 2)); home.Store.ReconcilePlaybackLeases(now.AddMinutes(19));
        Assert.Equal("limits-changed", home.Store.GetPlaybackLease(beforePolicy.Id)!.EndReason);
        Assert.Throws<DomainException>(() => home.Store.SaveUserPlaybackLimits("1", new(3, 3)));
        var interrupted = Admit(4, now.AddMinutes(20)); var reopened = new MediaStore(home.Paths); reopened.Initialize(); reopened.RecoverPlaybackLeases(now.AddMinutes(20));
        Assert.Equal("server-restarted", reopened.GetPlaybackLease(interrupted.Id)!.EndReason); Assert.Empty(reopened.ActivePlaybackLeases()); Assert.Equal(2, reopened.GetUserPlaybackLimits("1").MaximumStreams);
        Assert.Throws<DomainException>(() => Admit(4, now.AddMinutes(20))); Assert.NotNull(Admit(5, now.AddMinutes(20)));
    }
    [Fact]
    public async Task DeviceCookieSurvivesLoginAndMemberCannotManageOtherHouseholdPlayback()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        await using var app = new Factory(home); using var admin = app.CreateClient(); await Setup(admin);
        var devices = (await admin.GetFromJsonAsync<PlaybackDevice[]>("/api/playback/devices"))!; var originalDevice = Assert.Single(devices);
        (await admin.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode(); await Login(admin, "Admin");
        Assert.Equal(originalDevice.Id, Assert.Single((await admin.GetFromJsonAsync<PlaybackDevice[]>("/api/playback/devices"))!).Id);
        var create = await admin.PostAsJsonAsync("/api/users", new CreateAccount(new("Member"), Password)); create.EnsureSuccessStatusCode(); var user = (await create.Content.ReadFromJsonAsync<Account>())!;
        using var member = app.CreateClient(); await Login(member, "Member"); var active = await Start(admin, new(media.Id, Direct));
        var usage = (await member.GetFromJsonAsync<PlaybackUsage>("/api/account/playback"))!; Assert.Empty(usage.Active); Assert.Empty(usage.History);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/account/playback?all=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"/api/account/playback/{active.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/users/{user.Id}/playback-limits", new UserPlaybackLimits(16, 16))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/playback/devices/{originalDevice.Id}", new PlaybackDeviceInput("Spoof", 16, 0))).StatusCode);
        var own = await Start(member, new(media.Id, Direct));
        (await admin.PutAsJsonAsync($"/api/users/{user.Id}/playback-limits", new UserPlaybackLimits(0, 1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Gone, (await member.GetAsync(own.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, Direct))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(active.Url)).StatusCode);
    }
    [Fact]
    public async Task LogoutDuringDelayedAnalysisCancelsPreparationAndCannotPublishALateSession()
    {
        using var home = new TestWorkspace(); home.Write("One.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var probe = new DelayedProbe(); await using var app = new Factory(home, probe: probe); using var client = app.CreateClient(); await Setup(client);
        var pending = client.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, Direct)); await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); using var result = await pending;
        Assert.False(result.IsSuccessStatusCode); Assert.Empty(app.Services.GetRequiredService<MediaStore>().ActivePlaybackLeases());
    }
    [Fact]
    public async Task ClosingMusicWhilePreparingCancelsTheDecodeInputAndLeavesNoPublishedCache()
    {
        using var home = new TestWorkspace(); home.Write("One.flac"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        var probe = new DelayedProbe(); await using var app = new Factory(home, probe: probe); using var client = app.CreateClient(); await Setup(client);
        var started = await client.PostAsJsonAsync("/api/music/playback", new MusicPlaybackStart(media.Id)); started.EnsureSuccessStatusCode(); var play = (await started.Content.ReadFromJsonAsync<PlaybackSession>())!;
        var pending = client.PostAsync($"/api/music/media/{media.Id}/prepare?playbackId={play.Id}", null); await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (await client.DeleteAsync($"/api/account/playback/{play.Id}")).EnsureSuccessStatusCode(); await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ended = await Record.ExceptionAsync(async () => { using var result = await pending; Assert.False(result.IsSuccessStatusCode); });
        Assert.True(ended is null or OperationCanceledException or IOException or HttpRequestException, ended?.ToString());
        Assert.Empty(app.Services.GetRequiredService<MediaStore>().ActivePlaybackLeases());
        Assert.False(Directory.Exists(Path.Combine(home.Paths.Data, "music-cache", "pcm")));
    }
    [MediaFact]
    public async Task DirectHlsAndPcmShareSlotsAndGaplessPrefetchDoesNotSpendExtraSlots()
    {
        using var home = new TestWorkspace(); var tools = SubtitleBurnTests.Tools(home);
        var video = Path.Combine(home.Source, "Video.mp4");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24", "-t", "3", "-c:v", "libx264", "-threads", "1", "-g", "12", video], TimeSpan.FromSeconds(15), CancellationToken.None);
        foreach (var name in new[] { "One", "Two" }) await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "0.25", "-c:a", "flac", Path.Combine(home.Source, name + ".flac")], TimeSpan.FromSeconds(15), CancellationToken.None);
        var library = home.AddLibrary(); await home.Scan(library.Id); var media = home.Store.GetCatalog(null, null, null, 0, 10).Items; var file = media.Single(item => item.Kind == "video"); var songs = media.Where(item => item.Kind == "audio").ToArray();
        await using var app = new Factory(home, native: true); using var client = app.CreateClient(); await Setup(client); var store = app.Services.GetRequiredService<MediaStore>(); store.SaveUserPlaybackLimits("1", new(1, 1));
        var player = Id(); var direct = await Start(client, new(file.Id, Direct, ClientId: player, ClientRevision: 1));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/music/playback", new MusicPlaybackStart(songs[0].Id))).StatusCode);
        var hls = await Start(client, new(file.Id, Direct, ForceCompatible: true, ClientId: player, ClientRevision: 2, ReplaceSessionId: direct.Id));
        Assert.Equal("video-transcode", hls.Mode); Assert.Contains("#EXTM3U", await client.GetStringAsync(hls.Url)); Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(direct.Url)).StatusCode);
        var started = await client.PostAsJsonAsync("/api/music/playback", new MusicPlaybackStart(songs[0].Id, player, 3)); started.EnsureSuccessStatusCode(); var music = (await started.Content.ReadFromJsonAsync<PlaybackSession>())!;
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(hls.Url)).StatusCode);
        (await client.PostAsJsonAsync($"/api/playback/{music.Id}/heartbeat", new PlaybackHeartbeat(0, false, songs[0].Id))).EnsureSuccessStatusCode();
        Assert.Equal((HttpStatusCode)428, (await client.PostAsync($"/api/music/media/{songs[0].Id}/prepare", null)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await client.GetAsync($"/api/music/media/{songs[0].Id}/pcm?fingerprint=x&start=0&frames=1")).StatusCode);
        foreach (var song in songs)
        {
            var prepared = await client.PostAsync($"/api/music/media/{song.Id}/prepare?playbackId={music.Id}", null); prepared.EnsureSuccessStatusCode(); var pcm = (await prepared.Content.ReadFromJsonAsync<MusicPcmInfo>())!;
            var bytes = await client.GetByteArrayAsync($"/api/music/media/{song.Id}/pcm?playbackId={music.Id}&fingerprint={pcm.Fingerprint}&start=0&frames=100"); Assert.Equal(800, bytes.Length);
            (await client.PostAsJsonAsync($"/api/playback/{music.Id}/heartbeat", new PlaybackHeartbeat(.1, false, song.Id))).EnsureSuccessStatusCode();
        }
        Assert.Single(store.ActivePlaybackLeases()); Assert.Equal(songs[^1].Id, store.GetPlaybackLease(music.Id!)!.MediaId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/media/{file.Id}/stream?playbackId={music.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/playback", new PlaybackStart(file.Id, Direct))).StatusCode);
        var fallback = await Start(client, new(songs[0].Id, Direct, ClientId: player, ClientRevision: 4)); Assert.Equal("direct", fallback.Mode);
        (await client.PostAsJsonAsync($"/api/playback/clients/{player}/close", new PlaybackClientClose(3))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(fallback.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync($"/api/playback/{music.Id}/heartbeat", new PlaybackHeartbeat(0))).StatusCode);
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode(); Assert.Empty(store.ActivePlaybackLeases());
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "playback")));
    }
    [Fact]
    public async Task TerminationAbortsAnAlreadyOpenDirectResponseAndFreesTheSlot()
    {
        using var home = new TestWorkspace();
        await using (var file = File.Create(Path.Combine(home.Source, "Long.mp4"))) file.SetLength(64 * 1024 * 1024);
        var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        await using var app = new Factory(home); using var client = app.CreateClient(); await Setup(client); var play = await Start(client, new(media.Id, Direct));
        using var response = await client.GetAsync(play.Url, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        await using var bytes = await response.Content.ReadAsStreamAsync(); var prefix = new byte[1024]; Assert.True(await bytes.ReadAsync(prefix) > 0);
        (await client.DeleteAsync($"/api/account/playback/{play.Id}")).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = await Record.ExceptionAsync(() => bytes.CopyToAsync(Stream.Null, timeout.Token));
        Assert.NotNull(failure); Assert.False(timeout.IsCancellationRequested, "The server must end the old response, without waiting for a client timeout.");
        Assert.Empty(app.Services.GetRequiredService<MediaStore>().ActivePlaybackLeases());
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(play.Url)).StatusCode);
    }
    private static async Task<PlaybackSession> Start(HttpClient client, PlaybackStart input)
    { var response = await client.PostAsJsonAsync("/api/playback", input); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<PlaybackSession>())!; }
    private static async Task Setup(HttpClient client) { await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Admin", Password))).EnsureSuccessStatusCode(); await Csrf(client); }
    private static async Task Login(HttpClient client, string name) { await Csrf(client); (await client.PostAsJsonAsync("/api/auth/login", new Credentials(name, Password))).EnsureSuccessStatusCode(); await Csrf(client); }
    private static async Task Csrf(HttpClient client)
    { var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); }
    private sealed class Factory(TestWorkspace home, bool native = false, IMediaProbe? probe = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true",
                ["Home:FfmpegPath"] = native ? Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG") : "absent",
                ["Home:FfprobePath"] = native ? Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") : "absent"
            }));
            if (probe is not null) builder.ConfigureTestServices(services => services.AddSingleton(probe));
        }
    }
    private sealed class DelayedProbe : IMediaProbe
    {
        public bool FfprobeAvailable => true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProbeResult?> ProbeAsync(string path, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
    }
}
