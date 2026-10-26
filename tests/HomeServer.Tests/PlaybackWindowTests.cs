using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Tests;

public sealed class PlaybackWindowTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private static void Publish(string root, int first, int last)
    {
        var text = new StringBuilder($"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:4\n#EXT-X-MEDIA-SEQUENCE:{first}\n#EXT-X-MAP:URI=\"init.mp4\"\n");
        var initial = !File.Exists(Path.Combine(root, "index.m3u8"));
        for (var i = first; i <= last; i++) { var name = $"segment{i:D6}.m4s"; if (initial || i == last) File.WriteAllText(Path.Combine(root, name), "isolated segment fixture"); text.Append("#EXTINF:4,\n").Append(name).Append('\n'); }
        File.WriteAllText(Path.Combine(root, "index.m3u8"), text.ToString());
    }
    [Fact]
    public void WindowRetainsOldAdvertisementsThenDeletesOnlyExpiredOwnedSegments()
    {
        using var home = new TestWorkspace(); var root = AppFiles.DirectoryIn(home.Root, "window"); var clock = new Clock();
        File.WriteAllText(Path.Combine(root, "notes.txt"), "original notes"); Publish(root, 0, 9);
        var window = new PlaybackWindow(root, 1024 * 1024, 100, 100, clock, 12);
        using (var list = window.Playlist()) using (var reader = new StreamReader(list)) Assert.Contains("#EXT-X-MEDIA-SEQUENCE:0", reader.ReadToEnd());
        window.Touch(128); window.Control(false);
        using (var list = window.Playlist()) using (var reader = new StreamReader(list)) { var text = reader.ReadToEnd(); Assert.DoesNotContain("segment000000", text); Assert.Contains("segment000006", text); }
        window.RequireArtifact("segment000000.m4s"); Assert.True(File.Exists(Path.Combine(root, "segment000000.m4s")));
        clock.Now = clock.Now.AddSeconds(45); window.Control(false);
        Assert.Equal(410, Assert.Throws<DomainException>(() => window.RequireArtifact("segment000000.m4s")).StatusCode);
        Assert.False(File.Exists(Path.Combine(root, "segment000000.m4s"))); Assert.Equal("original notes", File.ReadAllText(Path.Combine(root, "notes.txt")));
        Assert.Throws<DomainException>(() => window.RequireArtifact("../notes.txt"));
    }
    [Fact]
    public void DeletedSegmentMetadataKeepsTheMovingProducerTimelineAnchored()
    {
        using var home = new TestWorkspace(); var root = AppFiles.DirectoryIn(home.Root, "moving"); var clock = new Clock();
        var window = new PlaybackWindow(root, 1024 * 1024, 0, 0, clock, 12);
        for (var last = 0; last < 270; last++)
        {
            Publish(root, Math.Max(0, last - 127), last); window.Touch(Math.Max(0, last * 4 - 8)); window.Control(false);
            clock.Now = clock.Now.AddSeconds(4);
        }
        Assert.Equal(1080, window.Telemetry(false).WindowEndSeconds);
        using var list = window.Playlist(); using var reader = new StreamReader(list); Assert.Contains("segment000269.m4s", reader.ReadToEnd());
    }
    [Fact]
    public void InitialSeekKeepsTimestampOriginUntilPlaybackAdvancesAndWaitsForRequestedContent()
    {
        using var home = new TestWorkspace(); var root = AppFiles.DirectoryIn(home.Root, "seek");
        var window = new PlaybackWindow(root, 1024 * 1024, 100, 140, aheadSeconds: 12);
        Publish(root, 0, 2); Assert.False(window.Ready); Assert.False(window.Control(false));
        Publish(root, 0, 12); Assert.True(window.Ready); window.Control(false);
        using (var list = window.Playlist()) using (var reader = new StreamReader(list)) Assert.Contains("#EXT-X-MEDIA-SEQUENCE:0", reader.ReadToEnd());
        window.Touch(140); window.Control(false);
        using (var list = window.Playlist()) using (var reader = new StreamReader(list)) Assert.Contains("#EXT-X-MEDIA-SEQUENCE:0", reader.ReadToEnd());
        window.Touch(144); window.Control(false);
        using (var list = window.Playlist()) using (var reader = new StreamReader(list)) Assert.DoesNotContain("#EXT-X-MEDIA-SEQUENCE:0\n", reader.ReadToEnd());
    }
    [Fact]
    public void ResumeUsesTheConsumerPlayheadNotJustTheLastPublishedSegmentBoundary()
    {
        using var home = new TestWorkspace(); var root = AppFiles.DirectoryIn(home.Root, "in-flight-progress");
        Publish(root, 0, 2);
        var window = new PlaybackWindow(root, 1024 * 1024, 0, 0, aheadSeconds: 12);
        window.Progress(16.032, 4); Assert.True(window.Control(false));
        var published = window.Telemetry(true).WindowEndSeconds;
        window.Touch(published - 4); Assert.True(window.Control(true));
        window.Touch(published - .25); Assert.False(window.Control(true));
        Assert.Equal(published, window.Telemetry(false).WindowEndSeconds);
    }
    [Fact]
    public void DecoderConfigurationRecordsProvideRealProfileAndBitDepthIdentifiers()
    {
        var probe = MediaTools.ParseProbe("""
            {"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","profile":"Main 10","level":153,"pix_fmt":"yuv420p10le","avg_frame_rate":"24000/1001","extradata":"\n00000000: 0102 2000 0000 b000 0000 0000 99            ..fixture\n"}],"format":{"duration":"10"}}
            """);
        var stream = Assert.Single(probe.Streams!); Assert.Equal("hvc1.2.4.L153.B0", stream.CodecString); Assert.Equal(10, stream.BitDepth); Assert.InRange(stream.FrameRate!.Value, 23.97, 23.98);
        using var data = JsonDocument.Parse("""{"codec_name":"h264","extradata":"\n00000000: 0164 0029 ff                           .d.)\n"}""");
        Assert.Equal("avc1.640029", CodecIdentifiers.FromProbe(data.RootElement));
    }
}
