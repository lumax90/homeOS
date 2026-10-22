using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;

namespace HomeServer.Tests;

public sealed class MusicCatalogTests
{
    private const string Lead = "11111111-1111-4111-8111-111111111111";
    private const string Guest = "22222222-2222-4222-8222-222222222222";
    private const string AlbumArtist = "33333333-3333-4333-8333-333333333333";
    private static AudioTags Tags => new("Song", "AC/DC & Guest", "Album band", "Shared album", "2020", 1, 1, null, null, null, null, null, null, null,
        ArtistCredits: [new(Lead, "AC/DC", "AC/DC", " & "), new(Guest, "Guest", "Guest")], AlbumArtistCredits: [new(AlbumArtist, "Album band", "Album band")]);
    [Fact]
    public async Task MultipleCreditsSeparateAlbumAndTrackArtistsAndKeepProviderMatchesAcrossRescan()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var actor = home.Store.GetAccount("1")!;
        home.Write("One.flac"); home.Write("Two.flac"); var library = home.AddLibrary(); await home.Scan(library.Id); var files = home.Store.GetCatalog(null, null, null, 0, 10).Items;
        home.Store.SaveMusicTags(files[0].Id, Tags); home.Store.SaveMusicTags(files[1].Id, Tags with { Title = "Solo", Track = 2, Artist = "Album band", ArtistCredits = [new(AlbumArtist, "Album band", "Album band")] });
        var artists = home.Store.GetLocalMusicArtists(actor).Items; Assert.Equal(3, artists.Count); Assert.Equal(Lead, artists.Single(row => row.Artist.Name == "AC/DC").Artist.Mbid);
        var guest = artists.Single(row => row.Artist.Mbid == Guest); Assert.Equal(1, guest.Tracks); var guestAlbums = home.Store.GetMusicAlbums(actor, 0, artistId: guest.Artist.Id); Assert.Equal(2, Assert.Single(guestAlbums.Items).Tracks);
        var album = Assert.Single(home.Store.GetMusicAlbums(actor, 0).Items); Assert.Equal(AlbumArtist, Assert.Single(album.ArtistCredits!).Mbid);
        var track = home.Store.GetMusicTracks(album.Id, actor).Items.Single(track => track.MediaId == files[0].Id); Assert.Equal(new[] { Lead, Guest }, track.ArtistCredits!.Select(credit => credit.Mbid));
        var release = new MusicRelease(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Official album", "Guest", "2020", null, "Official", [], 2, [new(Guest, "Guest", "Guest")]),
            files.Select((file, index) => new MusicReleaseTrack(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, index + 1, (index + 1).ToString(), index == 0 ? "Song" : "Solo", "Lead credited", null, [new(Lead, "AC/DC", "Lead credited")])).ToArray());
        home.Store.MatchMusicAlbum(album.Id, release, actor, files.Select((file, index) => new MusicTrackAssignment(file.Id, 1, index + 1)).ToArray());
        home.Store.SaveMusicTags(files[0].Id, Tags);
        Assert.Equal(album.Id, Assert.Single(home.Store.GetMusicAlbums(actor, 0).Items).Id);
        Assert.Equal(Guest, Assert.Single(home.Store.GetMusicAlbum(album.Id, actor)!.ArtistCredits!).Mbid);
        Assert.All(home.Store.GetMusicTracks(album.Id, actor).Items, track => Assert.Equal("Lead credited", Assert.Single(track.ArtistCredits!).CreditedName));
        home.Store.UnmatchMusicAlbum(album.Id); Assert.Equal(AlbumArtist, Assert.Single(home.Store.GetMusicAlbum(album.Id, actor)!.ArtistCredits!).Mbid);
        Assert.Equal(2, home.Store.GetMusicTracks(album.Id, actor).Items.Single(track => track.MediaId == files[0].Id).ArtistCredits!.Length);
        var restricted = home.Store.SaveAccount(null, new("Hidden", AllLibraries: false), "isolated"); Assert.Empty(home.Store.GetLocalMusicArtists(restricted).Items); Assert.Empty(home.Store.GetMusicAlbums(restricted, 0, artistId: guest.Artist.Id).Items);
    }
    [Fact]
    public void AmbiguousFlatTagsNeverInventMultiArtistIdsOrSplitBandNames()
    {
        Assert.Single(MusicCredits.FromValues(["AC/DC & Friends"], Lead + ";" + Guest)); Assert.Null(MusicCredits.FromValues(["AC/DC & Friends"], Lead + ";" + Guest)[0].Mbid);
        var arrays = MusicCredits.FromValues(["AC/DC", "Earth, Wind & Fire"], Lead + "; " + Guest); Assert.Equal(2, arrays.Length); Assert.Equal(Guest, arrays[1].Mbid);
        using var json = JsonDocument.Parse($$"""{"format":{"tags":{"artist":"Track Artist","album_artist":"Album Artist","MUSICBRAINZ_ARTISTID":"{{Lead}}","MUSICBRAINZ_ALBUMARTISTID":"{{AlbumArtist}}"} } }""");
        var tags = MusicTags.Read(json.RootElement); Assert.Equal(Lead, tags.ArtistMbid); Assert.Equal(AlbumArtist, tags.AlbumArtistMbid);
    }
    [Fact]
    public async Task LocalMusicVideoLinksRespectBothLibrariesAndChangingRecordingIdentity()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var actor = home.Store.GetAccount("1")!;
        home.Write("Song.flac"); var audioLibrary = home.AddLibrary(); await home.Scan(audioLibrary.Id); var audio = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.SaveMusicTags(audio.Id, Tags with { RecordingMbid = Lead });
        var videos = Path.Combine(home.Root, "videos"); Directory.CreateDirectory(videos); File.WriteAllBytes(Path.Combine(videos, "Clip.mp4"), [1, 2, 3]);
        var videoLibrary = home.Store.AddLibrary(new("Videos", videos, "movies")); await home.Scan(videoLibrary.Id); var video = Assert.Single(home.Store.GetCatalog(null, "video", null, 0, 10).Items);
        var before = StateFolders.Hash(Path.Combine(videos, "Clip.mp4")); var link = home.Store.LinkMusicVideo(audio.Id, new(video.Id, Lead), actor);
        Assert.Equal(video.Id, Assert.Single(home.Store.GetMusicVideos(audio.Id, actor)).Video.Id);
        var restricted = home.Store.SaveAccount(null, new("Member", AllLibraries: false, LibraryIds: [audioLibrary.Id]), "isolated"); Assert.Empty(home.Store.GetMusicVideos(audio.Id, restricted));
        Assert.Equal(403, Assert.Throws<DomainException>(() => home.Store.LinkMusicVideo(audio.Id, new(video.Id, Lead), restricted)).StatusCode);
        home.Store.SaveMusicTags(audio.Id, Tags with { RecordingMbid = Guest }); Assert.Empty(home.Store.GetMusicVideos(audio.Id, actor));
        Assert.Throws<DomainException>(() => home.Store.LinkMusicVideo(audio.Id, new(video.Id, Lead), actor));
        home.Store.UnlinkMusicVideo(link.Id, link.Revision, actor); Assert.Equal(before, StateFolders.Hash(Path.Combine(videos, "Clip.mp4")));
    }
    [Fact]
    public async Task QueuedTagPublicationCannotOutliveItsReviewedReleaseMetadata()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Owner", "isolated")); var actor = home.Store.GetAccount("1")!;
        var paths = new HomeServer.Downloads.DownloadFiles(home.Paths); Directory.CreateDirectory(paths.MusicLibraryRoot); var path = Path.Combine(paths.MusicLibraryRoot, "One.flac"); File.WriteAllBytes(path, [1, 2, 3]);
        var library = home.Store.EnsureDownloadLibrary(paths.MusicLibraryRoot, true); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.SaveMusicTags(media.Id, Tags);
        var album = Assert.Single(home.Store.GetMusicAlbums(actor, 0).Items); var release = new MusicRelease(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Original", "Artist", "2020", null, null, [], 1),
            [new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "1", "Song", "Artist", null)]);
        home.Store.MatchMusicAlbum(album.Id, release, actor, [new(media.Id, 1, 1)]); var hash = StateFolders.Hash(path);
        var plan = new MusicTagPlan(media.Id, library.Id, home.Store.GetLibraryConfiguration(library.Id).Revision, media.RelativePath, media.RelativePath,
            HomeServer.Playback.MediaAnalysisService.Fingerprint(new FileInfo(path)), Tags, false, release.Metadata.Id, hash, hash, MetadataHash: home.Store.GetMusicTagMetadataHash(media.Id));
        home.Store.QueueMusicTask(media.Id, "tags", plan); var work = home.Store.ClaimMusicTask()!;
        home.Store.MatchMusicAlbum(album.Id, release with { Metadata = release.Metadata with { Title = "Changed" } }, actor, [new(media.Id, 1, 1)]);
        var published = false; Assert.Throws<DomainException>(() => home.Store.TryPublishMusicTags(work, plan, () => published = true)); Assert.False(published); Assert.Equal(hash, StateFolders.Hash(path));
    }
    [Fact]
    public void EnhancedLrcPreservesWordsRepeatOffsetsAndExplicitVocalEndMarkers()
    {
        var lines = LyricsService.Parse("[ar:Isolated]\n[00:01.00][00:11.00]<00:01.00>Merhaba <00:02.25>dünya<00:03.00>\n[offset:500]\n[00:20.00]Son satır");
        Assert.Equal(new double?[] { 1.5, 11.5, 20.5 }, lines.Select(line => line.Seconds)); Assert.Equal("Merhaba dünya", lines[0].Text);
        Assert.Equal(2, lines[0].Words!.Length); Assert.Equal(2.75, lines[0].Words![1].Seconds); Assert.Equal(3.5, lines[0].Words![1].EndSeconds);
        Assert.Equal(12.75, lines[1].Words![1].Seconds); Assert.Equal(13.5, lines[1].Words![1].EndSeconds);
        var malformed = LyricsService.Parse("[00:01]<00:02>One <00:01>Two\n[00:03]Next"); Assert.Empty(malformed[0].Words!); Assert.Equal("One Two", malformed[0].Text);
        Assert.Empty(LyricsService.Parse("[00:99.0]Invalid seconds")); Assert.Throws<DomainException>(() => LyricsService.Parse(new string('a', 256 * 1024 + 1)));
    }
    [Fact]
    public async Task EnhancedSidecarOverridesPlainTagsAndReadsInsideTheLibraryOnly()
    {
        using var home = new TestWorkspace(); home.Write("Song.flac"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items); home.Store.SaveMusicTags(media.Id, Tags with { Lyrics = "Plain tag" });
        var sidecar = Path.Combine(home.Source, "Song.elrc"); File.WriteAllText(sidecar, "[00:01]<00:01>Timed <00:02>words<00:03>"); var before = StateFolders.Hash(sidecar);
        var service = new LyricsService(home.Store); var line = Assert.Single(await service.ReadAsync(media, CancellationToken.None)); Assert.Equal(2, line.Words!.Length); Assert.Equal(before, StateFolders.Hash(sidecar));
        File.Delete(sidecar); Assert.Equal("Plain tag", Assert.Single(await service.ReadAsync(media, CancellationToken.None)).Text);
    }
}
