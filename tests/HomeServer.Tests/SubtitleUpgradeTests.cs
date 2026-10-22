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

namespace HomeServer.Tests;

public sealed class SubtitleUpgradeTests
{
    [MediaFact]
    public async Task UpgradeKeepsThePreviousBytesAndRestorePausesAutomaticReplacement()
    {
        using var home = new TestWorkspace(); var (media, tools) = await Media(home); using var setup = new Setup(home, tools);
        await setup.Login(); var first = await setup.Add(media.Id);
        var oldPath = Path.Combine(home.Paths.State, "subtitles", media.Id, first.Id + ".srt"); var oldHash = StateFolders.Hash(oldPath);
        setup.Settings.SaveProfile(new(["en"], Enabled: true, AutoDownload: true, AllowUpgrades: true));
        setup.Handler.FileId = 20; setup.Handler.HashMatched = true;
        Assert.True(await setup.Coordinator.RunOnceAsync(CancellationToken.None));
        var current = Assert.Single(home.Store.GetManagedSubtitles(media.Id)); Assert.NotEqual(first.Id, current.Id); Assert.Equal(20, current.ProviderFileId); Assert.Equal(first.Id, current.SupersedesId);
        Assert.Equal(2, home.Store.GetManagedSubtitles(media.Id, true).Count); Assert.Equal(oldHash, StateFolders.Hash(oldPath)); Assert.Equal(2, setup.Handler.Grants);
        home.Store.WakeSubtitle(media.Id); await setup.Coordinator.RunOnceAsync(CancellationToken.None); Assert.Equal(2, setup.Handler.Grants);
        home.Store.RestoreSubtitleVersion(media.Id, first.Id, current.Id); Assert.True(home.Store.GetEffectiveSubtitlePolicy(media.Id).UpgradesPaused);
        home.Store.WakeSubtitle(media.Id); await setup.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Equal(first.Id, Assert.Single(home.Store.GetManagedSubtitles(media.Id)).Id); Assert.Equal(2, setup.Handler.Grants); Assert.Equal(oldHash, StateFolders.Hash(oldPath));
    }
    [MediaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightOverrideOrConnectionRevocationCannotRetireTheGoodSubtitle(bool connection)
    {
        using var home = new TestWorkspace(); var (media, tools) = await Media(home); using var setup = new Setup(home, tools);
        await setup.Login(); var first = await setup.Add(media.Id);
        setup.Settings.SaveProfile(new(["en"], Enabled: true, AutoDownload: true, AllowUpgrades: true)); setup.Handler.FileId = 20; setup.Handler.HashMatched = true;
        setup.Handler.BeforeFile = () => { if (connection) setup.Settings.Save(new("new-fixture-key")); else home.Store.SaveSubtitleOverride(media.Id, new(new(["en"], Enabled: false))); };
        await setup.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Equal(first.Id, Assert.Single(home.Store.GetManagedSubtitles(media.Id)).Id); Assert.Single(home.Store.GetManagedSubtitles(media.Id, true));
        if (connection) Assert.Equal("new-fixture-key", setup.Settings.Secrets.ApiKey);
    }
    [MediaFact]
    public async Task ProtectedGrantIsReusedAfterAFileTransferFailureWithoutSpendingAgain()
    {
        using var home = new TestWorkspace(); var (media, tools) = await Media(home); using var setup = new Setup(home, tools); await setup.Login();
        var candidate = Assert.Single((await setup.Acquisition.SearchAsync(media.Id, new("en"), CancellationToken.None)).Items);
        setup.Handler.FailFiles = 1;
        await Assert.ThrowsAsync<DomainException>(() => setup.Acquisition.AcquireAsync(media.Id, candidate.Id, CancellationToken.None));
        var stored = home.Store.GetSubtitleCandidate(candidate.Id); Assert.NotNull(stored.ProtectedTicket); Assert.DoesNotContain("sensitive-ticket", stored.ProtectedTicket);
        var result = await setup.Acquisition.AcquireAsync(media.Id, candidate.Id, CancellationToken.None);
        Assert.Equal(1, setup.Handler.Grants); Assert.Equal(2, setup.Handler.FileCalls); Assert.Equal(result.Id, Assert.Single(home.Store.GetManagedSubtitles(media.Id)).Id);
    }
    [MediaFact]
    public async Task AutomaticBudgetIsSharedByLanguageJobsAndLocalTracksArePreserved()
    {
        using var home = new TestWorkspace(); var (media, tools) = await Media(home); using var setup = new Setup(home, tools); await setup.Login();
        setup.Settings.SaveProfile(new(["en", "tr"], Enabled: true, AutoDownload: true, MaximumDailyDownloads: 1)); setup.Handler.HashMatched = true;
        await setup.Coordinator.RunOnceAsync(CancellationToken.None); await setup.Coordinator.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, setup.Handler.Grants); Assert.Equal(1, home.Store.AutomaticSubtitleAttemptsToday()); Assert.Single(home.Store.GetManagedSubtitles(media.Id));
        var inspected = await setup.Analysis.InspectAsync(media.Id, CancellationToken.None);
        var local = new SubtitleTrack("sidecar-fixture", "en", "Local", "srt", "sidecar", false, false, true);
        Assert.False(SubtitleCoordinator.Decide(inspected, "en", setup.Settings.Profile with { AllowUpgrades = true }, [local], []).Search);
        Assert.False(SubtitleCoordinator.Decide(inspected, "en", setup.Settings.Profile with { SkipAudioLanguage = true }, [], []).Search);
    }
    [Fact]
    public async Task OverrideResetIsMonotonicAndRecoveryCannotReuseAnOldLease()
    {
        using var home = new TestWorkspace(); home.Write("fixture.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var settings = new SubtitleSettings(home.Store, new EphemeralDataProtectionProvider()); settings.Save(new("fixture-key")); settings.SaveProfile(new(["en"], Enabled: true));
        var disabled = home.Store.SaveSubtitleOverride(media.Id, new(new(["tr"], Enabled: false))); Assert.False(disabled.Profile.Enabled);
        var inherited = home.Store.SaveSubtitleOverride(media.Id, new(null, disabled.Stamp.OverrideRevision)); Assert.True(inherited.Inherited); Assert.True(inherited.Stamp.OverrideRevision > disabled.Stamp.OverrideRevision);
        Assert.Throws<DomainException>(() => home.Store.SaveSubtitleOverride(media.Id, new(null, disabled.Stamp.OverrideRevision)));
        home.Store.ScheduleSubtitles(media.Id, inherited); var first = home.Store.ClaimSubtitleJob(DateTimeOffset.UtcNow.AddSeconds(1))!; home.Store.RecoverSubtitleJobs(); var next = home.Store.ClaimSubtitleJob(DateTimeOffset.UtcNow.AddSeconds(1))!;
        Assert.NotEqual(first.Lease.Token, next.Lease.Token); Assert.Throws<DomainException>(() => home.Store.RequireSubtitlePolicy(media.Id, first.Lease.Stamp, "en", false, first.Lease));
        var oldConnection = settings.ConnectionRevision; settings.Save(new("new-fixture-key")); settings.Quota(0, DateTimeOffset.UtcNow.AddDays(1).ToString("O"), oldConnection);
        Assert.Null(settings.Status.Remaining); Assert.Equal("new-fixture-key", settings.Secrets.ApiKey);
    }
    private static async Task<(MediaItem, MediaTools)> Media(TestWorkspace home)
    {
        home.Store.CreateOwner(new Owner("Owner", "fixture")); var tools = SubtitleBurnTests.Tools(home); var path = Path.Combine(home.Source, "Subtitle.Fixture.2020.mp4");
        await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=256x144:r=10", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000", "-t", "1", "-c:v", "libx264", "-threads", "1", "-c:a", "aac", "-metadata:s:a:0", "language=eng", path], TimeSpan.FromSeconds(20), CancellationToken.None);
        // A valid ISO-BMFF free box makes the exact OpenSubtitles hash available for this isolated small fixture.
        var padding = new byte[160000]; BinaryPrimitives.WriteInt32BigEndian(padding, padding.Length); Encoding.ASCII.GetBytes("free").CopyTo(padding, 4);
        await using (var file = new FileStream(path, FileMode.Append, FileAccess.Write)) await file.WriteAsync(padding);
        var media = await SubtitleBurnTests.Catalog(home, tools);
        return (home.Store.MatchMedia(media.Id, new("tmdb", 42, "movie", "Subtitle Fixture", "Subtitle Fixture", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", [], "tt1234567"), null, ""), tools);
    }
    private sealed class Setup : IDisposable
    {
        public Handler Handler { get; } = new(); public SubtitleSettings Settings { get; } public SubtitleAcquisition Acquisition { get; } public SubtitleCoordinator Coordinator { get; } public MediaAnalysisService Analysis { get; }
        private readonly SubtitleService _subtitles; private readonly OpenSubtitlesProvider _provider;
        public Setup(TestWorkspace home, MediaTools tools)
        {
            Settings = new(home.Store, new EphemeralDataProtectionProvider()); Settings.Save(new("fixture-key"));
            _provider = new(new Clients(Handler), Settings, home.Store); Analysis = new(home.Store, tools); _subtitles = new(home.Paths, home.Store, tools);
            Acquisition = new(home.Paths, home.Store, Analysis, _provider, _subtitles, Settings, new EventHub());
            Coordinator = new(Settings, home.Store, Analysis, _subtitles, Acquisition, new EventHub(), TimeProvider.System);
        }
        public Task<SubtitleConnection> Login() => _provider.LoginAsync(new("fixture", "fixture-password"), CancellationToken.None);
        public async Task<ManagedSubtitle> Add(string mediaId) { var candidate = Assert.Single((await Acquisition.SearchAsync(mediaId, new("en"), CancellationToken.None)).Items); return await Acquisition.AcquireAsync(mediaId, candidate.Id, CancellationToken.None); }
        public void Dispose() => _subtitles.Dispose();
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler : HttpMessageHandler
    {
        public int FileId { get; set; } = 10; public bool HashMatched { get; set; } public int Grants { get; private set; } public int FileCalls { get; private set; } public int FailFiles { get; set; } public Action? BeforeFile { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "dl.opensubtitles.com")
            {
                FileCalls++; Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Api-Key")); BeforeFile?.Invoke();
                if (FailFiles-- > 0) return new(HttpStatusCode.BadGateway);
                return new(HttpStatusCode.OK) { Content = new StringContent($"1\n00:00:00,100 --> 00:00:00,800\nISOLATED CAPTION {uri.AbsolutePath}\n", Encoding.UTF8) };
            }
            string json;
            if (uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                var token = "header." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds() }))) + ".fixture";
                json = JsonSerializer.Serialize(new { token, base_url = "api.opensubtitles.com", user = new { allowed_downloads = 50 } });
            }
            else if (uri.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
            {
                Grants++; using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var id = input.RootElement.GetProperty("file_id").GetInt64();
                json = JsonSerializer.Serialize(new { link = $"https://dl.opensubtitles.com/{id}.srt?token=sensitive-ticket", remaining = 50 - Grants, reset_time_utc = DateTimeOffset.UtcNow.AddDays(1).ToString("O") });
            }
            else
            {
                var language = QueryHelpers.ParseQuery(uri.Query)["languages"].ToString();
                json = JsonSerializer.Serialize(new { total_pages = 1, data = new[] { new { attributes = new { language, moviehash_match = HashMatched, from_trusted = true, release = "Subtitle.Fixture.2020", feature_details = new { tmdb_id = 42, imdb_id = 1234567 }, files = new[] { new { file_id = FileId + (language == "tr" ? 1 : 0), file_name = "fixture.srt" } } } } } });
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
