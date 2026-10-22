using System.Buffers.Binary;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class SubtitleBurnTests
{
    private static PlaybackClient Client => new(["mkv", "mp4"], ["h264"], ["aac"], true);
    [MediaTheory]
    [InlineData("copy", "hdmv_pgs_subtitle")]
    [InlineData("dvdsub", "dvd_subtitle")]
    public async Task NativeBitmapCaptionsSurviveBurnInDelaySeekAndSessionCleanup(string codec, string expectedCodec)
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var sup = Path.Combine(home.Root, "fixture.sup"); await File.WriteAllBytesAsync(sup, Pgs());
        var video = Path.Combine(home.Source, "Bitmap.mkv");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24", "-fix_sub_duration", "-i", sup, "-t", "4", "-map", "0:v:0", "-map", "1:s:0", "-c:v", "libx264", "-threads", "1", "-g", "12", "-pix_fmt", "yuv420p", "-c:s", codec, "-metadata:s:s:0", "language=eng", video], TimeSpan.FromSeconds(20), CancellationToken.None);
        var hash = StateFolders.Hash(video); var media = await Catalog(home, tools); var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var inspected = await analysis.InspectAsync(media.Id, CancellationToken.None); var track = Assert.Single(subtitles.List(inspected)); Assert.Equal(expectedCodec, track.Format); Assert.True(track.Image); Assert.True(track.Burnable); Assert.False(track.Playable);
        Assert.InRange(inspected.Probe.Duration!.Value, 3.9, 5);
        using var manager = new PlaybackManager(home.Paths, home.Store, analysis, tools, Configuration(), NullLogger<PlaybackManager>.Instance, subtitles: subtitles);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var plain = await manager.CreateAsync("owner", new(media.Id, Client), CancellationToken.None); Assert.Equal("direct", plain.Mode);
            var burning = await manager.CreateAsync("owner", new(media.Id, Client, SubtitleId: track.Id), CancellationToken.None);
            Assert.True(burning.SubtitleBurned); Assert.Equal("video-transcode", burning.Mode);
            await Completed(manager, burning.Id!); Assert.True(await BrightPixels(tools, home, burning.Id!, 1.25) > 200);
            Assert.Equal(0, await BrightPixels(tools, home, burning.Id!, 3.25));
            await manager.CloseAsync(burning.Id!, "owner");
            var seek = await manager.CreateAsync("owner", new(media.Id, Client, StartSeconds: 1.25, SubtitleId: track.Id), CancellationToken.None);
            await Completed(manager, seek.Id!); Assert.True(await BrightPixels(tools, home, seek.Id!, .15) > 200); await manager.CloseAsync(seek.Id!, "owner");
            var delayed = await manager.CreateAsync("owner", new(media.Id, Client, SubtitleId: track.Id, SubtitleDelaySeconds: 1), CancellationToken.None);
            await Completed(manager, delayed.Id!); Assert.Equal(0, await BrightPixels(tools, home, delayed.Id!, 1.0)); Assert.True(await BrightPixels(tools, home, delayed.Id!, 2.0) > 200);
            await manager.CloseAsync(delayed.Id!, "owner"); Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "playback")));
        }
        finally { await manager.StopAsync(CancellationToken.None); }
        Assert.Equal(hash, StateFolders.Hash(video));
    }
    [MediaFact]
    public async Task NativeTextStyleBurnUsesOriginalCueTimeWhenSeeking()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var video = Path.Combine(home.Source, "Text.mkv");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24", "-t", "4", "-c:v", "libx264", "-threads", "1", "-g", "12", video], TimeSpan.FromSeconds(20), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(home.Source, "Text.en.ass"), """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 320
            PlayResY: 180
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Fixture,Arial,26,&H000000FF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.50,0:00:02.50,Fixture,,0,0,0,,{\pos(20,20)}CAPTION
            """, Encoding.UTF8);
        var media = await Catalog(home, tools); var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var track = Assert.Single(subtitles.List(await analysis.InspectAsync(media.Id, CancellationToken.None)));
        using var manager = new PlaybackManager(home.Paths, home.Store, analysis, tools, Configuration(), NullLogger<PlaybackManager>.Instance, subtitles: subtitles);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var playback = await manager.CreateAsync("owner", new(media.Id, Client, StartSeconds: 1, SubtitleId: track.Id, BurnSubtitle: true), CancellationToken.None);
            await Completed(manager, playback.Id!); var pixels = await Frame(tools, home, playback.Id!, .3);
            var red = 0;
            for (var y = 0; y < 90; y++) for (var x = 0; x < 320; x++) { var index = (y * 320 + x) * 3; if (pixels[index] > 180 && pixels[index + 1] < 90 && pixels[index + 2] < 90) red++; }
            Assert.True(red > 30, "ASS color and top-of-frame position must survive conversion and seeking."); await manager.CloseAsync(playback.Id!, "owner");
            var direct = await manager.CreateAsync("owner", new(media.Id, Client), CancellationToken.None); Assert.Equal("direct", direct.Mode); Assert.False(direct.SubtitleBurned);
        }
        finally { await manager.StopAsync(CancellationToken.None); }
    }
    private static async Task<int> BrightPixels(MediaTools tools, TestWorkspace home, string id, double seconds)
        => (await Frame(tools, home, id, seconds)).Count(value => value > 200);
    private static async Task<byte[]> Frame(MediaTools tools, TestWorkspace home, string id, double seconds)
    {
        var output = Path.Combine(home.Root, Guid.NewGuid().ToString("N") + ".rgb");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", Path.Combine(home.Paths.Data, "playback", id, "index.m3u8"), "-ss", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", output], TimeSpan.FromSeconds(10), CancellationToken.None);
        var pixels = await File.ReadAllBytesAsync(output); Assert.Equal(320 * 180 * 3, pixels.Length); return pixels;
    }
    private static async Task Completed(PlaybackManager manager, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true) { var state = manager.Describe(id, "owner"); Assert.Null(state.Error); if (state.State == "completed") return; await Task.Delay(25, timeout.Token); }
    }
    internal static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") }).Build();
    internal static MediaTools Tools(TestWorkspace home) => new(Configuration(), home.Paths, NullLogger<MediaTools>.Instance);
    internal static async Task<MediaItem> Catalog(TestWorkspace home, MediaTools tools)
    { var library = home.AddLibrary(); home.Store.QueueScan(library.Id); await new LibraryScanner(home.Store, tools, new EventHub()).ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); return Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); }
    // An isolated PGS display set: a white subtitle bitmap, followed by a clear display set. No external media fixture.
    private static byte[] Pgs()
    {
        using var stream = new MemoryStream();
        void Packet(double seconds, byte type, byte[] payload)
        {
            var header = new byte[13]; header[0] = (byte)'P'; header[1] = (byte)'G'; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), (uint)(seconds * 90000));
            header[10] = type; BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(11), (ushort)payload.Length); stream.Write(header); stream.Write(payload);
        }
        byte[] Composition(ushort number, bool show)
        {
            var bytes = new byte[show ? 19 : 11]; BinaryPrimitives.WriteUInt16BigEndian(bytes, 320); BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 180); bytes[4] = 0x10;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(5), number); bytes[7] = show ? (byte)0x80 : (byte)0; bytes[10] = show ? (byte)1 : (byte)0;
            if (show) { BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(15), 40); BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(17), 120); } return bytes;
        }
        Packet(.5, 0x16, Composition(0, true));
        Packet(.5, 0x17, [1, 0, 0, 40, 0, 120, 0, 160, 0, 30]);
        Packet(.5, 0x14, [0, 0, 0, 16, 128, 128, 0, 1, 235, 128, 128, 255]);
        var rle = Enumerable.Range(0, 30).SelectMany(_ => new byte[] { 0, 0xc0, 160, 1, 0, 0 }).ToArray();
        var objectData = new byte[11 + rle.Length]; objectData[3] = 0xc0; var length = rle.Length + 4; objectData[4] = (byte)(length >> 16); objectData[5] = (byte)(length >> 8); objectData[6] = (byte)length;
        BinaryPrimitives.WriteUInt16BigEndian(objectData.AsSpan(7), 160); BinaryPrimitives.WriteUInt16BigEndian(objectData.AsSpan(9), 30); rle.CopyTo(objectData, 11);
        Packet(.5, 0x15, objectData); Packet(.5, 0x80, []); Packet(2.5, 0x16, Composition(1, false)); Packet(2.5, 0x80, []); return stream.ToArray();
    }
}
