using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using HomeServer.Subtitles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class SubtitleProviderTests
{
    [Fact]
    public async Task MovieHashUsesBothLittleEndianBlocksAndFileSize()
    {
        var bytes = new byte[131072]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, 1); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 8), 2);
        Assert.Equal("0000000000020003", await MovieHash.ComputeAsync(new MemoryStream(bytes), CancellationToken.None));
        Assert.Null(await MovieHash.ComputeAsync(new MemoryStream(new byte[100]), CancellationToken.None));
    }

    [Fact]
    public async Task VipHostParentEpisodeIdsAndSecretsAreHandledExplicitly()
    {
        using var home = new TestWorkspace(); var (settings, handler, provider) = Provider(home); handler.Episode = true;
        await provider.LoginAsync(new SubtitleLogin("fixture-user", "fixture-password"), CancellationToken.None);
        Assert.True(settings.Status.Authenticated); Assert.DoesNotContain("fixture-key", home.Store.GetSetting("subtitles.connection")!);
        var file = home.Write("Example.Show.S01E02.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var content = new ProviderContent("tmdb", 42, "show", "Example Show", "Example Show", "", "2020-01-01", null, null, "https://www.themoviedb.org/tv/42", [], "tt1234567");
        media = home.Store.MatchMedia(media.Id, content, new ProviderEpisode(2, 1, 2, "Episode", "", "2020-01-02", null), "");
        var input = new AnalyzedMedia(media, file, MediaAnalysisService.Fingerprint(new FileInfo(file)), new ProbeResult(null, null, null, null, null));
        var result = await provider.SearchAsync(input, content, new SubtitleSearchInput(), CancellationToken.None);
        Assert.True(Assert.Single(result.Items).Eligible);
        Assert.Contains(handler.Requests, request => request.Contains("vip-api.opensubtitles.com") && request.Contains("parent_imdb_id=1234567") && request.Contains("episode_number=2"));
        Assert.DoesNotContain("fixture-key", JsonSerializer.Serialize(settings.Status));
    }

    [MediaFact]
    public async Task DownloadDoesNotForwardCredentialsAndRepeatedSelectionDoesNotSpendQuotaAgain()
    {
        using var home = new TestWorkspace(); var (settings, handler, provider) = Provider(home);
        var media = await PrepareMovie(home);
        await provider.LoginAsync(new SubtitleLogin("fixture-user", "fixture-password"), CancellationToken.None);
        var tools = Tools(home); using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var acquisition = new SubtitleAcquisition(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), provider, subtitles, settings, new EventHub());
        var candidate = Assert.Single((await acquisition.SearchAsync(media.Id, new SubtitleSearchInput(), CancellationToken.None)).Items);
        var first = await acquisition.AcquireAsync(media.Id, candidate.Id, CancellationToken.None);
        var again = await acquisition.AcquireAsync(media.Id, candidate.Id, CancellationToken.None);
        Assert.Equal(first.Id, again.Id); Assert.Single(home.Store.GetManagedSubtitles(media.Id));
        Assert.Equal(1, handler.DownloadCalls); Assert.Equal(1, handler.FileCalls); Assert.Equal(4, settings.Status.Remaining);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(home.Source, "Example.Movie.2020.mp4")));
    }

    [MediaFact]
    public async Task ChangedAutomationProfileCannotPublishTheInFlightSubtitle()
    {
        using var home = new TestWorkspace(); var (settings, handler, provider) = Provider(home);
        var media = await PrepareMovie(home); await provider.LoginAsync(new SubtitleLogin("fixture-user", "fixture-password"), CancellationToken.None);
        settings.SaveProfile(new SubtitleLanguageProfile(["tr"], Enabled: true, AutoDownload: true)); var revision = settings.Revision;
        handler.BeforeFile = () => settings.SaveProfile(new SubtitleLanguageProfile(["tr"]));
        var tools = Tools(home); using var subtitles = new SubtitleService(home.Paths, home.Store, tools);
        var acquisition = new SubtitleAcquisition(home.Paths, home.Store, new MediaAnalysisService(home.Store, tools), provider, subtitles, settings, new EventHub());
        var candidate = Assert.Single((await acquisition.SearchAsync(media.Id, new SubtitleSearchInput(), CancellationToken.None)).Items);
        await Assert.ThrowsAsync<DomainException>(() => acquisition.AcquireAsync(media.Id, candidate.Id, CancellationToken.None, revision));
        Assert.Empty(home.Store.GetManagedSubtitles(media.Id));
    }

    [Fact]
    public async Task ForeignDownloadHostsAreRejectedBeforeFileAccess()
    {
        using var home = new TestWorkspace(); var (_, handler, provider) = Provider(home); handler.BadHost = true;
        await provider.LoginAsync(new SubtitleLogin("fixture-user", "fixture-password"), CancellationToken.None);
        await Assert.ThrowsAsync<DomainException>(() => provider.DownloadAsync(10, CancellationToken.None));
        Assert.Equal(0, handler.FileCalls);
    }
    private static (SubtitleSettings, Handler, OpenSubtitlesProvider) Provider(TestWorkspace home)
    {
        var settings = new SubtitleSettings(home.Store, new EphemeralDataProtectionProvider()); settings.Save(new SubtitleConnectionInput("fixture-key"));
        var handler = new Handler(); return (settings, handler, new OpenSubtitlesProvider(new Factory(handler), settings, home.Store));
    }
    private static async Task<MediaItem> PrepareMovie(TestWorkspace home)
    {
        home.Write("Example.Movie.2020.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        return home.Store.MatchMedia(media.Id, new ProviderContent("tmdb", 42, "movie", "Example Movie", "Example Movie", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", [], "tt1234567"), null, "");
    }
    private static MediaTools Tools(TestWorkspace home) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:FfmpegPath"] = Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"), ["Home:FfprobePath"] = "absent" }).Build(), home.Paths, NullLogger<MediaTools>.Instance);
    private sealed class Factory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Episode { get; set; }
        public bool BadHost { get; set; }
        public int DownloadCalls { get; private set; }
        public int FileCalls { get; private set; }
        public Action? BeforeFile { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (request.RequestUri.Host == "www.opensubtitles.com")
            {
                Assert.False(request.Headers.Contains("Api-Key")); Assert.Null(request.Headers.Authorization); FileCalls++; BeforeFile?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("1\n00:00:01,000 --> 00:00:03,000\nTürkçe fixture altyazısı\n", Encoding.UTF8) };
            }
            Assert.Equal("fixture-key", Assert.Single(request.Headers.GetValues("Api-Key")));
            string json;
            if (request.RequestUri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken); Assert.Contains("username", body); Assert.Contains("fixture-password", body);
                var token = "header." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }))) + ".fixture";
                json = JsonSerializer.Serialize(new { token, base_url = "vip-api.opensubtitles.com", user = new { allowed_downloads = 5 }, status = 200 });
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("vip-api.opensubtitles.com", request.RequestUri.Host); DownloadCalls++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken); Assert.Contains("file_id", body);
                json = JsonSerializer.Serialize(new { link = BadHost ? "https://unapproved.invalid/data" : "https://www.opensubtitles.com/download/fixture.srt", file_name = "fixture.srt", remaining = 4, reset_time_utc = DateTimeOffset.UtcNow.AddHours(1).ToString("O") });
            }
            else
            {
                json = JsonSerializer.Serialize(new { total_pages = 1, data = new[] { new { id = "11", attributes = new { language = "tr", release = "Example.Movie.2020", from_trusted = true, foreign_parts_only = false, hearing_impaired = false,
                    feature_details = new { tmdb_id = 42, imdb_id = 1234567, parent_tmdb_id = 42, parent_imdb_id = 1234567, season_number = Episode ? 1 : 0, episode_number = Episode ? 2 : 0 }, files = new[] { new { file_id = 10, file_name = "fixture.srt" } } } } } });
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
