using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class EncodingTests
{
    [NvidiaFact]
    public async Task VerifiedNvencProfileCreatesRealH264Segments()
    {
        using var home = new TestWorkspace(); var tools = Tools(home);
        var media = await Generate(home, false, "bt709");
        var support = new EncodingSupport(tools, home.Store); support.SetPreference("nvenc");
        Assert.True((await support.InspectAsync(CancellationToken.None)).Nvenc);
        using var manager = new PlaybackManager(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), tools, Config(), NullLogger<PlaybackManager>.Instance, support);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session = await manager.CreateAsync("owner", new PlaybackStart(media.Id, Client(), ForceCompatible: true), CancellationToken.None);
            Assert.Equal("h264_nvenc", session.Encoder);
            var probe = await SegmentProbe(home, manager, tools, session);
            Assert.Equal("h264", probe.VideoCodec);
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }

    [MediaTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public async Task HdrTransferIsConvertedToTaggedSdr(string transfer)
    {
        using var home = new TestWorkspace(); var tools = Tools(home);
        var media = await Generate(home, true, transfer);
        var original = await tools.ProbeAsync(Path.Combine(home.Source, "fixture.mkv"), CancellationToken.None);
        Assert.Equal(transfer, Assert.Single(original!.Streams!, stream => stream.Kind == "video").ColorTransfer);
        var support = new EncodingSupport(tools, home.Store); support.SetPreference("software");
        Assert.True((await support.InspectAsync(CancellationToken.None)).ToneMapping);
        using var manager = new PlaybackManager(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), tools, Config(), NullLogger<PlaybackManager>.Instance, support);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session = await manager.CreateAsync("owner", new PlaybackStart(media.Id, Client()), CancellationToken.None);
            var probe = await SegmentProbe(home, manager, tools, session);
            var video = Assert.Single(probe.Streams!, stream => stream.Kind == "video");
            Assert.Equal("bt709", video.ColorTransfer); Assert.Equal("bt709", video.ColorPrimaries); Assert.Equal("yuv420p", video.PixelFormat);
            Assert.Contains(session.Reasons, reason => reason.Contains("HDR10/HLG"));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }
    private static async Task<ProbeResult> SegmentProbe(TestWorkspace home, PlaybackManager manager, MediaTools tools, PlaybackSession session)
    {
        var artifact = manager.OpenArtifact(session.Id!, "segment000000.ts", "owner");
        var path = Path.Combine(home.Root, "encoded.ts");
        await using (artifact.Stream)
        await using (var output = File.Create(path)) await artifact.Stream.CopyToAsync(output);
        return (await tools.ProbeAsync(path, CancellationToken.None))!;
    }
    private static PlaybackClient Client() => new(["mp4"], ["h264"], ["aac"], true);
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")
    }).Build();
    private static MediaTools Tools(TestWorkspace home) => new(Config(), home.Paths, NullLogger<MediaTools>.Instance);
    private static async Task<MediaItem> Generate(TestWorkspace home, bool hdr, string transfer)
    {
        var file = Path.Combine(home.Source, "fixture.mkv");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=256x144:rate=12", "-t", "4", "-threads", "1",
            "-c:v", hdr ? "libx265" : "libx264", "-preset", "ultrafast", "-pix_fmt", hdr ? "yuv420p10le" : "yuv420p" };
        if (hdr) args.AddRange(["-x265-params", $"pools=1:frame-threads=1:log-level=error:colorprim=bt2020:transfer={transfer}:colormatrix=bt2020nc"]);
        args.Add(file);
        await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!, args, TimeSpan.FromSeconds(30), CancellationToken.None);
        var library = home.AddLibrary(); await home.Scan(library.Id);
        return Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
    }
}
public sealed class NvidiaFactAttribute : FactAttribute
{
    public NvidiaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HOME_TEST_NVENC") != "1") Skip = "Set HOME_TEST_NVENC=1 only on an NVENC test device.";
    }
}
