using System.Net;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Music;
using HomeServer.Playback;
using HomeServer.Scanning;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MusicAudioTests
{
    [MediaTheory]
    [InlineData(".flac", "flac")]
    [InlineData(".mp3", "libmp3lame")]
    [InlineData(".m4a", "aac")]
    [InlineData(".ogg", "libvorbis")]
    [InlineData(".oga", "libvorbis")]
    [InlineData(".opus", "libopus")]
    [InlineData(".wav", "pcm_s16le")]
    [InlineData(".aif", "pcm_s16be")]
    [InlineData(".aiff", "pcm_s16be")]
    public async Task NativeAdditionalContainersKeepMultipleArtistIdsArtworkAudioAndExactUndo(string extension, string codec)
    {
            using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var actor = home.Store.GetAccount("1")!; var tools = Tools(home); var paths = new DownloadFiles(home.Paths);
            var source = Path.Combine(home.Source, "Source" + extension); await Generate(tools, source, 440, .5, codec); var originalHash = StateFolders.Hash(source);
            var folder = DownloadFiles.EnsureDirectory(paths.MusicLibraryRoot, Guid.NewGuid().ToString("N")); var path = Path.Combine(folder, "Original" + extension); Assert.True(DownloadFiles.TryHardLink(source, path));
            var library = home.Store.EnsureDownloadLibrary(paths.MusicLibraryRoot, true); await Scan(home, tools, library.Id);
            var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); var album = Assert.Single(home.Store.GetMusicAlbums(actor, 0).Items);
            MusicArtistCredit[] credits = [new(Guid.NewGuid().ToString(), "AC/DC", "AC/DC", " feat. "), new(Guid.NewGuid().ToString(), "Guest; alias", "Guest; alias")];
            MusicArtistCredit[] albumCredits = [new(Guid.NewGuid().ToString(), "Album band", "Album band")];
            var release = new MusicRelease(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Matched Album", "Album band", "2020-05-17", "TR", "Official", [], 1, albumCredits),
                [new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "1", "Matched Song", "AC/DC feat. Guest", 500, credits)]);
            home.Store.MatchMusicAlbum(album.Id, release, actor, [new(media.Id, 1, 1)]);
            var coverRoot = AppFiles.DirectoryIn(home.Paths.Data, "cover-art"); var cover = Path.Combine(coverRoot, "release-" + release.Metadata.Id + ".image");
            await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=32x32", "-frames:v", "1", "-f", "image2", "-c:v", "mjpeg", cover], TimeSpan.FromSeconds(10), CancellationToken.None);
            using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }); var tagger = new MusicTagService(home.Store, new MediaAnalysisService(home.Store, tools), tools, paths, new CoverArtCache(home.Paths, new NoNetwork(), memory));
            var preview = await tagger.PreviewAsync(media.Id, false, CancellationToken.None); Assert.True(preview.Writable, extension);
            var job = await tagger.QueueAsync(media.Id, new(preview.Fingerprint, Rename: false, Artwork: true, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None);
            try { await tagger.ExecuteAsync(home.Store.ClaimMusicTask()!, CancellationToken.None); }
            catch (DomainException ex)
            {
                var candidate = Path.Combine(paths.ImportArea, "music-tags", job.Id, "replacement" + extension);
                string detail;
                try { using var tagged = TagLib.File.Create(candidate, extension == ".opus" ? "taglib/ogg" : null, TagLib.ReadStyle.None); detail = string.Join(" | ", tagged.Tag.Performers); }
                catch (Exception tagError) { detail = tagError.ToString(); }
                throw new Xunit.Sdk.XunitException(extension + " · " + ex.Message + " · " + detail + " · " + JsonSerializer.Serialize((await tools.ProbeAsync(candidate, CancellationToken.None))?.Music)
                    + (extension == ".opus" ? " SOURCE=" + OggPages(source) + " TARGET=" + OggPages(candidate) : ""));
            }
            var written = (await tools.ProbeAsync(path, CancellationToken.None))!.Music!; Assert.Equal("Matched Song", written.Title); Assert.Equal(release.Tracks[0].RecordingId, written.RecordingMbid); Assert.Equal("2020-05-17", written.Date);
            Assert.True(written.HasArtwork, extension); Assert.Equal(credits.Select(credit => credit.Mbid), written.ArtistCredits!.Select(credit => credit.Mbid)); Assert.Equal(albumCredits[0].Mbid, Assert.Single(written.AlbumArtistCredits!).Mbid);
            Assert.Equal(originalHash, StateFolders.Hash(source));
            await tagger.RestoreAsync(job.Id, CancellationToken.None); await tagger.ExecuteAsync(home.Store.ClaimMusicTask()!, CancellationToken.None); Assert.Equal(originalHash, StateFolders.Hash(path));
    }
    [MediaFact]
    public async Task NativePcmUsesExactSampleRangesAndRejectsChangedSources()
    {
        using var home = new TestWorkspace(); var tools = Tools(home); var path = Path.Combine(home.Source, "Fixture.flac");
        await Generate(tools, path, 440, .25);
        var library = home.Store.AddLibrary(new AddLibrary("Music", home.Source, "music")); await Scan(home, tools, library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); var cache = new MusicPcmCache(home.Paths, new MediaAnalysisService(home.Store, tools), tools, home.Store);
        var hash = StateFolders.Hash(path); var info = await cache.PrepareAsync(media.Id, CancellationToken.None); Assert.Equal(12000, info.Frames); Assert.Equal(48000, info.SampleRate);
        Assert.Equal(-4.5, info.TrackGainDb); Assert.Equal(-5.5, info.AlbumGainDb); Assert.Equal(.9, info.TrackPeak); Assert.Equal(.95, info.AlbumPeak);
        var first = await cache.ReadAsync(media.Id, info.Fingerprint, 0, 7000, CancellationToken.None); var second = await cache.ReadAsync(media.Id, info.Fingerprint, 7000, 5000, CancellationToken.None);
        await using (var full = cache.Open(media.Id, info.Fingerprint)) { var expected = new byte[full.Length]; await full.ReadExactlyAsync(expected); Assert.Equal(expected, first.Concat(second).ToArray()); }
        var again = await cache.PrepareAsync(media.Id, CancellationToken.None); Assert.Equal(info.Frames, again.Frames); Assert.Equal(hash, StateFolders.Hash(path));
        var range = await Assert.ThrowsAsync<DomainException>(() => cache.ReadAsync(media.Id, info.Fingerprint, 12000, 1, CancellationToken.None)); Assert.Equal(416, range.StatusCode);
        await Assert.ThrowsAsync<DomainException>(() => cache.ReadAsync(media.Id, info.Fingerprint, -1, 1, CancellationToken.None));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10)); Assert.Throws<DomainException>(() => cache.Open(media.Id, info.Fingerprint));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.PrepareAsync(media.Id, cancelled.Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(home.Paths.Data, "music-cache", "pcm"), "*.partial"));
    }
    [MediaFact]
    public async Task NativeLoudnessAndSpectralSimilarityAreRealAndRespectUserAccess()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = Tools(home);
        await Generate(tools, Path.Combine(home.Source, "Low.flac"), 440, 2);
        await Generate(tools, Path.Combine(home.Source, "Similar.flac"), 440, 2);
        await Generate(tools, Path.Combine(home.Source, "High.flac"), 8000, 2);
        var library = home.Store.AddLibrary(new AddLibrary("Music", home.Source, "music")); await Scan(home, tools, library.Id);
        var media = home.Store.GetCatalog(null, null, null, 0, 60).Items; var analysis = new MediaAnalysisService(home.Store, tools); var pcm = new MusicPcmCache(home.Paths, analysis, tools, home.Store); var sonic = new MusicSonicAnalyzer(pcm, analysis, tools);
        foreach (var item in media)
        {
            home.Store.QueueMusicTask(item.Id, "sonic"); var task = home.Store.ClaimMusicTask()!;
            var result = await sonic.AnalyzeAsync(item.Id, CancellationToken.None); home.Store.SaveMusicSonic(task, result); home.Store.FinishMusicTask(task, "completed", "Fixture analysis");
            Assert.NotNull(result.LoudnessLufs); Assert.InRange(result.LoudnessLufs.Value, -40, -10); Assert.InRange(result.Bands.Sum(), .999, 1.001);
        }
        var low = media.Single(item => item.RelativePath == "Low.flac"); var high = media.Single(item => item.RelativePath == "High.flac"); var similar = media.Single(item => item.RelativePath == "Similar.flac");
        var lowAnalysis = home.Store.GetMusicSonic(low.Id)!; var highAnalysis = home.Store.GetMusicSonic(high.Id)!;
        Assert.InRange(lowAnalysis.CentroidHz, 400, 500); Assert.InRange(highAnalysis.CentroidHz, 7800, 8200);
        Assert.True(MusicSonicAnalyzer.Similarity(lowAnalysis, home.Store.GetMusicSonic(similar.Id)!) > MusicSonicAnalyzer.Similarity(lowAnalysis, highAnalysis) + .5);
        var owner = home.Store.GetAccount("1")!; home.Store.SetMusicFavorite(low.Id, owner, true);
        Assert.Equal(similar.Id, home.Store.MusicSuggestions(owner)[0].Media.Id);
        var hidden = home.Store.SaveAccount(null, new AccountInput("Restricted", AllLibraries: false), "fixture");
        Assert.Empty(home.Store.MusicSuggestions(hidden)); Assert.Throws<DomainException>(() => home.Store.SetMusicFavorite(low.Id, hidden, true));
        home.Store.SaveMusicPlaybackPreferences(owner.Id, new("album", -2)); Assert.Equal("album", home.Store.GetMusicPlaybackPreferences(owner.Id).ReplayGain); Assert.Equal("track", home.Store.GetMusicPlaybackPreferences(hidden.Id).ReplayGain);
    }
    [MediaFact]
    public async Task ManagedTagRenameAndCrashRecoveryRestoreExactBytesWithoutChangingHardlinkedSource()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = Tools(home); var paths = new DownloadFiles(home.Paths);
        var source = Path.Combine(home.Source, "Original.flac"); await Generate(tools, source, 440, .5); var sourceHash = StateFolders.Hash(source);
        var folder = DownloadFiles.EnsureDirectory(paths.MusicLibraryRoot, Guid.NewGuid().ToString("N")); var original = Path.Combine(folder, "Original.flac");
        Assert.True(DownloadFiles.TryHardLink(source, original));
        var library = home.Store.EnsureDownloadLibrary(paths.MusicLibraryRoot, true); await Scan(home, tools, library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); var album = Assert.Single(home.Store.GetMusicAlbums(home.Store.GetAccount("1")!, 0).Items);
        var release = new MusicRelease(new("7a65d5cb-bcbf-4b0d-b6e0-9a7a49a156cd", "caafa493-8b5c-40a0-803e-95a690d5a30e", "Matched Album", "Fixture Artist", "2020", "TR", "Official", ["CD"], 1),
            [new("1bda5c1a-2c21-43f3-a3f1-c84b7b611146", "ec670060-53aa-4283-94d5-af423fc5a8a9", 1, 1, "1", "Matched Song", "Fixture Artist", 500)]);
        home.Store.MatchMusicAlbum(album.Id, release, home.Store.GetAccount("1")!, [new(media.Id, 1, 1)]);
        home.Store.SaveProgress(media.Id, new PlaybackProgress(.2, .5));
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }); var tagger = new MusicTagService(home.Store, new MediaAnalysisService(home.Store, tools), tools, paths, new CoverArtCache(home.Paths, new NoNetwork(), memory));
        var preview = await tagger.PreviewAsync(media.Id, true, CancellationToken.None); Assert.True(preview.Writable);
        home.Store.MatchMusicAlbum(album.Id, release with { Metadata = release.Metadata with { Title = "Another metadata revision" } }, home.Store.GetAccount("1")!, [new(media.Id, 1, 1)]);
        await Assert.ThrowsAsync<DomainException>(() => tagger.QueueAsync(media.Id, new(preview.Fingerprint, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None));
        home.Store.MatchMusicAlbum(album.Id, release, home.Store.GetAccount("1")!, [new(media.Id, 1, 1)]);
        var conflict = Path.Combine(library.Path, preview.ProposedPath); File.WriteAllBytes(conflict, [7, 8, 9]);
        await Assert.ThrowsAsync<DomainException>(() => tagger.QueueAsync(media.Id, new(preview.Fingerprint, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None)); Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(conflict)); File.Delete(conflict);
        var queued = await tagger.QueueAsync(media.Id, new(preview.Fingerprint, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None); var claimed = home.Store.ClaimMusicTask()!;
        await tagger.ExecuteAsync(claimed, CancellationToken.None);
        var edited = home.Store.GetMedia(media.Id)!; Assert.Equal(preview.ProposedPath, edited.RelativePath); Assert.False(File.Exists(original)); Assert.Equal(sourceHash, StateFolders.Hash(source));
        var tags = (await tools.ProbeAsync(Path.Combine(library.Path, edited.RelativePath), CancellationToken.None))!.Music!;
        Assert.Equal("Matched Song", tags.Title); Assert.Equal("Matched Album", tags.Album); Assert.Equal(release.Metadata.Id, tags.ReleaseMbid); Assert.Equal(.2, edited.PositionSeconds, 4);
        // Simulate process exit after publishing the restore target but before deleting the managed old name or committing the DB.
        var restore = await tagger.RestoreAsync(queued.Id, CancellationToken.None); var restoring = home.Store.ClaimMusicTask()!;
        var plan = JsonSerializer.Deserialize<MusicTagPlan>(restoring.PlanJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var area = AppFiles.DirectoryIn(paths.ImportArea, "music-tags", restore.Id); var currentPath = Path.Combine(library.Path, plan.OriginalPath);
        File.Copy(currentPath, Path.Combine(area, "original.flac"));
        plan = plan with { OriginalHash = StateFolders.Hash(currentPath), TargetHash = sourceHash };
        var temporary = Path.Combine(area, "replacement.flac"); File.Copy(source, temporary);
        Assert.Throws<IOException>(() => home.Store.TryPublishMusicTags(restoring, plan, () => { File.Move(temporary, original); throw new IOException("Simulated process exit"); }));
        Assert.Equal("publishing", home.Store.GetMusicTask(restore.Id)!.Status); Assert.Null(home.Store.TryClaimJob());
        home.Store.RecoverMusicTasks(); var recovered = home.Store.ClaimMusicTask()!; await tagger.ExecuteAsync(recovered, CancellationToken.None);
        Assert.Equal("completed", home.Store.GetMusicTask(restore.Id)!.Status); Assert.Equal(sourceHash, StateFolders.Hash(original)); Assert.Equal(sourceHash, StateFolders.Hash(source));
        Assert.Equal(media.RelativePath, home.Store.GetMedia(media.Id)!.RelativePath); Assert.False(File.Exists(currentPath));
        tagger.PruneHistory(queued.Id); Assert.False(home.Store.GetMusicTask(queued.Id)!.HistoryAvailable);
        Assert.Equal(sourceHash, StateFolders.Hash(source)); Assert.Equal(sourceHash, StateFolders.Hash(original));
    }
    [MediaFact]
    public async Task NativeMp3AndM4aTagCopyAndArtworkKeepAudioPayloadAndUndoExactBytes()
    {
        foreach (var extension in new[] { ".mp3", ".m4a" })
        {
            using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = Tools(home); var paths = new DownloadFiles(home.Paths);
            var source = Path.Combine(home.Source, "Source" + extension); await Generate(tools, source, 440, .5, extension == ".mp3" ? "libmp3lame" : "aac");
            var hash = StateFolders.Hash(source); var folder = DownloadFiles.EnsureDirectory(paths.MusicLibraryRoot, Guid.NewGuid().ToString("N")); var path = Path.Combine(folder, "Original" + extension);
            Assert.True(DownloadFiles.TryHardLink(source, path)); var library = home.Store.EnsureDownloadLibrary(paths.MusicLibraryRoot, true); await Scan(home, tools, library.Id);
            var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); var album = Assert.Single(home.Store.GetMusicAlbums(home.Store.GetAccount("1")!, 0).Items);
            var release = new MusicRelease(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Tagged Album", "Fixture Artist", "2020", "TR", "Official", ["Digital Media"], 1),
                [new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "1", "Tagged Song", "Fixture Artist", 500)]);
            home.Store.MatchMusicAlbum(album.Id, release, home.Store.GetAccount("1")!, [new(media.Id, 1, 1)]);
            var coverRoot = AppFiles.DirectoryIn(home.Paths.Data, "cover-art"); var cover = Path.Combine(coverRoot, "release-" + release.Metadata.Id + ".image");
            await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=32x32", "-frames:v", "1", "-f", "image2", "-c:v", "mjpeg", cover], TimeSpan.FromSeconds(10), CancellationToken.None);
            using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }); var tagger = new MusicTagService(home.Store, new MediaAnalysisService(home.Store, tools), tools, paths, new CoverArtCache(home.Paths, new NoNetwork(), memory));
            var preview = await tagger.PreviewAsync(media.Id, false, CancellationToken.None); Assert.True(preview.Writable);
            var job = await tagger.QueueAsync(media.Id, new(preview.Fingerprint, Rename: false, Artwork: true, ExpectedMetadataHash: preview.MetadataHash), CancellationToken.None);
            await tagger.ExecuteAsync(home.Store.ClaimMusicTask()!, CancellationToken.None);
            var changed = (await tools.ProbeAsync(path, CancellationToken.None))!.Music!; Assert.Equal("Tagged Song", changed.Title); Assert.True(changed.HasArtwork, extension); Assert.Equal(hash, StateFolders.Hash(source));
            await tagger.RestoreAsync(job.Id, CancellationToken.None); await tagger.ExecuteAsync(home.Store.ClaimMusicTask()!, CancellationToken.None);
            Assert.Equal(hash, StateFolders.Hash(path)); Assert.Equal(hash, StateFolders.Hash(source));
        }
    }
    [MediaFact]
    public async Task TagWritesRejectReadOnlyRootsAndCancelledJobsCannotPublish()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = Tools(home); var source = Path.Combine(home.Source, "Fixture.flac"); await Generate(tools, source, 440, .25);
        var library = home.Store.AddLibrary(new AddLibrary("Read only", home.Source, "music")); await Scan(home, tools, library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var tagger = new MusicTagService(home.Store, new MediaAnalysisService(home.Store, tools), tools, new DownloadFiles(home.Paths), new CoverArtCache(home.Paths, new NoNetwork(), memory));
        var preview = await tagger.PreviewAsync(media.Id, true, CancellationToken.None); Assert.False(preview.Writable); var hash = StateFolders.Hash(source);
        await Assert.ThrowsAsync<DomainException>(() => tagger.QueueAsync(media.Id, new(preview.Fingerprint), CancellationToken.None)); Assert.Equal(hash, StateFolders.Hash(source));
        var queued = home.Store.QueueMusicTask(media.Id, "sonic"); var claimed = home.Store.ClaimMusicTask()!; home.Store.CancelMusicTask(queued.Id);
        Assert.Throws<OperationCanceledException>(() => home.Store.SaveMusicSonic(claimed, new(media.Id, preview.Fingerprint, 1, -18, -2, 440, .1, new double[16], DateTimeOffset.UtcNow.ToString("O"))));
        Assert.Null(home.Store.GetMusicSonic(media.Id));
    }
    private static MediaTools Tools(TestWorkspace home) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE") }).Build(), home.Paths, NullLogger<MediaTools>.Instance);
    private static string OggPages(string path)
    {
        var data = File.ReadAllBytes(path); var rows = new List<object>(); var offset = 0;
        while (offset + 27 <= data.Length)
        {
            var count = data[offset + 26]; var laces = data.Skip(offset + 27).Take(count).ToArray(); var body = offset + 27 + count;
            rows.Add(new { offset, flags = data[offset + 5], sequence = BitConverter.ToInt32(data, offset + 18), granule = BitConverter.ToInt64(data, offset + 6), laces, head = Convert.ToHexString(data.Skip(body).Take(12).ToArray()) });
            offset = body + laces.Sum(value => value);
        }
        return JsonSerializer.Serialize(rows);
    }
    private static Task Generate(MediaTools tools, string path, int frequency, double seconds, string codec = "flac") => MediaProcess.RunAsync(tools.FfmpegExecutable,
        ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", $"sine=frequency={frequency}:sample_rate=48000", "-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:a", codec,
            "-metadata", "title=Original", "-metadata", "artist=Fixture Artist", "-metadata", "album=Fixture Album", "-metadata", "track=1", "-metadata", "disc=1",
            "-metadata", "replaygain_track_gain=-4.5 dB", "-metadata", "replaygain_album_gain=-5.5 dB", "-metadata", "replaygain_track_peak=0.9", "-metadata", "replaygain_album_peak=0.95", path], TimeSpan.FromSeconds(20), CancellationToken.None);
    private static async Task Scan(TestWorkspace home, MediaTools tools, string library)
    { home.Store.QueueScan(library); await new LibraryScanner(home.Store, tools, new EventHub()).ScanAsync(home.Store.TryClaimJob()!, CancellationToken.None); }
    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler());
        private sealed class Handler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
}
