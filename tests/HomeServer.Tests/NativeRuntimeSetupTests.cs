using System.Text.Json;
using System.Text.Json.Nodes;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class NativeRuntimeSetupTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Account Owner(TestWorkspace home) { home.Store.CreateOwner(new("Owner", "isolated-secret")); return home.Store.GetAccount("1")!; }
    private static NativeGameManager Manager(TestWorkspace home, NativeRomLibrary library) => new(home.Store, home.Paths, library, new(home.Paths), new(), NullLogger<NativeGameManager>.Instance);
    private sealed class FilesVerifier : INativeRuntimeVerifier
    {
        public bool Reject;
        public void Verify(NativeRuntimeProfile profile)
        {
            if (Reject) throw new DomainException("isolated-prerequisite-failure", 409);
            NativeRuntimeProfile.ValidateDefinition(profile);
            Assert.True(profile.Enabled); Assert.True(profile.InputReady);
            foreach (var file in profile.Dependencies.Concat([profile.RetroArch,profile.Core,profile.CoreInfo,profile.MediaMtx])) VerifiedFilePublication.Require(file.Path,file.Bytes,file.Sha256);
        }
    }
    private static NativeRuntimeCandidate Candidate(TestWorkspace home)
    {
        var root = AppFiles.DirectoryIn(home.Root, "isolated-runtime-files"); var file = Path.Combine(root,"never-executed.bin"); File.WriteAllBytes(file,[7,8,9]);
        var descriptor = new NativeRuntimeFile(file,3,StateFolders.Hash(file));
        var profile = new NativeRuntimeProfile(1,false,"0.10.5","26b7884bc25a5933960f3cdcd98bac1ae14d42e2",descriptor,descriptor,descriptor,descriptor,[],true);
        var proof = new { error=(string?)null,frontendSha256=descriptor.Sha256,coreSha256=descriptor.Sha256,patchedPrototype=true,upstreamInputFix="cfb1204314dd6b061a7b1cda77221735939bf5ba",
            streaming=new {simultaneousHeldInputVerified=true,held=new {consecutiveHeldFrames=10},nativeSaveRestoreVerified=true,windowless=true,privateInputGuard=true,whepStatus=201,negotiatedH264Opus=true} };
        var proofPath=Path.Combine(root,"SYNTHETIC-UNIT-PROOF.json");File.WriteAllText(proofPath,JsonSerializer.Serialize(proof,Json));
        var candidate=new NativeRuntimeCandidate(1,"1.22.2","69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576","cfb1204314dd6b061a7b1cda77221735939bf5ba","1.21.2",profile,new(proofPath,new FileInfo(proofPath).Length,StateFolders.Hash(proofPath)));
        File.WriteAllText(Path.Combine(AppFiles.DirectoryIn(home.Paths.State,"games"),"native-runtime-candidate.json"),JsonSerializer.Serialize(candidate,Json));return candidate;
    }
    [Fact]
    public async Task PreparedDescriptorDoesNotEnableOrLaunchUntilExplicitOwnerCommandAndTogglePreservesUserFiles()
    {
        using var home=new TestWorkspace();var owner=Owner(home);var candidate=Candidate(home);using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=Manager(home,library);
        var setup=new NativeRuntimeSetup(home.Paths,manager,new FilesVerifier(),new());var initial=setup.Status(owner,true);
        Assert.True(initial.Prepared);Assert.False(initial.Enabled);Assert.False(initial.Available);Assert.Null(NativeRuntimeProfile.Read(home.Paths));
        Directory.CreateDirectory(Path.Combine(home.Source,"user-library"));Directory.CreateDirectory(Path.Combine(home.Source,"existing-save"));
        var userFile=home.Write("user-library/keep.gba",[1,2,3]);var saved=home.Write("existing-save/keep.state",[4,5,6]);
        var enabled=await setup.ConfigureAsync(new(true,true,initial.Version),owner,true,CancellationToken.None);Assert.True(enabled.Enabled);Assert.True(enabled.Available);
        var path=Path.Combine(home.Paths.State,"games","native-runtime-profile.json");var content=JsonNode.Parse(File.ReadAllText(path))!;content["operatorNote"]="preserve this user metadata";File.WriteAllText(path,content.ToJsonString());
        var disabled=await setup.ConfigureAsync(new(false,false,enabled.Version),owner,true,CancellationToken.None);Assert.False(disabled.Enabled);Assert.False(disabled.Available);Assert.True(disabled.Prepared);
        Assert.Equal("preserve this user metadata",JsonNode.Parse(File.ReadAllText(path))!["operatorNote"]!.GetValue<string>());
        Assert.Equal([1,2,3],File.ReadAllBytes(userFile));Assert.Equal([4,5,6],File.ReadAllBytes(saved));Assert.Empty(home.Store.GetActiveGameSessions());Assert.False(Directory.Exists(Path.Combine(home.Paths.State,"games","native-work")));
        Assert.False(NativeRuntimeProfile.Read(home.Paths)!.Enabled);Assert.Equal(candidate.Profile.Version,disabled.Version);
    }
    [Fact]
    public async Task RemoteOwnerCanConfigureButMemberAndChangedProofCannotEnable()
    {
        using var home=new TestWorkspace();var owner=Owner(home);var candidate=Candidate(home);using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=Manager(home,library);var setup=new NativeRuntimeSetup(home.Paths,manager,new FilesVerifier(),new());
        var member=owner with {Role="viewer"};Assert.False(setup.Status(member,true).CanConfigure);Assert.True(setup.Status(owner,false).CanConfigure);Assert.False(setup.Status(owner,false).LocalOnly);
        Assert.Equal(403,(await Assert.ThrowsAsync<DomainException>(()=>setup.ConfigureAsync(new(true,true,candidate.Profile.Version),member,true,CancellationToken.None))).StatusCode);
        Assert.True((await setup.ConfigureAsync(new(true,false,candidate.Profile.Version),owner,false,CancellationToken.None)).Available);
        await setup.ConfigureAsync(new(false,false,candidate.Profile.Version),owner,false,CancellationToken.None);
        File.WriteAllText(candidate.Proof.Path,"changed proof");Assert.False(setup.Status(owner,true).Prepared);
        Assert.Equal(409,(await Assert.ThrowsAsync<DomainException>(()=>setup.ConfigureAsync(new(true,true,candidate.Profile.Version),owner,true,CancellationToken.None))).StatusCode);Assert.False(NativeRuntimeProfile.Read(home.Paths)!.Enabled);
    }
    [Fact]
    public async Task FailedPrerequisiteCancellationStaleVersionAndConflictingProfileAreFailClosed()
    {
        using var home=new TestWorkspace();var owner=Owner(home);var candidate=Candidate(home);using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=Manager(home,library);var verifier=new FilesVerifier{Reject=true};var setup=new NativeRuntimeSetup(home.Paths,manager,verifier,new());
        await Assert.ThrowsAsync<DomainException>(()=>setup.ConfigureAsync(new(true,true,candidate.Profile.Version),owner,true,CancellationToken.None));Assert.Null(NativeRuntimeProfile.Read(home.Paths));verifier.Reject=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>setup.ConfigureAsync(new(true,true,candidate.Profile.Version),owner,true,new CancellationToken(true)));
        await Assert.ThrowsAsync<DomainException>(()=>setup.ConfigureAsync(new(true,true,"stale-version"),owner,true,CancellationToken.None));
        var different=candidate.Profile with {CoreInfo=candidate.Profile.CoreInfo with {Bytes=4}};var target=Path.Combine(home.Paths.State,"games","native-runtime-profile.json");var original=JsonSerializer.Serialize(different,Json);File.WriteAllText(target,original);
        await Assert.ThrowsAsync<DomainException>(()=>setup.ConfigureAsync(new(true,true,different.Version),owner,true,CancellationToken.None));Assert.Equal(original,File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!,".native-setup-*.partial"));Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sourceHarnessOnly\":true,\"fixedHeld\":true}")]
    [InlineData("null")]
    public void SourceOnlyOrMalformedProofIsNotFullNativeAcceptance(string content)
    {
        using var home=new TestWorkspace();var candidate=Candidate(home);
        Assert.Equal(409,Assert.Throws<DomainException>(()=>NativeRuntimeCandidate.RequireProof(candidate.Profile,content)).StatusCode);
    }
}
