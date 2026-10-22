using System.Buffers.Binary;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class NativeHostIntegrationTests
{
    [Fact]
    public void WindowsArgumentsAreQuotedWithoutShellExpansionOrControlCharacters()
    {
        Assert.Equal("\"C:\\Some Folder\\game.3ds\"",NativeGpuProcess.QuoteArgument(@"C:\Some Folder\game.3ds"));
        Assert.Equal("\"abc\\\"def\"",NativeGpuProcess.QuoteArgument("abc\"def"));
        Assert.Equal("\"C:\\folder\\\\\"",NativeGpuProcess.QuoteArgument("C:\\folder\\"));
        Assert.Throws<DomainException>(()=>NativeGpuProcess.QuoteArgument("game\n--command"));
    }
    [Fact]
    public void FullInputFramePreservesEdgesWhileCoalescingMotionAndPressure()
    {
        var buffer=new NativeInputBuffer();var start=new NativePadFrame(1,1000,2000,PointerX:100,PointerY:200,Pressed:true,Keyboard:"97");buffer.StageFrame(start,ushort.MaxValue);
        for(short x=101;x<500;x++)buffer.StageFrame(start with{PointerX=x,LeftX=x},ushort.MaxValue);
        buffer.StageFrame(default,ushort.MaxValue);var pressed=buffer.NextFrame(false);Assert.True(pressed.Pressed);Assert.Equal(499,pressed.PointerX);Assert.Equal("97",pressed.Keyboard);Assert.Equal(default,buffer.NextFrame(false));
        var pressure=RetroPadProtocol.Analog(2,12,12345);Assert.Equal(5,BinaryPrimitives.ReadInt32LittleEndian(pressure.AsSpan(4)));Assert.Equal(2,BinaryPrimitives.ReadInt32LittleEndian(pressure.AsSpan(8)));Assert.Equal(12,BinaryPrimitives.ReadInt32LittleEndian(pressure.AsSpan(12)));Assert.Equal(12345,BinaryPrimitives.ReadInt16LittleEndian(pressure.AsSpan(16)));
        Assert.Throws<DomainException>(()=>NativePadFrame.From(0,[1,2]));Assert.Throws<DomainException>(()=>NativePadFrame.From(0,null,keys:[323]));Assert.Throws<DomainException>(()=>RetroPadProtocol.Peripheral(6,4,1));
        buffer.Clear();buffer.StageFrame(NativePadFrame.From(0,[0,0,0,0],new(),[],new short[16]),ushort.MaxValue);buffer.StageFrame(new(1<<8),ushort.MaxValue);Assert.Equal(1<<8,buffer.NextFrame(false).Buttons); // Initial neutral samples cannot insert a frame of input delay.
    }
    [Fact]
    public async Task NestedConsoleSavesAndEmptyFilesRoundTripAndStayBoundToOwnerAndBuild()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"engine","saves");Directory.CreateDirectory(Path.Combine(source,"Citra","sdmc","title","cache"));File.WriteAllBytes(Path.Combine(source,"Citra","sdmc","title","cache","progress"),[7]);File.WriteAllBytes(Path.Combine(source,"game.state"),[1,2,3]);File.WriteAllBytes(Path.Combine(source,"Citra","sdmc","title","save.dat"),[4,5,6]);File.WriteAllBytes(Path.Combine(source,"Citra","sdmc","title","empty"),[]);
        var snapshots=new NativeSaveSnapshots(home.Paths);var manifest=await snapshots.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None);var checkpoint=new GameCheckpoint(1,new(){{"native-save.json",manifest}},"native-fixture");var restored=AppFiles.DirectoryIn(home.Root,"restored","saves");Assert.True(await snapshots.RestoreAsync(checkpoint,"1","rom-fixture","native-fixture",restored,CancellationToken.None));Assert.Equal(new byte[]{4,5,6},File.ReadAllBytes(Path.Combine(restored,"Citra","sdmc","title","save.dat")));Assert.True(File.Exists(Path.Combine(restored,"Citra","sdmc","title","empty")));
        await Assert.ThrowsAsync<DomainException>(()=>snapshots.RestoreAsync(checkpoint,"2","rom-fixture","native-fixture",AppFiles.DirectoryIn(home.Root,"foreign"),CancellationToken.None));
        Assert.Equal(new byte[]{7},File.ReadAllBytes(Path.Combine(restored,"Citra","sdmc","title","cache","progress")));
    }
    [Fact]
    public async Task LargeManifestUsesIntegrityBoundReferenceRatherThanBreakingCheckpointBudget()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"engine","saves");var data=Path.Combine(source,"Azahar","sdmc");Directory.CreateDirectory(data);File.WriteAllBytes(Path.Combine(source,"game.state"),[1]);for(var i=0;i<220;i++)File.WriteAllBytes(Path.Combine(data,$"{i:D4}.sav"),[(byte)i]);
        var snapshots=new NativeSaveSnapshots(home.Paths);var manifest=await snapshots.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None);Assert.True(manifest.Length<32768);Assert.Contains("manifestSha256",manifest);
        var target=AppFiles.DirectoryIn(home.Root,"recovered","saves");Assert.True(await snapshots.RestoreAsync(new(1,new(){{"native-save.json",manifest}},"native-fixture"),"1","rom-fixture","native-fixture",target,CancellationToken.None));Assert.Equal(220,Directory.GetFiles(Path.Combine(target,"Azahar","sdmc")).Length);
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("Citra/../../outside")]
    [InlineData("Citra/NUL")]
    [InlineData("Citra/save.dat:other")]
    [InlineData("Azahar/code.dll")]
    public void SaveAndFirmwarePathsCannotEscapeOrTargetDevices(string path)
    {Assert.False(NativeSaveSnapshots.SafeName(path));Assert.False(NativeFirmwareLibrary.SafePath(path));}
    [Fact]
    public void RecoveryUsesCompareAndSwapAndCannotOverwriteANewerCheckpoint()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","fixture"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"recovery-fixture");var rom=home.Store.RegisterGameRom("fixture.gba",new string('a',64),512,actor,login.Id);const string version="native-fixture";
        var first=JsonSerializer.Serialize(new{gameId=rom.Id,build=version,id=Guid.NewGuid().ToString("N"),files=new[]{new{name="game.state",bytes=1,sha256=new string('b',64)}}});Assert.True(home.Store.RecoverNativeCheckpoint("1",rom.Id,version,0,first));Assert.True(home.Store.RecoverNativeCheckpoint("1",rom.Id,version,0,first));Assert.Equal(1,home.Store.GetGameCheckpoint(rom.Id,version,"1").Revision);
        var different=first.Replace(new string('b',64),new string('c',64));Assert.False(home.Store.RecoverNativeCheckpoint("1",rom.Id,version,0,different));Assert.Equal(first,home.Store.GetGameCheckpoint(rom.Id,version,"1").Files["native-save.json"]);Assert.False(home.Store.RecoverNativeCheckpoint("2",rom.Id,version,1,different));
    }
    [Fact]
    public void FirmwareFolderRequirementsAndShortValidSegmentsAreSupported()
    {
        using var home=new TestWorkspace();var catalog=new RetroArchCoreCatalog(new GameStoreTests.EnvironmentStub(home.Root));Assert.True(NativeFirmwareLibrary.IsDeclared(catalog.Require("pcsx2"),"pcsx2/bios/scph-test.bin"));Assert.True(NativeFirmwareLibrary.IsDeclared(catalog.Require("dolphin"),"dolphin-emu/Sys/GC/USA/IPL.bin"));Assert.True(NativeFirmwareLibrary.IsDeclared(catalog.Require("citra"),"Citra/nand/0000/data.bin"));Assert.False(NativeFirmwareLibrary.IsDeclared(catalog.Require("pcsx2"),"outside/file.bin"));Assert.True(NativeSystems.All.Length>80);
    }
    [Fact]
    public void RelativeMouseMotionCoalescesWithoutAReleaseJumpAndModifiersPrecedeText()
    {
        var previous=new NativePadFrame(0,MotionX:100,MotionY:-100);var next=previous with{MotionX=135,MotionY=-120,Keyboard="49,304"};
        var packets=RetroPadProtocol.PeripheralChanges(previous,next).ToArray();
        Assert.Equal(35,BinaryPrimitives.ReadInt16LittleEndian(packets[0].AsSpan(16)));Assert.Equal(-20,BinaryPrimitives.ReadInt16LittleEndian(packets[1].AsSpan(16)));
        Assert.Equal(304,BinaryPrimitives.ReadInt32LittleEndian(packets[2].AsSpan(12)));Assert.Equal(49,BinaryPrimitives.ReadInt32LittleEndian(packets[3].AsSpan(12)));
        Assert.DoesNotContain(RetroPadProtocol.PeripheralChanges(next,default,true),packet=>BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4))==2&&BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12))<2);
    }
    [Fact]
    public async Task NativeMemoryCardsRestoreBesideTheSaveDirectoryAndDoNotCaptureOtherSystemFiles()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"card-engine","saves");var cards=Path.Combine(home.Root,"card-engine","system","pcsx2","memcards");Directory.CreateDirectory(cards);File.WriteAllBytes(Path.Combine(source,"game.state"),[1]);File.WriteAllBytes(Path.Combine(cards,"Mcd001.ps2"),[2,3]);File.WriteAllText(Path.Combine(home.Root,"card-engine","system","not-a-save.txt"),"preserve");
        var saves=new NativeSaveSnapshots(home.Paths);var manifest=await saves.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None);Assert.DoesNotContain("not-a-save",manifest);
        var destination=AppFiles.DirectoryIn(home.Root,"card-restore","saves");Assert.True(await saves.RestoreAsync(new(1,new(){{"native-save.json",manifest}},"native-fixture"),"1","rom-fixture","native-fixture",destination,CancellationToken.None));Assert.Equal(new byte[]{2,3},File.ReadAllBytes(Path.Combine(home.Root,"card-restore","system","pcsx2","memcards","Mcd001.ps2")));
    }
    [Fact]
    public async Task LargeNativeSavePayloadIsDeduplicatedAndRestoresItsVerifiedBytes()
    {
        using var home=new TestWorkspace();var source=AppFiles.DirectoryIn(home.Root,"blob-engine","saves");var input=Path.Combine(source,"native.sav");using(var file=File.Create(input)){file.SetLength(65L*1024*1024);file.Position=file.Length-1;file.WriteByte(42);}
        var saves=new NativeSaveSnapshots(home.Paths);var first=await saves.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None,false);var second=await saves.PublishAsync("1","rom-fixture","native-fixture",source,CancellationToken.None,false);Assert.Contains("blob",first);Assert.Single(Directory.GetDirectories(Path.Combine(home.Paths.State,"games","native-save-objects","1")));
        var destination=AppFiles.DirectoryIn(home.Root,"blob-restore","saves");Assert.False(await saves.RestoreAsync(new(1,new(){{"native-save.json",second}},"native-fixture"),"1","rom-fixture","native-fixture",destination,CancellationToken.None));Assert.Equal(StateFolders.Hash(input),StateFolders.Hash(Path.Combine(destination,"native.sav")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartRecoversOnlyFreshClosingStatesAndRetainsNativeSaves(bool closing)
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","fixture"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"recovery-fixture");var rom=home.Store.RegisterGameRom("fixture.gba",new string('a',64),512,actor,login.Id);
        var work=AppFiles.DirectoryIn(home.Paths.State,"games","native-work",Guid.NewGuid().ToString("N"));var source=AppFiles.DirectoryIn(work,"engine","saves");File.WriteAllBytes(Path.Combine(source,"game.srm"),[7,8]);File.WriteAllBytes(Path.Combine(source,"game.state"),[9,10]);File.WriteAllText(Path.Combine(work,closing?"closing-checkpoint.json":"active-checkpoint.json"),JsonSerializer.Serialize(new NativeClosingCheckpoint(actor.Id,rom.Id,"native-fixture",0,closing)));
        using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=new NativeGameManager(home.Store,home.Paths,library,new NativeSaveSnapshots(home.Paths),new EventHub(),NullLogger<NativeGameManager>.Instance);
        await manager.StartAsync(CancellationToken.None);try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(home.Store.GetGameCheckpoint(rom.Id,"native-fixture",actor.Id).Revision==0)await Task.Delay(10,deadline.Token);var checkpoint=home.Store.GetGameCheckpoint(rom.Id,"native-fixture",actor.Id);var destination=AppFiles.DirectoryIn(home.Root,"journal-restore","saves");Assert.Equal(closing,await new NativeSaveSnapshots(home.Paths).RestoreAsync(checkpoint,actor.Id,rom.Id,"native-fixture",destination,deadline.Token));Assert.Equal(new byte[]{7,8},File.ReadAllBytes(Path.Combine(destination,"game.srm")));Assert.True(File.Exists(Path.Combine(source,"game.state")));}finally{await manager.StopAsync(CancellationToken.None);}
    }
    [Fact]
    public void CorePreferencesRemainBoundToTheCoreBinaryAndManagedPathsCannotBeOverridden()
    {
        using var home=new TestWorkspace();var options=new NativeCoreOptions(home.Paths);var hash=new string('a',64);
        options.Save("1","citra",hash,[new(0,"citra_resolution_factor","Resolution","","2x",["1x","2x"]),new(1,"citra_use_libretro_save_path","Save location","","Citra Default",["LibRetro Default","Citra Default"])]);
        options.Save("1","citra",hash,[new(2,"citra_analog_function","Analog","","C-Stick",["C-Stick","Touchscreen"])]);
        Assert.Contains("citra_resolution_factor",options.Configuration("1","citra",hash));Assert.Contains("citra_analog_function",options.Configuration("1","citra",hash));Assert.DoesNotContain("Citra Default",options.Configuration("1","citra",hash));Assert.Equal("",options.Configuration("1","citra",new string('b',64)));Assert.Equal("",options.Configuration("2","citra",hash));
        Assert.Throws<DomainException>(()=>NativeCoreOptions.ValidateChoice("option","value\"\nother = true"));
    }
    [Fact]
    public async Task UserFirmwareSeedsMissingCitraFilesButCannotReplaceRestoredNandData()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","fixture"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"firmware-fixture");var catalog=new RetroArchCoreCatalog(new GameStoreTests.EnvironmentStub(home.Root));using var firmware=new NativeFirmwareLibrary(home.Paths);var core=catalog.Require("citra");
        await firmware.ImportAsync(core,"Citra/nand/0000/data.bin",new MemoryStream([1,2]),2,actor,home.Store,login.Id,CancellationToken.None);var system=AppFiles.DirectoryIn(home.Root,"seed-engine","system");var restored=Path.Combine(home.Root,"seed-engine","saves","Citra","nand","0000");Directory.CreateDirectory(restored);File.WriteAllBytes(Path.Combine(restored,"data.bin"),[7,8]);
        await firmware.StageAsync(core,new("rom-fixture",actor.Id,"3ds","fixture.3ds","Fixture",new string('a',64),16,""),system,CancellationToken.None);Assert.Equal(new byte[]{7,8},File.ReadAllBytes(Path.Combine(restored,"data.bin")));Assert.Single(firmware.Files(actor.Id,"citra"));
        Assert.True(NativeFirmwareLibrary.IsDeclared(catalog.Require("cemu"),"Cemu/keys.txt"));Assert.Equal("gl",NativeGpuRuntime.Renderer(catalog.Require("nuance")));
    }
    [Fact]
    public async Task ComputerDiskWritesAreCheckpointedWithoutChangingTheImportedOriginal()
    {
        using var home=new TestWorkspace();_=new RetroArchCoreCatalog(new GameStoreTests.EnvironmentStub(home.Root));home.Store.CreateOwner(new("Owner","fixture"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"writable-fixture");using var library=new NativeRomLibrary(home.Paths,home.Store);
        var rom=await library.ImportAsync(new MemoryStream(new byte[512]),"isolated.d64",512,actor,login.Id,CancellationToken.None,"commodore_c64");var work=AppFiles.DirectoryIn(home.Root,"disk-engine");var staged=await library.StageContentAsync(rom,work,CancellationToken.None);var playable=await library.PrepareWritableContentAsync(rom,work,staged,CancellationToken.None);Assert.NotEqual(staged,playable);await File.WriteAllBytesAsync(Path.Combine(work,playable),[7,8,9]);
        using(var original=library.OpenVerified(rom)){Assert.Equal(512,original.Length);Assert.Equal(0,original.ReadByte());}
        var snapshots=new NativeSaveSnapshots(home.Paths);var manifest=await snapshots.PublishAsync(actor.Id,rom.Id,"native-fixture",Path.Combine(work,"saves"),CancellationToken.None,false);var next=AppFiles.DirectoryIn(home.Root,"disk-next");staged=await library.StageContentAsync(rom,next,CancellationToken.None);Directory.CreateDirectory(Path.Combine(next,"saves"));await snapshots.RestoreAsync(new(1,new(){{"native-save.json",manifest}},"native-fixture"),actor.Id,rom.Id,"native-fixture",Path.Combine(next,"saves"),CancellationToken.None);playable=await library.PrepareWritableContentAsync(rom,next,staged,CancellationToken.None);Assert.Equal(new byte[]{7,8,9},File.ReadAllBytes(Path.Combine(next,playable)));
    }
    [Fact]
    public async Task ClosedSessionAcknowledgementIsRetryableOnlyForTheOwningLogin()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","fixture"));var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"exit-fixture");var game=new GameManifest("rom-fixture","Original isolated fixture","","","","","native-fixture","RetroArch","user-supplied","native","16/9",[],["native-save.json"],[],"native");
        var session=home.Store.StartGameSession(game,new(Guid.NewGuid().ToString("N"),new string('a',64)),actor,login.Id);home.Store.CloseGameSession(session.Id,actor,login.Id);using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=new NativeGameManager(home.Store,home.Paths,library,new NativeSaveSnapshots(home.Paths),new EventHub(),NullLogger<NativeGameManager>.Instance);
        Assert.True((await manager.CommandAsync(session.Id,"end",actor,login.Id,CancellationToken.None)).Paused);await Assert.ThrowsAsync<DomainException>(()=>manager.CommandAsync(session.Id,"end",actor,"different-login",CancellationToken.None));Assert.Empty(home.Store.GetActiveGameSessions());
    }
}
