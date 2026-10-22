using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Subtitles;

namespace HomeServer.Tests;

public sealed class SubtitleSyncTests
{
    [Fact]
    public void OffsetAndKnownFrameRateCorrectionUseSpeechEvidenceAndRejectInsufficientEvidence()
    {
        var intervals = new[] { (2d, 3.4), (9d, 12.8), (25d, 27.2), (45d, 49.5) };
        var voice = Activity(intervals, 60); var cues = intervals.Select(item => new SubtitleCue(item.Item1 + 4.2, item.Item2 + 4.2, ["Fixture"])).ToArray();
        var offset = SubtitleAligner.Align(voice, cues, 10, false, CancellationToken.None); Assert.True(offset.CanApply, offset.Reason); Assert.InRange(offset.OffsetSeconds, -4.3, -4.1);
        var longer = new[] { (5d, 7d), (43d, 48d), (360d, 371d), (601d, 611d), (920d, 925d), (1170d, 1180d) };
        var stretched = longer.Select(item => new SubtitleCue(item.Item1 * 25 / 24 + 3.5, item.Item2 * 25 / 24 + 3.5, ["Fixture"])).ToArray();
        var result = SubtitleAligner.Align(Activity(longer, 1200), stretched, 15, true, CancellationToken.None);
        Assert.True(result.CanApply, result.Reason); Assert.Equal(24d / 25, result.TimeScale, 6); Assert.InRange(result.OffsetSeconds, -3.5, -3.2);
        Assert.False(SubtitleAligner.Align(new(new float[100], .1, 10), cues, 10, false, CancellationToken.None).CanApply);
        Assert.Contains("00:00:00.000 --> 00:00:01.000", SubtitleTiming.Transform([new(1, 3, ["Türkçe"])], -2, 1));
    }
    [Fact]
    public void PackagedPinnedVadModelInitializesOnCpu()
    {
        using var home = new TestWorkspace(); using var vad = new SileroVad(SubtitleBurnTests.Tools(home)); var result = vad.Capabilities(); Assert.True(result.Available, result.Error); Assert.Contains("CPU", result.Runtime);
    }
    [WindowsSpeechFact]
    public async Task LocalSpeechProducesARealVadAlignmentAndRecoverableSubtitleVersion()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = SubtitleBurnTests.Tools(home);
        var spoken = Path.Combine(home.Root, "spoken.wav"); Speak(spoken);
        var raw = Path.Combine(home.Root, "spoken.pcm"); await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", spoken, "-ac", "1", "-ar", "16000", "-f", "s16le", raw], TimeSpan.FromSeconds(15), CancellationToken.None);
        var speech = await File.ReadAllBytesAsync(raw); var speechSeconds = speech.Length / 32000d; Assert.InRange(speechSeconds, 1, 8);
        var starts = new[] { 2d, 13d, 25d, 38d }; var timeline = new byte[50 * 32000]; foreach (var start in starts) speech.CopyTo(timeline, (int)(start * 32000));
        var audio = Path.Combine(home.Root, "audio.wav"); Wave(audio, timeline);
        var video = Path.Combine(home.Source, "speech.mkv"); await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=256x144:r=10", "-i", audio, "-t", "50", "-c:v", "libx264", "-threads", "1", "-c:a", "pcm_s16le", video], TimeSpan.FromSeconds(30), CancellationToken.None);
        var cues = starts.Select(start => new SubtitleCue(start + 4, start + 4 + speechSeconds, ["ISOLATED SPEECH FIXTURE"])).ToArray(); var original = SubtitleTiming.Transform(cues, 0, 1);
        var subtitlePath = Path.Combine(home.Source, "speech.en.vtt"); await File.WriteAllTextAsync(subtitlePath, original, new UTF8Encoding(false));
        var videoHash = StateFolders.Hash(video); var subHash = StateFolders.Hash(subtitlePath); var media = await SubtitleBurnTests.Catalog(home, tools);
        var analysis = new MediaAnalysisService(home.Store, tools); using var subtitles = new SubtitleService(home.Paths, home.Store, tools); using var vad = new SileroVad(tools);
        var inspected = await analysis.InspectAsync(media.Id, CancellationToken.None); var track = Assert.Single(subtitles.List(inspected));
        var service = new SubtitleSyncService(home.Store, analysis, subtitles, vad, new EventHub());
        var queued = await service.QueueAsync(media.Id, new(track.Id, MaximumOffsetSeconds: 10), CancellationToken.None); Assert.True(await service.RunOnceAsync(CancellationToken.None));
        var job = home.Store.GetSubtitleSync(queued.Id)!; Assert.Equal("proposed", job.Status); Assert.NotNull(job.Result); Assert.True(job.Result.CanApply, job.Result.Reason);
        Assert.InRange(job.Result.OffsetSeconds, -4.8, -3.2); Assert.True(job.Result.VoiceSeconds > 5);
        var applied = await service.ApplyAsync(job.Id, job.Revision, CancellationToken.None); Assert.Equal("sync", applied.Origin);
        Assert.Equal(applied.Id, (await service.ApplyAsync(job.Id, job.Revision, CancellationToken.None)).Id); Assert.Single(home.Store.GetManagedSubtitles(media.Id));
        Assert.Equal(videoHash, StateFolders.Hash(video)); Assert.Equal(subHash, StateFolders.Hash(subtitlePath));
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "subtitle-work")));
        var second = await service.QueueAsync(media.Id, new(track.Id, MaximumOffsetSeconds: 11), CancellationToken.None); home.Store.CancelSubtitleSync(second.Id);
        Assert.False(await service.RunOnceAsync(CancellationToken.None)); Assert.Equal("cancelled", home.Store.GetSubtitleSync(second.Id)!.Status);
    }
    private static VoiceActivity Activity((double, double)[] intervals, int duration)
    {
        var values = new float[duration * 10]; foreach (var (start, end) in intervals) for (var i = (int)Math.Round(start * 10); i < (int)Math.Round(end * 10); i++) values[i] = 1; return new(values, .1, duration);
    }
    [SupportedOSPlatform("windows")]
    private static void Speak(string path)
    {
        dynamic voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!)!; dynamic stream = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream")!)!;
        try { stream.Open(path, 3, false); voice.AudioOutputStream = stream; voice.Rate = 0; voice.Speak("This is a local voice activity test. The words are spoken clearly.", 0); stream.Close(); }
        finally { Marshal.FinalReleaseComObject(stream); Marshal.FinalReleaseComObject(voice); }
    }
    private static void Wave(string path, byte[] pcm)
    {
        using var file = File.Create(path); using var writer = new BinaryWriter(file); writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(pcm.Length + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
    }
}

public sealed class WindowsSpeechFactAttribute : FactAttribute
{
    public WindowsSpeechFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "This native fixture uses the installed offline Windows SAPI voice.";
        else if (!File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")) || !File.Exists(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE"))) Skip = "Set HOME_TEST_FFMPEG and HOME_TEST_FFPROBE for the local speech fixture.";
    }
}
