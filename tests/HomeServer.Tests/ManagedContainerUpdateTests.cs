using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class ManagedContainerUpdateTests
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private static ContainerUpdateCandidate Candidate(char value = 'b') => new("0.99.0", new string(value, 40), "sha256:" + new string(value, 64), 100, 40);
    private static ContainerUpdateCoordinator Coordinator(TestWorkspace home) => new(home.Paths, home.Store, new(home.Paths, home.Store),
        new(TimeProvider.System), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Home:Updates:Managed"] = "true", ["HOME_SOURCE_REVISION"] = new string('a', 40) }).Build(), TimeProvider.System,
        NullLogger<ContainerUpdateCoordinator>.Instance, supported: true);

    [Fact]
    public async Task DefaultAndLegacyEnabledPreferenceDoNotPermitAutomaticInstallation()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home);
        Assert.True(updates.Enabled); Assert.False(updates.AutomaticInstall);
        home.Store.SetSetting("operations.container-auto-updates", "{\"enabled\":true}");
        await updates.ReportCandidateAsync(Candidate(), null, Token);
        await updates.AgentAsync(new(1, "status"), Token);
        Assert.Null(updates.Availability().ApprovedDigest); Assert.Equal(75, (await updates.PrepareAsync(Token)).ExitCode);
        Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }
    [Fact]
    public async Task ApprovalRequiresConnectedHelperAndExactCurrentDigest()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home); await updates.ReportCandidateAsync(Candidate(), null, Token);
        await Assert.ThrowsAsync<DomainException>(() => updates.ApproveAsync(Candidate().Digest, Token));
        await updates.AgentAsync(new(1, "status"), Token);
        await Assert.ThrowsAsync<DomainException>(() => updates.ApproveAsync(Candidate('c').Digest, Token));
        await updates.ApproveAsync(Candidate().Digest, Token);
        Assert.Equal(Candidate().Digest, Coordinator(home).Availability().ApprovedDigest);
        Assert.Equal(75, (await updates.PrepareAsync(Token)).ExitCode); // approval alone is not a downloaded image
    }
    [Fact]
    public async Task AChannelChangeCannotMoveTheApprovedTargetAndCancelDoesNotTouchData()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new("Isolated update owner", "preserved"));
        var transfer = home.Store.AddDownload(["isolated-paused"], "Isolated fixture", "torrent", "no-network", 4);
        home.Store.SetDownloadIntent(transfer.Id, "paused");
        var updates = Coordinator(home); await updates.ReportCandidateAsync(Candidate(), null, Token);
        await updates.AgentAsync(new(1, "status"), Token); await updates.ApproveAsync(Candidate().Digest, Token);
        await updates.ReportCandidateAsync(Candidate('c'), null, Token);
        Assert.Equal(Candidate().Digest, updates.Availability().Candidate!.Digest);
        await updates.CancelApprovalAsync(Token); Assert.Null(updates.Availability().ApprovedDigest);
        Assert.Equal("paused", home.Store.GetDownload(transfer.Id)!.DesiredState);
        Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }
    [Fact]
    public async Task ExplicitAutomaticOptInIsDurableAndPausingPreventsPreparation()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home); updates.Configure(new(true, true));
        Assert.True(Coordinator(home).AutomaticInstall);
        await updates.ReportCandidateAsync(Candidate(), null, Token); await updates.AgentAsync(new(1, "status"), Token);
        Assert.Equal(Candidate().Digest, updates.Availability().ApprovedDigest);
        await updates.AgentAsync(new(1, "downloading", Candidate().Digest), Token); await updates.AgentAsync(new(1, "ready", Candidate().Digest), Token);
        updates.Configure(new(false, false)); Assert.Equal(75, (await updates.PrepareAsync(Token)).ExitCode);
        Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }
    [Fact]
    public async Task WrongHelperTargetAndUnsupportedProtocolCannotArmAnUpdate()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home);
        await Assert.ThrowsAsync<DomainException>(() => updates.AgentAsync(new(2, "status"), Token));
        await updates.ReportCandidateAsync(Candidate(), null, Token); await updates.AgentAsync(new(1, "status"), Token);
        await updates.ApproveAsync(Candidate().Digest, Token);
        await Assert.ThrowsAsync<DomainException>(() => updates.AgentAsync(new(1, "ready", Candidate().Digest), Token));
        await Assert.ThrowsAsync<DomainException>(() => updates.AgentAsync(new(1, "downloading", Candidate('c').Digest), Token));
        Assert.Equal("approved", updates.Availability().Phase);
    }
    [Fact]
    public async Task DownloadFailureRetainsCandidateButRequiresFreshApproval()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home);
        await updates.ReportCandidateAsync(Candidate(), null, Token); await updates.AgentAsync(new(1, "status"), Token);
        await updates.ApproveAsync(Candidate().Digest, Token); await updates.AgentAsync(new(1, "downloading", Candidate().Digest), Token);
        await updates.AgentAsync(new(1, "failed", Candidate().Digest), Token);
        Assert.Equal("failed", updates.Availability().Phase); Assert.Null(updates.Availability().ApprovedDigest);
        Assert.NotNull(updates.Availability().Candidate); Assert.Empty(new BackupService(home.Paths, home.Store).List());
    }
    [Fact]
    public async Task CancellationDoesNotImmediatelyReapproveTheSameAutomaticRelease()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home); updates.Configure(new(true, true));
        await updates.ReportCandidateAsync(Candidate(), null, Token); await updates.AgentAsync(new(1, "status"), Token);
        await updates.CancelApprovalAsync(Token); await updates.AgentAsync(new(1, "status"), Token);
        Assert.Null(updates.Availability().ApprovedDigest);
        await updates.ReportCandidateAsync(Candidate('c'), null, Token); await updates.AgentAsync(new(1, "status"), Token);
        Assert.Equal(Candidate('c').Digest, updates.Availability().ApprovedDigest);
    }
    [Fact]
    public async Task FailedMetadataCheckDoesNotDiscardLastKnownRelease()
    {
        using var home = new TestWorkspace(); var updates = Coordinator(home);
        await updates.ReportCandidateAsync(Candidate(), null, Token); await updates.ReportCandidateAsync(null, "Offline", Token);
        Assert.Equal(Candidate().Digest, updates.Availability().Candidate!.Digest); Assert.Equal("Offline", updates.Availability().Error);
    }
    private sealed class Registry(bool corrupt = false, string version = "0.99.0") : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        private static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            var config = JsonSerializer.SerializeToUtf8Bytes(new { os = "linux", architecture = "amd64", config = new { Labels = new Dictionary<string, string>
            { ["org.opencontainers.image.source"] = "https://github.com/lumax90/homeOS", ["org.opencontainers.image.version"] = version, ["org.opencontainers.image.revision"] = new string('b', 40) } } });
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, config = new { digest = Digest(config), size = config.Length },
                layers = new[] { new { digest = "sha256:" + new string('c', 64), size = 100 } } });
            var bytes = path == "/token" ? Encoding.UTF8.GetBytes("{\"token\":\"isolated-token\"}") : path.Contains("/manifests/") ? manifest : config;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (path != "/token") response.Headers.TryAddWithoutValidation("Docker-Content-Digest", corrupt ? "sha256:" + new string('f', 64) : Digest(bytes));
            return Task.FromResult(response);
        }
    }
    [Fact]
    public async Task RegistryDiscoveryFetchesOnlyMetadataAndNoLayerPayloads()
    {
        using var handler = new Registry(); using var client = new HttpClient(handler);
        var candidate = await new ContainerReleaseSource(client).CheckAsync(BuildInfo.Version, new string('a', 40), null, Token);
        Assert.NotNull(candidate); Assert.Equal(100, candidate.TotalBytes); Assert.Null(candidate.EstimatedDownloadBytes);
        Assert.Equal(3, handler.Requests.Count); Assert.Single(handler.Requests, path => path.Contains("/blobs/"));
    }
    [Fact]
    public async Task CorruptRegistryDigestAndReleaseDowngradesAreRejected()
    {
        using var corrupt = new Registry(true); using var client = new HttpClient(corrupt);
        await Assert.ThrowsAsync<DomainException>(() => new ContainerReleaseSource(client).CheckAsync(BuildInfo.Version, null, null, Token));
        using var older = new Registry(version: "0.1.0"); using var oldClient = new HttpClient(older);
        await Assert.ThrowsAsync<DomainException>(() => new ContainerReleaseSource(oldClient).CheckAsync(BuildInfo.Version, null, null, Token));
    }
}
