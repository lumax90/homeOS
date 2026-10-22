using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

namespace HomeServer.Tests;

public sealed class NativeSaveSnapshotTests
{
    [Fact]
    public async Task BinarySnapshotUsesBoundedManifestAndRestoresExactBytesWithoutOverwritingConflict()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"native-state");var bytes=new byte[]{1,2,3,4,5};File.WriteAllBytes(Path.Combine(source,"game.state"),bytes);
        var snapshots=new NativeSaveSnapshots(home.Paths);var text=await snapshots.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None);
        Assert.DoesNotContain(Convert.ToBase64String(bytes),text);Assert.Contains("sha256",text);
        var target=AppFiles.DirectoryIn(home.Root,"native-restored");var checkpoint=new GameCheckpoint(1,new(){["native-save.json"]=text},"native-fixture");
        Assert.True(await snapshots.RestoreAsync(checkpoint,"1","rom-fixture","native-fixture",target,CancellationToken.None));Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(target,"game.state")));
        File.WriteAllText(Path.Combine(target,"game.state"),"conflicting user bytes");
        Assert.Equal(409,(await Assert.ThrowsAsync<DomainException>(()=>snapshots.RestoreAsync(checkpoint,"1","rom-fixture","native-fixture",target,CancellationToken.None))).StatusCode);
        Assert.Equal("conflicting user bytes",File.ReadAllText(Path.Combine(target,"game.state")));
    }
    [Fact]
    public async Task SnapshotManifestCannotCrossUserGameOrBuildBoundaries()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"native-state");File.WriteAllBytes(Path.Combine(source,"game.state"),[7,8,9]);var snapshots=new NativeSaveSnapshots(home.Paths);
        var text=await snapshots.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None);var checkpoint=new GameCheckpoint(1,new(){["native-save.json"]=text},"native-fixture");var target=AppFiles.DirectoryIn(home.Root,"target");
        await Assert.ThrowsAsync<DomainException>(()=>snapshots.RestoreAsync(checkpoint,"1","rom-other","native-fixture",target,CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(()=>snapshots.RestoreAsync(checkpoint,"1","rom-fixture","native-other",target,CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(()=>snapshots.RestoreAsync(checkpoint,"2","rom-fixture","native-fixture",target,CancellationToken.None));Assert.Empty(Directory.GetFiles(target));
    }
    [Fact]
    public async Task MissingNativeStateDoesNotProduceAFakeSaveReceipt()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"native-state");File.WriteAllBytes(Path.Combine(source,"game.srm"),[1,2]);var snapshots=new NativeSaveSnapshots(home.Paths);
        Assert.Equal(409,(await Assert.ThrowsAsync<DomainException>(()=>snapshots.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None))).StatusCode);
    }
}
