using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeServer.Tests;

public sealed class PlaybackTests
{
    private static PlaybackClient Web => new(["mp4", "webm", "mp3", "flac", "ogg", "wav"], ["h264", "vp9", "av1"], ["aac", "mp3", "opus", "flac"], true);
    private static MediaStreamInfo Video(string codec = "h264", string? transfer = null) => new(0, "video", codec, "und", "", true, false, false, 1920, 1080, null, null, null, 4_000_000, "High", "yuv420p", transfer, null);
    private static MediaStreamInfo Audio(int index = 1, string codec = "aac", int channels = 2) => new(index, "audio", codec, "en", "", index == 1, false, false, null, null, channels, "stereo", 48000, 192000, null, null, null, null);
    private static AnalyzedMedia Fake(string path, params MediaStreamInfo[] streams)
    {
        var file = new MediaItem("id", "library", "Library", "Fixture", "video", path, "video/mp4", 50_000_000, 100, 1920, 1080, "h264", "aac", true, "", "", false, 0, false, false);
        return new AnalyzedMedia(file, path, "fingerprint", new ProbeResult(100, 1920, 1080, "h264", "aac", Streams: streams, Container: "mov", BitRate: 4_000_000));
    }

    [Fact]
    public void PlaybackDecisionExplainsContainerAudioAndCodecChanges()
    {
        const long budget = 2L * 1024 * 1024 * 1024;
        Assert.Equal("direct", PlaybackPlanner.Decide(Fake("movie.mp4", Video(), Audio()), new("id", Web), true, budget).Mode);
        Assert.Equal("direct", PlaybackPlanner.Decide(Fake("movie.mp4"), new("id", Web), false, budget).Mode);
        Assert.Equal("remux", PlaybackPlanner.Decide(Fake("movie.mkv", Video(), Audio()), new("id", Web), true, budget).Mode);
        Assert.Equal("audio-transcode", PlaybackPlanner.Decide(Fake("movie.mkv", Video(), Audio(codec: "ac3", channels: 6)), new("id", Web), true, budget).Mode);
        Assert.Equal("video-transcode", PlaybackPlanner.Decide(Fake("movie.mkv", Video("mpeg4"), Audio()), new("id", Web), true, budget).Mode);
        var second = PlaybackPlanner.Decide(Fake("movie.mp4", Video(), Audio(), Audio(2)), new("id", Web, AudioStream: 2), true, budget);
        Assert.Equal(2, second.AudioStream);
        Assert.NotEqual("direct", second.Mode);
        Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(Fake("movie.mp4", Video(), Audio()), new("id", Web, AudioStream: 99), true, budget));
        Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(Fake("movie.mkv", Video("hevc", "smpte2084"), Audio()), new("id", Web), true, budget));
    }

    [Fact]
    public void SubtitleOffsetsClipCrossingCuesAndRetainTurkishText()
    {
        const string vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:03.000\nÇağrı\n\n00:00:04.000 --> 00:00:06.000\nİkinci\n\n";
        var shifted = SubtitleService.ShiftWebVtt(vtt, -2);
        Assert.Contains("00:00:00.000 --> 00:00:01.000", shifted);
        Assert.Contains("00:00:02.000 --> 00:00:04.000", shifted);
        Assert.Contains("Çağrı", shifted);
        Assert.Equal("İstanbul", SubtitleService.DecodeText(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("İstanbul")).ToArray()));
        Assert.Throws<DomainException>(() => SubtitleService.DecodeText([0xff, 0xff, 0xff]));
    }

    [Fact]
    public async Task OutOfOrderProgressCannotUndoANewerSeekFromTheSamePlayer()
    {
        using var home = new TestWorkspace();
        home.Write("progress.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var client = Guid.NewGuid().ToString("N");
        home.Store.SaveProgress(media.Id, new PlaybackProgress(80, 120, client, 2));
        home.Store.SaveProgress(media.Id, new PlaybackProgress(10, 120, client, 1));
        Assert.Equal(80, home.Store.GetMedia(media.Id)!.PositionSeconds);
        home.Store.SaveProgress(media.Id, new PlaybackProgress(5, 120, client, 3));
        Assert.Equal(5, home.Store.GetMedia(media.Id)!.PositionSeconds);
    }

    [MediaFact]
    public async Task InterruptedClientReplacesItsSessionWithoutLeakingSlotsAndPreservesForeignCache()
    {
        using var home = new TestWorkspace();
        await FixtureAsync(home, "libx264", "aac");
        var media = await CatalogAsync(home);
        var root = AppFiles.DirectoryIn(home.Paths.Data, "playback");
        var foreign = AppFiles.DirectoryIn(root, "user-notes");
        await File.WriteAllTextAsync(Path.Combine(foreign, "keep.txt"), "user-owned");
        var stale = AppFiles.DirectoryIn(root, Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(Path.Combine(stale, ".home-playback"), "Home.Playback.v1");
        await File.WriteAllTextAsync(Path.Combine(stale, ".home-process.json"), "{truncated");
        var tools = Tools(home);
        using var manager = new PlaybackManager(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), tools, Configuration(), NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            Assert.False(Directory.Exists(stale));
            var id = Guid.NewGuid().ToString("N");
            var first = await manager.CreateAsync("owner", new(media.Id, Web, ClientId: id, ClientRevision: 10), CancellationToken.None);
            var next = await manager.CreateAsync("owner", new(media.Id, Web, StartSeconds: 6.5, ClientId: id, ClientRevision: 11), CancellationToken.None);
            Assert.NotEqual(first.Id, next.Id);
            await manager.CloseClientAsync(id, "owner", 10);
            Assert.NotNull(manager.Touch(next.Id!, "owner"));
            Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => manager.CreateAsync("owner", new(media.Id, Web, ClientId: id, ClientRevision: 9), CancellationToken.None))).StatusCode);
            Assert.Equal(410, Assert.Throws<DomainException>(() => manager.Touch(first.Id!, "owner")).StatusCode);
            Assert.Equal(429, (await Assert.ThrowsAsync<DomainException>(() => manager.CreateAsync("another", new(media.Id, Web), CancellationToken.None))).StatusCode);
            await manager.CloseClientAsync(id, "another");
            Assert.NotNull(manager.Touch(next.Id!, "owner"));
            var direct = await manager.CreateAsync("owner", new(media.Id, Web with { Containers = ["mkv"] }, ClientId: id, ClientRevision: 12), CancellationToken.None);
            Assert.Equal("direct", direct.Mode);
            Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => manager.CreateAsync("owner", new(media.Id, Web, ClientId: id, ClientRevision: 11), CancellationToken.None))).StatusCode);
            await manager.CloseClientAsync(id, "owner", 12);
            Assert.Equal("user-owned", await File.ReadAllTextAsync(Path.Combine(foreign, "keep.txt")));
            Assert.Single(Directory.GetDirectories(root));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaFact]
    public async Task CancellingANativeOperationStopsBeforeTheFullMediaDuration()
    {
        using var home = new TestWorkspace();
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-re", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "60", "-f", "wav", Path.Combine(home.Root, "cancelled.wav")],
            TimeSpan.FromSeconds(90), token.Token));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
        using var output = new FileStream(Path.Combine(home.Root, "cancelled.wav"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    [MediaFact]
    public async Task NativeProbeFindsLanguagesChaptersAndReadOnlySidecarSubtitles()
    {
        using var home = new TestWorkspace();
        var file = await FixtureAsync(home, "libx264", "aac");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var media = await CatalogAsync(home);
        var tools = Tools(home);
        var analysis = new MediaAnalysisService(home.Store, tools);
        var inspected = await analysis.InspectAsync(media.Id, CancellationToken.None);
        Assert.Equal("h264", inspected.Probe.VideoCodec);
        Assert.Contains(inspected.Probe.Streams!, s => s.Kind == "audio" && s.Language == "tr");
        Assert.Contains(inspected.Probe.Streams!, s => s.Kind == "audio" && s.Language == "en");
        Assert.NotEmpty(inspected.Probe.Chapters!);
        using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var tracks = subtitles.List(inspected);
        var sidecar = Assert.Single(tracks, t => t.Origin == "sidecar");
        Assert.Equal("tr", sidecar.Language);
        var text = await subtitles.WebVttAsync(inspected, sidecar.Id, 0, 0, CancellationToken.None);
        Assert.StartsWith("WEBVTT", text);
        Assert.Contains("Türkçe", text);
        var embedded = Assert.Single(tracks, t => t.Origin == "embedded");
        Assert.Contains("Türkçe", await subtitles.WebVttAsync(inspected, embedded.Id, 0, 0, CancellationToken.None));
        var uploaded = await subtitles.UploadAsync(media.Id, "en", "User captions.srt", "srt", Encoding.UTF8.GetBytes("1\n00:00:00,500 --> 00:00:01,500\nUser captions\n"), CancellationToken.None);
        Assert.Contains(subtitles.List(inspected), t => t.Id == "managed-" + uploaded.Id);
        subtitles.Remove(media.Id, uploaded.Id);
        Assert.DoesNotContain(subtitles.List(inspected), t => t.Id == "managed-" + uploaded.Id);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(file)));
        Assert.True(File.Exists(Path.Combine(home.Source, "fixture.tr.srt")));
        Assert.NotNull(home.Store.GetAnalysis(media.Id, inspected.Fingerprint));
    }

    [MediaTheory]
    [InlineData("libx264", "aac", false, "remux")]
    [InlineData("libx264", "ac3", false, "audio-transcode")]
    [InlineData("mpeg4", "aac", false, "video-transcode")]
    [InlineData("libx264", "aac", true, "video-transcode")]
    public async Task NativeHlsProducesPlayableCodecsAndCleansItsOwnedSession(string video, string audio, bool force, string expectedMode)
    {
        using var home = new TestWorkspace();
        var source = await FixtureAsync(home, video, audio);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var media = await CatalogAsync(home);
        var tools = Tools(home);
        var analysis = new MediaAnalysisService(home.Store, tools);
        using var manager = new PlaybackManager(home.Paths, home.Store, analysis, tools, Configuration(), NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session = await manager.CreateAsync("owner", new PlaybackStart(media.Id, Web, StartSeconds: 2.5, ForceCompatible: force), CancellationToken.None);
            Assert.Equal(expectedMode, session.Mode);
            Assert.NotNull(session.Id);
            Assert.True(session.StartSeconds <= 2.5);
            Assert.InRange(2.5 - session.StartSeconds, 0, 2.5);
            Assert.Throws<DomainException>(() => manager.OpenArtifact(session.Id, "index.m3u8", "another-owner"));
            Assert.Throws<DomainException>(() => manager.OpenArtifact(session.Id, "../../fixture.mkv", "owner"));
            var playlist = manager.OpenArtifact(session.Id, "index.m3u8", "owner");
            string content;
            using (playlist.Stream)
            using (var reader = new StreamReader(playlist.Stream)) content = await reader.ReadToEndAsync();
            Assert.Contains("#EXTM3U", content);
            var firstName = content.Split('\n').Select(line => line.Trim()).First(line => line.EndsWith(".ts", StringComparison.Ordinal));
            var segment = manager.OpenArtifact(session.Id, firstName, "owner");
            var output = Path.Combine(home.Root, "segment.ts");
            await using (segment.Stream)
            await using (var target = File.Create(output)) await segment.Stream.CopyToAsync(target);
            var probe = await tools.ProbeAsync(output, CancellationToken.None);
            Assert.NotNull(probe);
            Assert.Equal("h264", probe.VideoCodec);
            Assert.Equal("aac", probe.AudioCodec);
            if (expectedMode == "audio-transcode" || force) Assert.Equal(2, Assert.Single(probe.Streams!, s => s.Kind == "audio").Channels);
            await manager.CloseAsync(session.Id, "owner");
            Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "playback")));
            Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaFact]
    public async Task RequestedQualityProducesTheActualBoundedResolution()
    {
        using var home = new TestWorkspace();
        await FixtureAsync(home, "libx264", "aac", "1280x720");
        var media = await CatalogAsync(home);
        var tools = Tools(home);
        using var manager = new PlaybackManager(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), tools, Configuration(), NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session = await manager.CreateAsync("owner", new(media.Id, Web, MaxHeight: 480, MaxBitrate: 1_500_000), CancellationToken.None);
            Assert.Equal("video-transcode", session.Mode);
            var segment = manager.OpenArtifact(session.Id!, "segment000000.ts", "owner");
            var path = Path.Combine(home.Root, "scaled.ts");
            await using (segment.Stream)
            await using (var output = File.Create(path)) await segment.Stream.CopyToAsync(output);
            var probe = await tools.ProbeAsync(path, CancellationToken.None);
            Assert.Equal(480, probe!.Height);
            Assert.InRange(probe.Width!.Value, 852, 854);
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaFact]
    public async Task ProbeRejectsDisguisedPlaylistInputs()
    {
        using var home = new TestWorkspace();
        var file = home.Write("disguised.mp4", Encoding.UTF8.GetBytes("ffconcat version 1.0\nfile 'outside.mp4'\n"));
        Assert.Null(await Tools(home).ProbeAsync(file, CancellationToken.None));
    }

    [MediaFact]
    public async Task HttpPlaybackProtectsTracksAndSegmentsAndSupportsSegmentRanges()
    {
        using var home = new TestWorkspace();
        await FixtureAsync(home, "libx264", "aac");
        var media = await CatalogAsync(home);
        await using var factory = new PlaybackFactory(home);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/media/{media.Id}/tracks")).StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Codec test", "local-codec-password"))).EnsureSuccessStatusCode();
        session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, Web))).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        var tracks = await client.GetFromJsonAsync<MediaTracks>($"/api/media/{media.Id}/tracks");
        Assert.Equal(2, tracks!.Streams.Count(s => s.Kind == "audio"));
        var started = await client.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, Web));
        started.EnsureSuccessStatusCode();
        var playback = (await started.Content.ReadFromJsonAsync<PlaybackSession>())!;
        var playlist = await client.GetStringAsync(playback.Url);
        var segment = playlist.Split('\n').Select(line => line.Trim()).First(line => line.EndsWith(".ts", StringComparison.Ordinal));
        using var range = new HttpRequestMessage(HttpMethod.Get, $"/api/playback/{playback.Id}/{segment}");
        range.Headers.Range = new RangeHeaderValue(10, 29);
        var response = await client.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(20, (await response.Content.ReadAsByteArrayAsync()).Length);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(playback.Url)).StatusCode);
        (await client.DeleteAsync($"/api/playback/{playback.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(playback.Url)).StatusCode);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"),
        ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE"),
        ["Home:Playback:MaximumSessions"] = "1", ["Home:Playback:CacheGiBPerSession"] = "0.1"
    }).Build();
    private sealed class PlaybackFactory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(Configuration()).AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:DemoPath"] = ""
            }));
        }
    }
    private static MediaTools Tools(TestWorkspace home) => new(Configuration(), home.Paths, NullLogger<MediaTools>.Instance);
    private static async Task<MediaItem> CatalogAsync(TestWorkspace home)
    {
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        return Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
    }
    private static async Task<string> FixtureAsync(TestWorkspace home, string video, string audio, string size = "320x180")
    {
        var subtitles = Path.Combine(home.Source, "fixture.tr.srt");
        await File.WriteAllTextAsync(subtitles, "1\n00:00:00,500 --> 00:00:04,000\nTürkçe altyazı\n\n2\n00:00:05,000 --> 00:00:09,000\nİkinci satır\n", new UTF8Encoding(false));
        var chapters = Path.Combine(home.Root, "chapters.txt");
        await File.WriteAllTextAsync(chapters, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=4000\ntitle=Chapter one\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=4000\nEND=12000\ntitle=Chapter two\n", new UTF8Encoding(false));
        var output = Path.Combine(home.Source, "fixture.mkv");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=12",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-f", "ffmetadata", "-i", chapters, "-i", subtitles,
            "-t", "12", "-map", "0:v:0", "-map", "1:a:0", "-map", "1:a:0", "-map", "3:s:0", "-map_chapters", "2", "-c:v", video,
            "-g", "24", "-pix_fmt", "yuv420p", "-threads", "1", "-c:a", audio, "-b:a", "96k", "-c:s", "srt",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=tur", "-metadata:s:s:0", "language=tur" };
        if (video == "libx264") args.AddRange(["-preset", "ultrafast"]);
        args.Add(output);
        await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!, args, TimeSpan.FromSeconds(30), CancellationToken.None);
        return output;
    }
}

public sealed class MediaFactAttribute : FactAttribute
{
    public MediaFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")) || !File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")))
            Skip = "Set HOME_TEST_FFMPEG and HOME_TEST_FFPROBE to run native codec checks.";
    }
}
public sealed class MediaTheoryAttribute : TheoryAttribute
{
    public MediaTheoryAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")) || !File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")))
            Skip = "Set HOME_TEST_FFMPEG and HOME_TEST_FFPROBE to run native codec checks.";
    }
}
