using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class PspImportAndRemovalTests
{
    public static byte[] PspIso()
    {
        var bytes=new byte[2048*110];var pvd=bytes.AsSpan(16*2048);pvd[0]=1;"CD001"u8.CopyTo(pvd[1..]);pvd[6]=1;
        BinaryPrimitives.WriteUInt16LittleEndian(pvd[128..],2048);pvd[156]=34;BinaryPrimitives.WriteUInt32LittleEndian(pvd[158..],70);BinaryPrimitives.WriteUInt32LittleEndian(pvd[166..],2048);
        var directory=bytes.AsSpan(70*2048);Record(directory,"PSP_GAME"u8,true,80);Record(directory[48..],"UMD_DATA.BIN;1"u8,false,90);
        return bytes;
        static void Record(Span<byte> record,ReadOnlySpan<byte> name,bool folder,uint block)
        {record[0]=48;BinaryPrimitives.WriteUInt32LittleEndian(record[2..],block);BinaryPrimitives.WriteUInt32LittleEndian(record[10..],2048);record[25]=folder?(byte)2:(byte)0;record[32]=(byte)name.Length;name.CopyTo(record[33..]);}
    }
    private static(Account Actor,LoginSession Login) Owner(TestWorkspace home)
    {home.Store.CreateOwner(new("Isolated PSP owner","preserved"));var actor=home.Store.GetAccount("1")!;return(actor,home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"isolated-PSP"));}
    [Fact]
    public async Task IsoStructureSuggestsPspAndCannotBePublishedAsPs1()
    {
        var bytes=PspIso();var input=new NativeRomProbeFile("ORIGINAL.iso",bytes.Length,Convert.ToBase64String(bytes[..65536]));var result=NativeRomDetection.Detect(new([input],input.Name));
        Assert.Equal("reading-disc",result.Reason);var read=Assert.Single(result.Reads!);
        input=input with{Samples=[new(read.Offset,Convert.ToBase64String(bytes.AsSpan((int)read.Offset,read.Length)))]};
        Assert.Equal("psp",NativeRomDetection.Detect(new([input],input.Name)).SystemId);
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);
        await Assert.ThrowsAsync<DomainException>(()=>roms.ImportAsync(new MemoryStream(bytes),input.Name,bytes.Length,actor,login.Id,CancellationToken.None,"ps1"));
        Assert.Empty(home.Store.GetGameRoms(actor));
        var psp=await roms.ImportAsync(new MemoryStream(bytes),input.Name,bytes.Length,actor,login.Id,CancellationToken.None,"psp");Assert.Equal("psp",psp.System);
    }
    [Fact]
    public async Task CorrectingAnOldWrongConsoleKeepsGameIdentityPreferencesAndSourceBytes()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var bytes=PspIso();var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var root=AppFiles.DirectoryIn(home.Paths.State,"games","roms","ps1",hash);File.WriteAllBytes(Path.Combine(root,"game.iso"),bytes);
        var old=home.Store.RegisterGameRom("ORIGINAL.iso",hash,bytes.Length,actor,login.Id,"ps1");home.Store.SetGamePreference(old.Id,new(true,true),actor);
        using var roms=new NativeRomLibrary(home.Paths,home.Store);GameRom? corrected=null;
        await roms.WithContentLockAsync(async token=>corrected=await roms.CorrectPspSystemAsync(old,actor,login.Id,token),CancellationToken.None);
        Assert.Equal(old.Id,corrected!.Id);Assert.Equal("psp",corrected.System);Assert.True(home.Store.GetGameStates(actor.Id).Single().Favorite);
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,"game.iso")));await using var source=roms.OpenVerified(corrected);Assert.Equal(bytes.Length,source.Length);
    }
    private static byte[] Cartridge()
    {var bytes=new byte[512];bytes[3]=0xea;bytes[0xb2]=0x96;var sum=0x19;for(var i=0xa0;i<=0xbc;i++)sum+=bytes[i];bytes[0xbd]=(byte)-sum;return bytes;}
    private static async Task<GameRom> Import(NativeRomLibrary roms,Account actor,LoginSession login) => await roms.ImportAsync(new MemoryStream(Cartridge()),"ORIGINAL.gba",512,actor,login.Id,CancellationToken.None);
    private static GameRemoval Removal(TestWorkspace home,NativeRomLibrary roms)
    {
        var events=new EventHub();var native=new NativeGameManager(home.Store,home.Paths,roms,new(home.Paths),events,NullLogger<NativeGameManager>.Instance);
        return new(home.Paths,home.Store,null!,roms,native,events);
    }
    private static async Task Remove(TestWorkspace home,NativeRomLibrary roms,GameRom rom,Account actor)
    {
        home.Store.QueueGameRemoval(rom.Id,new GameRemovalPlan("native",rom.System,rom.Sha256,$"games/roms/{rom.System}/{rom.Sha256}",roms.RemovalFiles(rom)),actor);
        await Removal(home,roms).RunAsync(home.Store.ClaimProductJob(["game-uninstall"])!,CancellationToken.None);
    }
    [Fact]
    public async Task UninstallDeletesOnlyManagedPayloadAndReimportKeepsIdentityAndFavorites()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);
        var original=home.Write("ORIGINAL.gba",Cartridge());var rom=await Import(roms,actor,login);home.Store.SetGamePreference(rom.Id,new(true,true),actor);
        var game=new GameManifest(rom.Id,rom.Title,"","","","","original-preserved-build","","user-supplied","native","16/9",[],["native-save.json"],[],"native");
        var work=AppFiles.DirectoryIn(home.Root,"isolated-work","saves");File.WriteAllBytes(Path.Combine(work,"game.state"),[7,8,9]);var memstick=AppFiles.DirectoryIn(work,"PSP","SAVEDATA","ISOLATED");File.WriteAllBytes(Path.Combine(memstick,"DATA.BIN"),[1,2,3,4]);
        var snapshot=await new NativeSaveSnapshots(home.Paths).PublishAsync(actor.Id,rom.Id,game.Version,work,CancellationToken.None);
        var session=home.Store.StartGameSession(game,new(Guid.NewGuid().ToString("N"),new string('a',64)),actor,login.Id);
        home.Store.EndGameSession(session.Id,game,new(false,0,new(){{"native-save.json",snapshot}}),actor,login.Id);
        var root=Path.Combine(home.Paths.State,"games","roms","gba",rom.Sha256);File.WriteAllText(Path.Combine(root,"USER-NOTES.txt"),"preserved");
        await Remove(home,roms,rom,actor);
        Assert.False(File.Exists(Path.Combine(root,"game.gba")));Assert.True(File.Exists(original));Assert.Equal("preserved",File.ReadAllText(Path.Combine(root,"USER-NOTES.txt")));
        var state=home.Store.GetGameStates(actor.Id).Single();Assert.False(state.InLibrary);Assert.True(state.Favorite);
        var receipt=home.Store.GetProductJobs(actor,"game-uninstall",rom.Id).Single();Assert.Equal(receipt.Id,home.Store.QueueGameRemoval(rom.Id,receipt.Input,actor).Id);
        Assert.Single(home.Store.GetProductJobs(actor,"game-uninstall",rom.Id));
        var again=await Import(roms,actor,login);Assert.Equal(rom.Id,again.Id);Assert.True(home.Store.GetGameStates(actor.Id).Single().InLibrary);Assert.True(home.Store.GetGameStates(actor.Id).Single().Favorite);
        var checkpoint=home.Store.GetGameCheckpoint(rom.Id,game.Version,actor.Id);Assert.Equal(snapshot,checkpoint.Files["native-save.json"]);
        var restored=AppFiles.DirectoryIn(home.Root,"isolated-restored","saves");Assert.True(await new NativeSaveSnapshots(home.Paths).RestoreAsync(checkpoint,actor.Id,rom.Id,game.Version,restored,CancellationToken.None));
        Assert.Equal(new byte[]{1,2,3,4},File.ReadAllBytes(Path.Combine(restored,"PSP","SAVEDATA","ISOLATED","DATA.BIN")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPayloadSurvivesUntilTheLastProfileRemovesIt(bool missingPreference)
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var first=await Import(roms,actor,login);
        var other=home.Store.SaveAccount(null,new("Isolated other",CanAcquire:true),"preserved");var secondLogin=home.Store.CreateLoginSession(other.Id,home.Store.GetAccountSecret(other.Id)!.Version,"isolated-other");var second=await Import(roms,other,secondLogin);
        if(missingPreference){using var db=new SqliteConnection($"Data Source={Path.Combine(home.Paths.Data,"home.db")}");db.Open();using var command=db.CreateCommand();command.CommandText="DELETE FROM game_user_state WHERE user_id=$user AND game_id=$game";command.Parameters.AddWithValue("$user",other.Id);command.Parameters.AddWithValue("$game",second.Id);command.ExecuteNonQuery();}
        var path=Path.Combine(home.Paths.State,"games","roms","gba",first.Sha256,"game.gba");await Remove(home,roms,first,actor);Assert.True(File.Exists(path));
        if(!missingPreference)Assert.True(home.Store.GetGameStates(other.Id).Single().InLibrary);Assert.NotNull(home.Store.GetGameRom(second.Id,other));await Remove(home,roms,second,other);Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task TamperedManagedCopyIsNotDeletedAndPendingRemovalCannotReactivateTheGame()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var rom=await Import(roms,actor,login);
        var plan=new GameRemovalPlan("native",rom.System,rom.Sha256,$"games/roms/{rom.System}/{rom.Sha256}",roms.RemovalFiles(rom));var job=home.Store.QueueGameRemoval(rom.Id,plan,actor);
        Assert.Throws<DomainException>(()=>home.Store.SetGamePreference(rom.Id,new(true,false),actor));
        var path=Path.Combine(home.Paths.State,"games","roms","gba",rom.Sha256,"game.gba");File.WriteAllBytes(path,new byte[512]);
        await Assert.ThrowsAsync<DomainException>(()=>Removal(home,roms).RunAsync(home.Store.ClaimProductJob(["game-uninstall"])!,CancellationToken.None));
        Assert.True(File.Exists(path));Assert.True(home.Store.GetGameStates(actor.Id).Single().InLibrary);
    }
    [Fact]
    public async Task InterruptedDeletionIsRecoveredIdempotentlyWithoutLosingSavesOrIdentity()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var rom=await Import(roms,actor,login);
        var plan=new GameRemovalPlan("native",rom.System,rom.Sha256,$"games/roms/{rom.System}/{rom.Sha256}",roms.RemovalFiles(rom));home.Store.QueueGameRemoval(rom.Id,plan,actor);var job=home.Store.ClaimProductJob(["game-uninstall"])!;
        var path=Path.Combine(home.Paths.State,"games","roms","gba",rom.Sha256,"game.gba");
        Assert.Throws<IOException>(()=>home.Store.ApplyGameRemoval(job,rom,()=>{File.Delete(path);throw new IOException("Original isolated interruption after one declared file");}));
        Assert.True(home.Store.GetGameStates(actor.Id).Single().InLibrary);Assert.False(File.Exists(path));
        home.Store.RecoverProductJobs(["game-uninstall"]);var retry=home.Store.ClaimProductJob(["game-uninstall"])!;Assert.Equal(job.Id,retry.Id);
        await Removal(home,roms).RunAsync(retry,CancellationToken.None);Assert.Equal("completed",home.Store.GetProductJob(job.Id)!.Status);Assert.False(home.Store.GetGameStates(actor.Id).Single().InLibrary);
        Assert.Equal(rom.Id,(await Import(roms,actor,login)).Id);
    }
    [Fact]
    public void PinnedSoftwareProfileUsesDeclaredBackendAndJitValuesAndDisablesNetworking()
    {
        Assert.Contains("ppsspp_cpu_core = \"JIT\"",NativeCoreConfiguration.Options("ppsspp"));var required=NativeCoreConfiguration.RequiredOptions("ppsspp",pspSoftware:true);Assert.Empty(NativeCoreConfiguration.RequiredOptions("ppsspp"));
        Assert.Contains("ppsspp_backend = \"none\"",required);Assert.Contains("ppsspp_software_rendering = \"enabled\"",required);
        Assert.Contains("ppsspp_internal_resolution = \"480x272\"",required);Assert.Contains("ppsspp_enable_wlan = \"disabled\"",required);
        Assert.Contains("ppsspp_enable_builtin_pro_ad_hoc_server = \"disabled\"",required);Assert.DoesNotContain("rendering_mode",required);
        Assert.True(NativeBasicSelection.Includes("psp"));Assert.Equal(new[]{"psp"},NativeBasicSelection.CoreSystems("ppsspp"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongDirectoryFlagsOrBareStringsCannotReclassifyAPs1Iso(bool bare)
    {
        var bytes=PspIso();if(bare){bytes.AsSpan(16*2048,2048).Clear();"PSP_GAME UMD_DATA.BIN"u8.CopyTo(bytes.AsSpan(80));}else bytes[70*2048+25]=0;
        using var home=new TestWorkspace();var path=home.Write("ORIGINAL.iso",bytes);Assert.Null(await NativeRomLibrary.IdentifyIsoAsync(path,CancellationToken.None));
    }
    [Fact]
    public void PspPackageRefusesMissingOrMismatchedProofAndOutOfPackageAssetPaths()
    {
        using var home=new TestWorkspace();var root=AppFiles.DirectoryIn(home.Root,"isolated-psp-package");var assets=AppFiles.DirectoryIn(root,"assets");
        var catalog=new RetroArchCoreCatalog(new GameStoreTests.EnvironmentStub(AppContext.BaseDirectory));var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var binary=Write("ppsspp_libretro.so","ORIGINAL NONEXECUTABLE FIXTURE"u8.ToArray());var info=Write("ppsspp_libretro.info",System.Text.Encoding.UTF8.GetBytes(catalog.Require("ppsspp").Info));var atlas=Write("assets/ppge_atlas.zim",[1]);
        var manifest=new NativePspManifest(1,"linux-x64","1.20.4",NativePspRuntime.Commit,"libretro-software-native-1x",binary,info,[atlas],MemoryAdapter:"linux-anonymous-memfd-v1");
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest,json));var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Home:Games:PspRuntimePath",root}}).Build();var runtime=new NativePspRuntime(config,catalog);
        var file=new NativeRuntimeFile("NEVER-EXECUTED",1,new string('a',64));var host=new NativeRuntimeProfile(2,true,"","",file,file,file,file,[],true,"linux-x64",true);
        Assert.NotNull(runtime.Installed());Assert.Contains("henüz tamamlanmadı",runtime.BlockReason(host));
        var proof=Write("proof.json",JsonSerializer.SerializeToUtf8Bytes(new{coreSha256=new string('f',64),frontendSha256=file.Sha256,softwareFrames=true,inputVerified=true,saveRestore=true,h264Opus=true,audioSignal=true,error=(string?)null},json));
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest with{Proof=proof},json));Assert.NotNull(runtime.BlockReason(host));
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest with{Assets=[atlas,atlas with{Path="../ORIGINAL.iso"}]},json));Assert.Throws<DomainException>(()=>runtime.Read());
        NativeBundleAsset Write(string name,byte[] bytes){var path=Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar));File.WriteAllBytes(path,bytes);return new(name,bytes.Length,Convert.ToHexStringLower(SHA256.HashData(bytes)));}
    }
    [Fact]
    public void OnlyBundledLinuxPspProfileLocksUnverifiedRendererResolutionAndNetworkingOptions()
    {
        using var home=new TestWorkspace();var bundled=home.Paths with{NativeRuntimePath=Path.Combine(home.Root,"isolated-bundle")};
        var options=new[]{new NativeCoreOption(0,"ppsspp_backend","Backend","","none",["none","opengl"]),new NativeCoreOption(1,"ppsspp_enable_wlan","Network","","disabled",["disabled","enabled"])};
        Assert.All(new NativeCoreOptions(bundled).Decorate(options),option=>Assert.True(option.Managed));Assert.All(new NativeCoreOptions(home.Paths).Decorate(options),option=>Assert.False(option.Managed));
        var rom=new GameRom("rom-isolated-psp","1","psp","ORIGINAL.iso","Original",new string('a',64),128,"");var hash=new string('b',64);var settings=new NativeGameSettings(bundled);
        settings.Observe("1","ppsspp",hash,options,[]);Assert.All(settings.View(rom,"ppsspp","PPSSPP",hash).Options,option=>Assert.True(option.Managed));
        Assert.Throws<DomainException>(()=>settings.Save(rom,"ppsspp","PPSSPP",hash,new("game",0,new(){{"ppsspp_backend","opengl"}})));
    }
}
