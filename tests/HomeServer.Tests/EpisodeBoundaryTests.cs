using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class EpisodeBoundaryTests
{
    [Fact]
    public async Task WholeFileWatchedThresholdDoesNotMarkAnUnfinishedLastEpisodeWatched()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var actor = home.Store.GetAccount("1")!;
        home.Write("Pack.mp4"); home.Write("Second.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var files = home.Store.GetCatalog(null, null, null, 0, 10).Items;
        var pack = files.Single(file => file.RelativePath == "Pack.mp4"); var second = files.Single(file => file.RelativePath == "Second.mp4");
        home.Store.SaveProbe(pack.Id, new(100, 320, 180, "h264", "aac"), false); home.Store.SaveProbe(second.Id, new(50, 320, 180, "h264", "aac"), false);
        var show = new ProviderContent("tmdb", 9000, "show", "Isolated", "Isolated", "", null, null, null, "https://www.themoviedb.org/tv/9000", []);
        var one = new ProviderEpisode(901, 1, 1, "One", "", null, null); var two = one with { ProviderId = 902, EpisodeNumber = 2 };
        pack = home.Store.MatchMediaEpisodes(pack.Id, show, [(one, null, null), (two, null, null)], "", pack.MatchRevision); home.Store.MatchMedia(second.Id, show, two, ""); home.Store.SaveProgress(pack.Id, new(95, 100));
        Assert.True(home.Store.GetMedia(pack.Id)!.IsWatched);
        var path = Path.Combine(home.Source, pack.RelativePath); var inspected = new AnalyzedMedia(pack, path, MediaAnalysisService.Fingerprint(new FileInfo(path)), new(100, 320, 180, "h264", "aac"));
        home.Store.QueueEpisodeBoundary(inspected, pack.MatchRevision, actor); var work = home.Store.ClaimEpisodeBoundary()!;
        home.Store.FinishEpisodeBoundary(work, new([new(pack.Episodes![0].EpisodeId, 0, 50), new(pack.Episodes[1].EpisodeId, 50, 100)], "chapters", [], [], true));
        home.Store.ApplyEpisodeBoundary(work.Job.Id, work.Job.Revision, actor);
        var tail = home.Store.GetMedia(second.Id)!; Assert.Equal(45, tail.PositionSeconds); Assert.False(tail.IsWatched);
    }
    [MediaFact]
    public async Task NativeBlackAndSilenceProposalRebasesEpisodeProgressAndKeepsSourceBytes()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var owner = home.Store.GetAccount("1")!; var tools = SubtitleBurnTests.Tools(home);
        var source = Path.Combine(home.Source, "Combined.mkv");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=red:s=160x90:r=8:d=6", "-f", "lavfi", "-i", "color=c=black:s=160x90:r=8:d=1", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=8:d=6",
            "-f", "lavfi", "-i", "aevalsrc=if(between(t\\,6\\,7)\\,0\\,sin(2*PI*440*t)):s=16000:d=13", "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]", "-map", "[v]", "-map", "3:a", "-c:v", "libx264", "-threads", "1", "-preset", "ultrafast", "-c:a", "aac", source], TimeSpan.FromSeconds(20), CancellationToken.None);
        var media = await SubtitleBurnTests.Catalog(home, tools); var show = new ProviderContent("tmdb", 5000, "show", "Isolated series", "Isolated series", "", "2020-01-01", null, null, "https://www.themoviedb.org/tv/5000", []);
        var one = new ProviderEpisode(501, 1, 1, "One", "", "2020-01-01", null); var two = one with { ProviderId = 502, EpisodeNumber = 2, Title = "Two" };
        media = home.Store.MatchMediaEpisodes(media.Id, show, [(one, null, null), (two, null, null)], "", media.MatchRevision); var hash = StateFolders.Hash(source);
        home.Store.SaveProgress(media.Id, new(8.5, media.DurationSeconds));
        var analysis = new MediaAnalysisService(home.Store, tools); using var service = new EpisodeBoundaryService(home.Paths, home.Store, analysis, tools, new EventHub(), NullLogger<EpisodeBoundaryService>.Instance);
        var queued = await service.QueueAsync(media.Id, media.MatchRevision, owner, CancellationToken.None); Assert.True(await service.RunOnceAsync(CancellationToken.None));
        var finished = home.Store.GetEpisodeBoundaryJob(queued.Id)!; Assert.Equal("completed", finished.Status); Assert.True(finished.Result!.CanApply, string.Join(" ", finished.Result.Warnings)); Assert.Equal("signals", finished.Result.Method);
        Assert.InRange(finished.Result.Segments[1].StartSeconds, 6.8, 7.3);
        var applied = home.Store.ApplyEpisodeBoundary(finished.Id, finished.Revision, owner); Assert.True(applied.MatchRevision > media.MatchRevision); Assert.Equal(hash, StateFolders.Hash(source)); Assert.Equal(8.5, applied.PositionSeconds);
        var second = home.Store.GetVideoSequence(media.Id, owner, applied.Episodes![1].EpisodeId); Assert.InRange(second.Items[0].PositionSeconds, 8.45, 8.55);
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "episode-boundaries")));
        Assert.Throws<DomainException>(() => home.Store.ApplyEpisodeBoundary(finished.Id, finished.Revision, owner));
    }
    [Fact]
    public async Task CancelAndRecoveryPreventLateBoundaryResultsFromBeingApplied()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var owner = home.Store.GetAccount("1")!;
        home.Write("Combined.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.SaveProbe(media.Id, new(100, 320, 180, "h264", "aac"), false);
        var show = new ProviderContent("tmdb", 5000, "show", "Isolated", "Isolated", "", null, null, null, "https://www.themoviedb.org/tv/5000", []);
        media = home.Store.MatchMediaEpisodes(media.Id, show, [(new(501, 1, 1, "One", "", null, null), null, null), (new(502, 1, 2, "Two", "", null, null), null, null)], "", media.MatchRevision);
        var inspected = new AnalyzedMedia(media, Path.Combine(home.Source, media.RelativePath), MediaAnalysisService.Fingerprint(new FileInfo(Path.Combine(home.Source, media.RelativePath))), new(100, 320, 180, "h264", "aac"));
        var job = home.Store.QueueEpisodeBoundary(inspected, media.MatchRevision, owner); var first = home.Store.ClaimEpisodeBoundary()!; home.Store.RecoverEpisodeBoundaries(); var second = home.Store.ClaimEpisodeBoundary()!;
        var result = new EpisodeBoundaryResult([new(media.Episodes![0].EpisodeId, 0, 50), new(media.Episodes[1].EpisodeId, 50, 100)], "chapters", [], [], true);
        home.Store.FinishEpisodeBoundary(first, result); Assert.Equal("running", home.Store.GetEpisodeBoundaryJob(job.Id)!.Status);
        home.Store.CancelEpisodeBoundary(job.Id, second.Job.Revision, owner); home.Store.FinishEpisodeBoundary(second, result);
        Assert.Equal("cancelled", home.Store.GetEpisodeBoundaryJob(job.Id)!.Status); Assert.Null(home.Store.GetEpisodeBoundaryJob(job.Id)!.Result);
    }
}
