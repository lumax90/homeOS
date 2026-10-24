using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class PlaybackEfficiencyTests
{
    private static AnalyzedMedia Source(string container = "mkv", string audio = "dts", bool hdr = false)
    {
        var video = new MediaStreamInfo(0, "video", "hevc", "und", "", true, false, false, 3840, 2160, null, null, null, 40_000_000, "Main 10", "yuv420p10le", hdr ? "smpte2084" : null, hdr ? "bt2020" : null);
        var sound = new MediaStreamInfo(1, "audio", audio, "en", "", true, false, false, null, null, 6, "5.1", 48000, 640000, null, null, null, null);
        var media = new MediaItem("fixture", "library", "Isolated library", "Isolated large movie", "video", "fixture." + container, "video/mp4", 36_000_000_000, 7200, 3840, 2160, "hevc", audio, true, "", "", false, 0, false, false);
        return new(media, media.RelativePath, "fixture-fingerprint", new(7200, 3840, 2160, "hevc", audio, Streams: [video, sound], BitRate: 40_640_000));
    }
    private static PlaybackClient Client(AnalyzedMedia source, params PlaybackFormatSupport[] formats) => new(["mp4"], ["h264", "hevc"], ["aac"], true, 2,
        new(source.Fingerprint, source.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "video")?.Index, source.Probe.Streams?.FirstOrDefault(stream => stream.Kind == "audio")?.Index, formats));

    [Fact]
    public void LargeHevcMovieOnlyConvertsItsUnsupportedAudioAndKeepsHdrAndResolution()
    {
        var source = Source(hdr: true);
        var client = Client(source, new PlaybackFormatSupport("mp4", "hls.js", false, true, false, true));
        var plan = PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100 * 1024 * 1024);
        Assert.Equal("audio-transcode", plan.Mode); Assert.True(plan.CopyVideo); Assert.False(plan.CopyAudio);
        Assert.Equal("hevc", plan.VideoCodec); Assert.Equal("aac", plan.AudioCodec); Assert.Equal(2160, plan.Height);
        Assert.Equal("mp4", plan.Container); Assert.False(plan.ToneMap);
    }
    [Fact]
    public void SourcePreservationRanksAboveTransportPreferenceAndLegacyStereoHint()
    {
        var source = Source(audio: "aac");
        var client = Client(source, new("mp4", "hls.js", false, false, false, true), new("mp4", "native-hls", true, true, false, true));
        var plan = PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100_000);
        Assert.Equal("remux", plan.Mode); Assert.True(plan.CopyAudio); Assert.Equal("native-hls", plan.Transport);
    }
    [Fact]
    public void RemuxRetryDoesNotForceVideoConversionButExplicitQualityAndBurningDo()
    {
        var source = Source("mp4", "aac");
        var client = Client(source, new("mp4", "direct", true, false, false, false), new("mp4", "hls.js", true, true, true, true));
        Assert.Equal("direct", PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100_000).Mode);
        Assert.Equal("remux", PlaybackPlanner.Decide(source, new(source.Media.Id, client, ForceRemux: true), true, 100_000).Mode);
        Assert.Equal("video-transcode", PlaybackPlanner.Decide(source, new(source.Media.Id, client, MaxHeight: 1080), true, 100_000).Mode);
        var subtitle = new SubtitleTrack("sub", "en", "Fixture captions", "pgs", "embedded", false, false, false, Burnable: true, Image: true);
        Assert.Equal("video-transcode", PlaybackPlanner.Decide(source, new(source.Media.Id, client, SubtitleId: "sub"), true, 100_000, selectedSubtitle: subtitle).Mode);
    }
    [Fact]
    public void UnsupportedVideoReallyTranscodesAndHdrStillRequiresToneMapping()
    {
        var source = Source(hdr: true); var client = Client(source, new PlaybackFormatSupport("mp4", "hls.js", false, false, false, true));
        Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100_000));
        var plan = PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100_000, toneMappingAvailable: true);
        Assert.Equal("video-transcode", plan.Mode); Assert.True(plan.ToneMap); Assert.Equal(1080, plan.Height);
    }
    [Fact]
    public void CapabilitiesCannotBeReusedForAnotherFileOrAudioSelection()
    {
        var source = Source(); var client = Client(source, new PlaybackFormatSupport("mp4", "hls.js", true, true, true, true));
        Assert.Equal(409, Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(source with { Fingerprint = "changed" }, new(source.Media.Id, client), true, 100_000)).StatusCode);
        Assert.Equal(409, Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(source, new(source.Media.Id, client with { Capabilities = client.Capabilities! with { AudioStream = 7 } }), true, 100_000)).StatusCode);
        Assert.Throws<DomainException>(() => PlaybackPlanner.Decide(source, new(source.Media.Id, client with { Capabilities = client.Capabilities! with { Formats = [new("../outside", "hls.js", true, true, true, true)] } }), true, 100_000));
    }
    [Fact]
    public void H264RemuxNoLongerDependsOnTheRemainingMovieSize()
    {
        var source = Source(audio: "aac"); var streams = source.Probe.Streams!;
        source = source with { Probe = source.Probe with { Streams = [streams[0] with { Codec = "h264", PixelFormat = "yuv420p" }, streams[1] with { Channels = 2 }] } };
        var client = new PlaybackClient(["mp4"], ["h264"], ["aac"], true);
        Assert.Equal("remux", PlaybackPlanner.Decide(source, new(source.Media.Id, client), true, 100 * 1024 * 1024).Mode);
    }

    [MediaFact]
    public async Task RealHevcHdrFmp4KeepsVideoPropertiesConvertsAudioAndPreservesSourceBytes()
    {
        using var home = new TestWorkspace(); var source = await Generate(home, true); var path = Path.Combine(home.Source, source.RelativePath);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path)); var tools = Tools(home); var analysis = new MediaAnalysisService(home.Store, tools);
        var inspected = await analysis.InspectAsync(source.Id, CancellationToken.None);
        Assert.StartsWith("hvc1.2.", inspected.Probe.Streams![0].CodecString);
        using var manager = new PlaybackManager(home.Paths, home.Store, analysis, tools, Configuration(), NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var client = Client(inspected, new PlaybackFormatSupport("mp4", "hls.js", false, true, false, true));
            var session = await manager.CreateAsync("fixture-owner", new(source.Id, client), CancellationToken.None);
            Assert.Equal("audio-transcode", session.Mode); Assert.Equal("copy", session.Encoder);
            var playlist = manager.OpenArtifact(session.Id!, "index.m3u8", "fixture-owner");
            using (playlist.Stream) using (var reader = new StreamReader(playlist.Stream)) Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"", await reader.ReadToEndAsync());
            var sample = Path.Combine(home.Root, "isolated-remux.mp4");
            await using (var output = File.Create(sample))
                foreach (var name in new[] { "init.mp4", "segment000000.m4s" })
                { var part = manager.OpenArtifact(session.Id!, name, "fixture-owner"); await using (part.Stream) await part.Stream.CopyToAsync(output); }
            var probe = (await tools.ProbeAsync(sample, CancellationToken.None))!;
            Assert.Equal("hevc", probe.VideoCodec); Assert.Equal("aac", probe.AudioCodec);
            var video = probe.Streams!.Single(stream => stream.Kind == "video"); var audio = probe.Streams!.Single(stream => stream.Kind == "audio");
            Assert.Equal("yuv420p10le", video.PixelFormat); Assert.Equal("smpte2084", video.ColorTransfer); Assert.Equal("bt2020", video.ColorPrimaries); Assert.Equal(2, audio.Channels);
            Assert.Throws<DomainException>(() => manager.OpenArtifact(session.Id!, "init.mp4", "other-owner"));
            await manager.CloseAsync(session.Id!, "fixture-owner"); Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaFact]
    public async Task ProductionWaitsForTheViewerThenResumesAndCanCloseWhileSuspended()
    {
        using var home = new TestWorkspace(); var source = await Generate(home, false, 180); var tools = Tools(home); var analysis = new MediaAnalysisService(home.Store, tools);
        var inspected = await analysis.InspectAsync(source.Id, CancellationToken.None);
        using var manager = new PlaybackManager(home.Paths, home.Store, analysis, tools, Configuration(), NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session = await manager.CreateAsync("fixture-owner", new(source.Id, Client(inspected, new PlaybackFormatSupport("mp4", "hls.js", true, true, true, true))), CancellationToken.None);
            async Task<PlaybackSession> Wait(Func<PlaybackSession, bool> condition)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (true) { var state = manager.Describe(session.Id!, "fixture-owner"); Assert.Null(state.Error); if (condition(state)) return state; await Task.Delay(100, timeout.Token); }
            }
            var paused = await Wait(state => state.Telemetry?.Throttled == true);
            await Task.Delay(600); Assert.Equal(paused.Telemetry!.WindowEndSeconds, manager.Describe(session.Id!, "fixture-owner").Telemetry!.WindowEndSeconds);
            manager.Touch(session.Id!, "fixture-owner", new(Math.Max(0, paused.Telemetry.WindowEndSeconds - 4), false));
            await Wait(state => state.Telemetry!.WindowEndSeconds > paused.Telemetry.WindowEndSeconds + 3);
            await Wait(state => state.Telemetry!.Throttled);
            await manager.CloseAsync(session.Id!, "fixture-owner").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(home.Paths.Data, "playback")));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaFact]
    public async Task HttpFmp4InitAndSegmentsRequireALeaseAndHonorRanges()
    {
        using var home = new TestWorkspace(); var source = await Generate(home, true); var tools = Tools(home); var inspected = await new MediaAnalysisService(home.Store, tools).InspectAsync(source.Id, CancellationToken.None);
        await using var factory = new Factory(home); using var client = factory.CreateClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Fixture owner", "isolated-efficiency-password"))).EnsureSuccessStatusCode();
        session = await client.GetFromJsonAsync<JsonElement>("/api/session"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        var response = await client.PostAsJsonAsync("/api/playback", new PlaybackStart(source.Id, Client(inspected, new PlaybackFormatSupport("mp4", "hls.js", false, true, false, true))));
        response.EnsureSuccessStatusCode(); var playback = (await response.Content.ReadFromJsonAsync<PlaybackSession>())!;
        using var anonymous = factory.CreateClient();
        foreach (var name in new[] { "init.mp4", "segment000000.m4s" })
        {
            var url = $"/api/playback/{playback.Id}/{name}";
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Range = new RangeHeaderValue(0, 15);
            var partial = await client.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode); Assert.Equal(16, (await partial.Content.ReadAsByteArrayAsync()).Length);
        }
        (await client.DeleteAsync($"/api/playback/{playback.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/playback/{playback.Id}/init.mp4")).StatusCode);
    }
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE"),
        ["Home:Playback:AheadSeconds"] = "12", ["Home:Playback:CacheGiBPerSession"] = "0.1"
    }).Build();
    private static MediaTools Tools(TestWorkspace home) => new(Configuration(), home.Paths, NullLogger<MediaTools>.Instance);
    private static async Task<MediaItem> Generate(TestWorkspace home, bool hevc, int duration = 10)
    {
        var path = Path.Combine(home.Source, "isolated-playback-fixture.mkv");
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=128x72:rate=12", "-f", "lavfi", "-i", "anullsrc=channel_layout=5.1:sample_rate=48000", "-t", duration.ToString(),
            "-threads", "1", "-c:v", hevc ? "libx265" : "libx264", "-preset", "ultrafast", "-g", "12", "-pix_fmt", hevc ? "yuv420p10le" : "yuv420p", "-c:a", "ac3", "-b:a", "384k" };
        if (hevc) arguments.AddRange(["-x265-params", "pools=1:frame-threads=1:log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc"]);
        arguments.Add(path); await MediaProcess.RunAsync(Tools(home).FfmpegExecutable, arguments, TimeSpan.FromSeconds(40), CancellationToken.None);
        var library = home.AddLibrary(); await home.Scan(library.Id); return Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
    }
    private sealed class Factory(TestWorkspace home) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddConfiguration(Configuration()).AddInMemoryCollection(new Dictionary<string, string?>
        { ["Home:DataPath"] = home.Paths.Data, ["Home:Downloads:LoopbackOnly"] = "true", ["Home:DemoPath"] = "" }));
    }
}
