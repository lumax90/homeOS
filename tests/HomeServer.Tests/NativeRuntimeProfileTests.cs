using System.Text.Json;
using System.Text.Json.Nodes;
using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Tests;

public sealed class NativeRuntimeProfileTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static NativeRuntimeProfile Profile(TestWorkspace home)
    {
        var file = new NativeRuntimeFile(Path.Combine(home.Root, "not-launched.exe"), 1, new string('a', 64));
        return new(1, true, "0.10.5", "26b7884bc25a5933960f3cdcd98bac1ae14d42e2", file with { Sha256 = NativeRuntimeProfile.RejectedRemotePadSha256 }, file, file, file, []);
    }
    private static string Write(TestWorkspace home, string text)
    {
        var root = AppFiles.DirectoryIn(home.Paths.State, "games");
        var path = Path.Combine(root, "native-runtime-profile.json"); File.WriteAllText(path, text); return path;
    }
    [Fact]
    public void MissingProfileDoesNotCreateOrEnableARuntime()
    {
        using var home = new TestWorkspace(); Assert.Null(NativeRuntimeProfile.Read(home.Paths));
        Assert.False(Directory.Exists(Path.Combine(home.Paths.State, "games")));
    }
    [Fact]
    public void LegacyProfileWithoutHeldInputEvidenceRemainsDisabledForPlay()
    {
        using var home = new TestWorkspace(); var content = JsonSerializer.SerializeToNode(Profile(home), Json)!.AsObject(); content.Remove("continuousInputVerified");
        Write(home, content.ToJsonString()); var profile = NativeRuntimeProfile.Read(home.Paths)!;
        Assert.True(profile.Enabled); Assert.False(profile.ContinuousInputVerified); Assert.False(profile.InputReady);
        Assert.Equal(409, Assert.Throws<DomainException>(profile.Verify).StatusCode);
        Assert.Empty(home.Store.GetActiveGameSessions()); Assert.False(File.Exists(profile.RetroArch.Path));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownFailingWindowsBinaryCannotBePromotedWithAHandwrittenFlag(bool evidence)
    {
        using var home = new TestWorkspace(); var requested = Profile(home) with { ContinuousInputVerified = evidence };
        Write(home, JsonSerializer.Serialize(requested, Json)); var profile = NativeRuntimeProfile.Read(home.Paths)!;
        Assert.False(profile.InputReady); Assert.Equal(409, Assert.Throws<DomainException>(profile.Verify).StatusCode);
        Assert.False((profile with { RetroArch = profile.RetroArch with { Sha256 = profile.RetroArch.Sha256.ToUpperInvariant() } }).InputReady);
        Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"not-an-integer\"}")]
    public void MalformedProfileReportsAConflictAndPreservesTheFile(string content)
    {
        using var home = new TestWorkspace(); var path = Write(home, content);
        Assert.Equal(409, Assert.Throws<DomainException>(() => NativeRuntimeProfile.Read(home.Paths)).StatusCode);
        Assert.Equal(content, File.ReadAllText(path));
    }
    [Fact]
    public void NullDescriptorHashCannotBeReadAsAPlayableBuild()
    {
        using var home = new TestWorkspace(); var content = JsonSerializer.SerializeToNode(Profile(home), Json)!.AsObject();
        content["retroArch"]!["sha256"] = null; Write(home, content.ToJsonString());
        Assert.Equal(409, Assert.Throws<DomainException>(() => NativeRuntimeProfile.Read(home.Paths)).StatusCode);
    }
}
