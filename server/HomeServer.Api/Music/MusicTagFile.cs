using HomeServer.Core;

namespace HomeServer.Music;

public static class MusicTagFile
{
    public static readonly string[] Extensions = [".flac", ".mp3", ".m4a", ".ogg", ".oga", ".opus", ".wav", ".aif", ".aiff"];
    public static bool Supports(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static string? Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".opus" or ".oga" => "taglib/ogg", ".aif" => "taglib/aiff", _ => null };
    private sealed class ReadOnlyFile(string path) : TagLib.File.IFileAbstraction
    {
        public string Name => path;
        public Stream ReadStream => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        public Stream WriteStream => throw new InvalidOperationException("Kaynak etiketi salt okunur.");
        public void CloseStream(Stream stream) => stream.Dispose();
    }
    public static AudioTags Read(string path, AudioTags fallback, CancellationToken token)
    {
        if (!Supports(path)) return fallback;
        token.ThrowIfCancellationRequested();
        try
        {
            using var file = TagLib.File.Create(new ReadOnlyFile(path), Mime(path), TagLib.ReadStyle.None); var tag = file.Tag;
            var native = NativeTags(tag).ToArray();
            var artistNames = native.Select(value => NativeValues(value, "ARTISTS")).FirstOrDefault(values => values.Length > 0) ?? tag.Performers;
            var albumNames = native.Select(value => NativeValues(value, "ALBUMARTISTS")).FirstOrDefault(values => values.Length > 0) ?? tag.AlbumArtists;
            // Pre-v2.4 ID3 uses '/' for multiple values, indistinguishable from a band's name.
            if (native.OfType<TagLib.Id3v2.Tag>().Any(value => value.Version < 4))
            {
                if (!native.Any(value => NativeValues(value, "ARTISTS").Length > 0) && fallback.Artist.Length > 0) artistNames = [fallback.Artist];
                if (!native.Any(value => NativeValues(value, "ALBUMARTISTS").Length > 0) && fallback.AlbumArtist.Length > 0) albumNames = [fallback.AlbumArtist];
            }
            var artists = MusicCredits.FromValues(artistNames, tag.MusicBrainzArtistId);
            var albumArtists = MusicCredits.FromValues(albumNames, tag.MusicBrainzReleaseArtistId);
            var fullDate = native.Select(NativeDate).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            static string Text(string? value, string other, int max = 500) => string.IsNullOrWhiteSpace(value) ? other : value[..Math.Min(max, value.Length)];
            static double? Gain(double value, double? other) => double.IsFinite(value) && value is >= -30 and <= 30 ? value : other;
            static double? Peak(double value, double? other) => double.IsFinite(value) && value is > 0 and <= 100 ? value : other;
            token.ThrowIfCancellationRequested();
            return fallback with
            {
                Title = Text(tag.Title, fallback.Title), Album = Text(tag.Album, fallback.Album),
                Artist = artists.Length > 0 ? MusicCredits.Display(artists) : fallback.Artist,
                AlbumArtist = albumArtists.Length > 0 ? MusicCredits.Display(albumArtists) : fallback.AlbumArtist,
                ArtistCredits = artists.Length > 0 ? artists : fallback.ArtistCredits, AlbumArtistCredits = albumArtists.Length > 0 ? albumArtists : fallback.AlbumArtistCredits,
                ArtistMbid = artists.Length == 1 ? artists[0].Mbid : null, AlbumArtistMbid = albumArtists.Length == 1 ? albumArtists[0].Mbid : null,
                Disc = tag.Disc > 0 && tag.Disc <= 10000 ? (int)tag.Disc : fallback.Disc, Track = tag.Track > 0 && tag.Track <= 100000 ? (int)tag.Track : fallback.Track,
                Date = fullDate is { Length: <= 20 } ? fullDate : fallback.Date ?? (tag.Year > 0 ? tag.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
                ReleaseMbid = MusicCredits.ValidId(tag.MusicBrainzReleaseId) ?? fallback.ReleaseMbid,
                ReleaseGroupMbid = MusicCredits.ValidId(tag.MusicBrainzReleaseGroupId) ?? fallback.ReleaseGroupMbid,
                RecordingMbid = MusicCredits.ValidId(tag.MusicBrainzTrackId) ?? fallback.RecordingMbid,
                Lyrics = Text(tag.Lyrics, fallback.Lyrics ?? "", 256 * 1024) is { Length: > 0 } lyrics ? lyrics : null,
                ReplayGainDb = Gain(tag.ReplayGainTrackGain, fallback.ReplayGainDb), AlbumGainDb = Gain(tag.ReplayGainAlbumGain, fallback.AlbumGainDb),
                TrackPeak = Peak(tag.ReplayGainTrackPeak, fallback.TrackPeak), AlbumPeak = Peak(tag.ReplayGainAlbumPeak, fallback.AlbumPeak), HasArtwork = tag.Pictures.Length > 0 || fallback.HasArtwork
            };
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException or ArgumentException) { return fallback; }
    }
    public static void WriteCopy(string path, AudioTags tags, (string Path, string Mime)? cover, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = TagLib.File.Create(path, Mime(path), TagLib.ReadStyle.None); var tag = file.Tag;
        foreach (var id3 in NativeTags(tag).OfType<TagLib.Id3v2.Tag>()) id3.Version = 4;
        var artists = MusicCredits.Normalize(tags.ArtistCredits, tags.Artist, tags.ArtistMbid); var albumArtists = MusicCredits.Normalize(tags.AlbumArtistCredits, tags.AlbumArtist, tags.AlbumArtistMbid);
        tag.Title = tags.Title; tag.Album = tags.Album; tag.Performers = artists.Select(artist => artist.CreditedName).ToArray(); tag.AlbumArtists = albumArtists.Select(artist => artist.CreditedName).ToArray();
        tag.PerformersSort = artists.Select(artist => artist.SortName ?? artist.Name).ToArray(); tag.AlbumArtistsSort = albumArtists.Select(artist => artist.SortName ?? artist.Name).ToArray();
        tag.MusicBrainzArtistId = artists.Length > 0 && artists.All(artist => artist.Mbid is not null) ? string.Join('/', artists.Select(artist => artist.Mbid)) : "";
        tag.MusicBrainzReleaseArtistId = albumArtists.Length > 0 && albumArtists.All(artist => artist.Mbid is not null) ? string.Join('/', albumArtists.Select(artist => artist.Mbid)) : "";
        tag.MusicBrainzReleaseId = tags.ReleaseMbid; tag.MusicBrainzReleaseGroupId = tags.ReleaseGroupMbid; tag.MusicBrainzTrackId = tags.RecordingMbid;
        tag.Track = (uint)Math.Max(0, tags.Track); tag.Disc = (uint)Math.Max(0, tags.Disc); tag.Lyrics = tags.Lyrics;
        if (tags.Date is { Length: >= 4 } date && uint.TryParse(date[..4], out var year)) tag.Year = year;
        foreach (var native in NativeTags(tag))
        {
            WriteNativeValues(native, "ARTISTS", artists.Select(artist => artist.CreditedName).ToArray());
            WriteNativeValues(native, "ALBUMARTISTS", albumArtists.Select(artist => artist.CreditedName).ToArray());
            if (tags.Date is not { Length: > 0 } releaseDate) continue;
            if (native is TagLib.Ogg.XiphComment xiph) xiph.SetField("DATE", releaseDate);
            else if (native is TagLib.Mpeg4.AppleTag apple) apple.SetText(DateAtom, releaseDate);
            else if (native is TagLib.Id3v2.Tag id3) TagLib.Id3v2.TextInformationFrame.Get(id3, "TDRC", true).Text = [releaseDate];
        }
        if (tags.ReplayGainDb is { } gain) tag.ReplayGainTrackGain = gain; if (tags.AlbumGainDb is { } albumGain) tag.ReplayGainAlbumGain = albumGain;
        if (tags.TrackPeak is { } peak) tag.ReplayGainTrackPeak = peak; if (tags.AlbumPeak is { } albumPeak) tag.ReplayGainAlbumPeak = albumPeak;
        if (cover is { } image)
        {
            if (new FileInfo(image.Path).Length > 10 * 1024 * 1024) throw new DomainException("Albüm kapağı boyut sınırını aşıyor.", 422);
            tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(File.ReadAllBytes(image.Path))) { MimeType = image.Mime, Type = TagLib.PictureType.FrontCover, Description = "Cover" }];
        }
        token.ThrowIfCancellationRequested(); file.Save(); token.ThrowIfCancellationRequested();
    }
    public static bool SameCredits(MusicArtistCredit[]? expected, MusicArtistCredit[]? actual) => expected is null or { Length: 0 }
        || actual is not null && expected.Length == actual.Length && expected.Zip(actual).All(pair => pair.First.CreditedName == pair.Second.CreditedName && (pair.First.Mbid is null || pair.First.Mbid == pair.Second.Mbid));
    private static TagLib.ByteVector DateAtom => new([0xa9, (byte)'d', (byte)'a', (byte)'y']);
    private static IEnumerable<TagLib.Tag> NativeTags(TagLib.Tag tag) => tag switch
    {
        TagLib.CombinedTag combined => combined.Tags.Where(value => value is not null).SelectMany(NativeTags),
        TagLib.Ogg.GroupedComment grouped => grouped.Comments,
        _ => [tag]
    };
    private static string[] NativeValues(TagLib.Tag tag, string key) => tag switch
    {
        TagLib.Ogg.XiphComment xiph => xiph.GetField(key),
        TagLib.Mpeg4.AppleTag apple => apple.GetDashBoxes("com.apple.iTunes", key) ?? [],
        TagLib.Id3v2.Tag id3 => TagLib.Id3v2.UserTextInformationFrame.Get(id3, key, false)?.Text ?? [],
        _ => []
    };
    private static void WriteNativeValues(TagLib.Tag tag, string key, string[] values)
    {
        if (values.Length == 0) return;
        if (tag is TagLib.Ogg.XiphComment xiph) xiph.SetField(key, values);
        else if (tag is TagLib.Mpeg4.AppleTag apple) apple.SetDashBoxes("com.apple.iTunes", key, values);
        else if (tag is TagLib.Id3v2.Tag id3) TagLib.Id3v2.UserTextInformationFrame.Get(id3, key, true).Text = values;
    }
    private static string? NativeDate(TagLib.Tag tag) => tag switch
    {
        TagLib.Ogg.XiphComment xiph => xiph.GetFirstField("DATE"),
        TagLib.Mpeg4.AppleTag apple => apple.GetText(DateAtom).FirstOrDefault(),
        TagLib.Id3v2.Tag id3 => TagLib.Id3v2.TextInformationFrame.Get(id3, "TDRC", false)?.Text.FirstOrDefault(),
        _ => null
    };
}
