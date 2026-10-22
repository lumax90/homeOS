using System.Buffers.Binary;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

// Original isolated data fixtures only: no user ROM, game launch, provider request or browser automation.
public sealed class NativeMultiSystemTests
{
    private static (Account Actor,string Login) Owner(TestWorkspace home)
    {home.Store.CreateOwner(new("Owner","isolated-hash"));var owner=home.Store.GetAccount("1")!;return(owner,home.Store.CreateLoginSession(owner.Id,home.Store.GetAccountSecret(owner.Id)!.Version,"multisystem-fixture").Id);}
    private static RetroArchCoreCatalog Catalog(TestWorkspace home)=>new(new GameStoreTests.EnvironmentStub(home.Root));
    private static byte[] Gba(){var bytes=new byte[512];bytes[3]=0xea;bytes[0xb2]=0x96;var sum=0x19;for(var i=0xa0;i<=0xbc;i++)sum+=bytes[i];bytes[0xbd]=(byte)-sum;return bytes;}
    [Fact]
    public async Task GbaIdentityAndCorePartitionSurviveSchemaFortyOneUpgrade()
    {
        using var home=new TestWorkspace();var data=AppFiles.DirectoryIn(home.Root,"actual-schema41");var paths=new HomePaths(data,Path.Combine(data,"thumbnails"),null);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(data,"home.db")}"))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA foreign_keys=OFF";command.ExecuteNonQuery();
            foreach(var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name=>name.Contains(".Migrations.")&&int.TryParse(name.Split(".Migrations.")[1][..3],out var number)&&number<=41).Order())
            {using var stream=typeof(MediaStore).Assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);command.CommandText=reader.ReadToEnd();command.ExecuteNonQuery();if(name.EndsWith("001_initial.sql",StringComparison.Ordinal)){command.CommandText="INSERT INTO owner VALUES(1,'Owner','preserved')";command.ExecuteNonQuery();}}
            command.CommandText="PRAGMA user_version=41";command.ExecuteNonQuery();
        }
        var store=new MediaStore(paths);var actor=store.GetAccount("1")!;var login=store.CreateLoginSession("1",store.GetAccountSecret("1")!.Version,"isolated-migration").Id;
        using var roms=new NativeRomLibrary(paths,store);var bytes=Gba();var rom=await roms.ImportAsync(new MemoryStream(bytes),"Original.gba",bytes.Length,actor,login,CancellationToken.None);store.SetGamePreference(rom.Id,new(true,true),actor);
        store.Initialize();store.Initialize();Assert.Equal(rom,Assert.Single(store.GetGameRoms(actor)));Assert.True(Assert.Single(store.GetGameStates(actor.Id)).Favorite);
        using var content=roms.OpenVerified(rom);Assert.Equal(bytes.Length,content.Length);
        using var firmware=new NativeFirmwareLibrary(paths);var registry=new NativeCoreRegistry(paths,Catalog(home),firmware);Assert.Equal("mgba",registry.Selection(rom).CoreId);
        using var verified=new SqliteConnection($"Data Source={Path.Combine(paths.State,"home.db")}");verified.Open();using var check=verified.CreateCommand();check.CommandText="PRAGMA user_version";Assert.Equal((long)MediaStore.LatestSchema,check.ExecuteScalar());
    }
    [Theory]
    [InlineData("nes","Original.nes")]
    [InlineData("snes","Original.sfc")]
    [InlineData("gb","Original.gb")]
    [InlineData("megadrive","Original.md")]
    [InlineData("ps1","Original.chd")]
    public async Task NewSystemsPublishPrivateVerifiedContentWithoutLaunchingAnything(string system,string name)
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var bytes=new byte[512];if(system=="nes")"NES\x1a"u8.CopyTo(bytes);if(system=="ps1")"MComprHD"u8.CopyTo(bytes);
        var rom=await roms.ImportAsync(new MemoryStream(bytes),name,bytes.Length,actor,login,CancellationToken.None,system);var retry=await roms.ImportAsync(new MemoryStream(bytes),name,bytes.Length,actor,login,CancellationToken.None,system);
        Assert.Equal(rom,retry);Assert.Equal(system,rom.System);using var content=roms.OpenVerified(rom);Assert.Equal(bytes.Length,content.Length);Assert.Empty(home.Store.GetActiveGameSessions());Assert.Empty(home.Store.GetProductJobs(actor));
    }
    [Theory]
    [InlineData("ps1","game.exe")]
    [InlineData("gba","game.iso")]
    [InlineData("ps1","../game.chd")]
    [InlineData("snes","CON.sfc")]
    public void SystemAndExecutablePathBoundariesAreExplicit(string system,string name)=>Assert.Throws<DomainException>(()=>NativeSystems.ValidateName(system,name));
    [Fact]
    public void FullRetroPadUsesExistingUpstreamAbiWithoutWeakeningGbaMask()
    {
        var packet=RetroPadProtocol.Button(15,true,ushort.MaxValue);Assert.Equal(1,BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4)));Assert.Equal(15,BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12)));Assert.Equal(16,RetroPadProtocol.ReleaseAll(ushort.MaxValue).Count());
        Assert.Throws<DomainException>(()=>RetroPadProtocol.Button(15,true));Assert.Equal(10,RetroPadProtocol.ReleaseAll().Count());
        var input=new NativeInputBuffer();input.Stage(1<<15,ushort.MaxValue);input.Stage(0,ushort.MaxValue);Assert.Equal(1<<15,input.Next(false));Assert.Equal(0,input.Next(false));
    }
    [Fact]
    public void OfficialCatalogDoesNotPromoteGpuOrSpecialSaveCoresToPlayable()
    {
        using var home=new TestWorkspace();var catalog=Catalog(home);Assert.Equal(326,catalog.Data.Entries.Length);Assert.Equal(246,catalog.Data.Entries.Count(core=>core.Categories.Contains("Emulator")));
        Assert.NotNull(RetroArchCoreCatalog.AdapterBlock(catalog.Require("pcsx2")));Assert.NotNull(RetroArchCoreCatalog.AdapterBlock(catalog.Require("dolphin")));Assert.NotNull(RetroArchCoreCatalog.AdapterBlock(catalog.Require("xemu")));Assert.Null(RetroArchCoreCatalog.AdapterBlock(catalog.Require("pcsx_rearmed")));Assert.DoesNotContain(catalog.Data.Entries,core=>core.System=="switch");
        using var firmware=new NativeFirmwareLibrary(home.Paths);var registry=new NativeCoreRegistry(home.Paths,catalog,firmware);var rom=new GameRom("rom-isolated","1","ps1","fixture.chd","fixture",new string('a',64),512,"");Assert.NotNull(registry.BlockReason(rom,null));
    }
    [Fact]
    public async Task FlatCueAndTrackPackageIsIdempotentAndPreservesSourceText()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);var cue=Encoding.UTF8.GetBytes("FILE \"Original track.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");var track=new byte[2352];
        async Task<GameRom> Import(){using var body=new MultipartFormDataContent("isolated-boundary");body.Add(new ByteArrayContent(cue),"files","Original.cue");body.Add(new ByteArrayContent(track),"files","Original track.bin");using var stream=await body.ReadAsStreamAsync();return await roms.ImportDiscAsync(stream,"isolated-boundary","ps1","Original.cue",actor,login,CancellationToken.None);}
        var rom=await Import();Assert.Equal(rom,await Import());Assert.Equal(cue.Length+track.Length,rom.SizeBytes);var destination=AppFiles.DirectoryIn(home.Root,"isolated-disc");Assert.Equal("game.cue",await roms.StageContentAsync(rom,destination,CancellationToken.None));Assert.Equal(cue,File.ReadAllBytes(Path.Combine(destination,"game.cue")));Assert.Equal(track,File.ReadAllBytes(Path.Combine(destination,"Original track.bin")));Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Theory]
    [InlineData("FILE \"../outside.bin\" BINARY\n")]
    [InlineData("FILE\t\"C:/outside.bin\" BINARY\n")]
    [InlineData("FILE \"inside.bin\" BINARY\nFILE\t\"../outside.bin\" BINARY\n")]
    [InlineData("CDTEXTFILE \"../outside.txt\"\nFILE \"inside.bin\" BINARY\n")]
    public async Task CueCannotReferenceFilesOutsideItsOwnedPackageIncludingWhitespaceVariants(string text)
    {
        using var home=new TestWorkspace();var root=AppFiles.DirectoryIn(home.Root,"disc-fixture");File.WriteAllText(Path.Combine(root,"entry.cue"),text);File.WriteAllBytes(Path.Combine(root,"inside.bin"),new byte[2352]);
        var manifest=new NativeDiscManifest(1,"ps1","entry.cue",[new("entry.cue",Encoding.UTF8.GetByteCount(text),new string('a',64)),new("inside.bin",2352,new string('b',64))]);Assert.Equal(422,(await Assert.ThrowsAsync<DomainException>(()=>NativeRomLibrary.ValidateDiscReferencesAsync(manifest,root,CancellationToken.None))).StatusCode);
    }
    [Fact]
    public async Task CanonicalNativeCueTracksFollowCueOrderWithoutOverwritingAnOriginalGameBin()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);using var roms=new NativeRomLibrary(home.Paths,home.Store);
        var cue="FILE \"Z first.bin\" BINARY\n TRACK 01 MODE1/2048\n INDEX 01 00:00:00\nFILE \"game.bin\" BINARY\n TRACK 02 AUDIO\n INDEX 01 00:00:00\n";var first=Enumerable.Repeat((byte)1,2048).ToArray();var second=Enumerable.Repeat((byte)2,2352).ToArray();
        using var body=new MultipartFormDataContent("original-cue");body.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(cue)),"files","Original.cue");body.Add(new ByteArrayContent(second),"files","game.bin");body.Add(new ByteArrayContent(first),"files","Z first.bin");using var input=await body.ReadAsStreamAsync();var rom=await roms.ImportDiscAsync(input,"original-cue","ps1","Original.cue",actor,login,CancellationToken.None);
        var output=AppFiles.DirectoryIn(home.Root,"canonical-cue");await roms.StageContentAsync(rom,output,CancellationToken.None,canonicalizeCueTracks:true);Assert.Equal(first,File.ReadAllBytes(Path.Combine(output,"game.bin")));Assert.Equal(second,File.ReadAllBytes(Path.Combine(output,"game-track-002.bin")));Assert.Contains("FILE \"game-track-002.bin\"",File.ReadAllText(Path.Combine(output,"game.cue")));Assert.Equal(second,File.ReadAllBytes(Path.Combine(home.Paths.State,"games","roms","ps1",rom.Sha256,"game.bin")));
    }
    [Fact]
    public async Task BiosUploadsArePrivateChecksummedAndNeverOverwriteConflicts()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var core=Catalog(home).Require("pcsx_rearmed") with{Firmware=[new("fixture.bin","Original isolated fixture",false)],Notes=""};using var firmware=new NativeFirmwareLibrary(home.Paths);var bytes=new byte[]{1,2,3,4};
        var first=await firmware.ImportAsync(core,"fixture.bin",new MemoryStream(bytes),bytes.Length,actor,home.Store,login,CancellationToken.None);Assert.Equal(first,await firmware.ImportAsync(core,"fixture.bin",new MemoryStream(bytes),bytes.Length,actor,home.Store,login,CancellationToken.None));Assert.Empty(firmware.Files("2",core.Id));
        await Assert.ThrowsAsync<DomainException>(()=>firmware.ImportAsync(core,"fixture.bin",new MemoryStream(new byte[]{5,6}),2,actor,home.Store,login,CancellationToken.None));Assert.Equal(first,Assert.Single(firmware.Files(actor.Id,core.Id)));
        var bad=core with{Notes="fixture.bin (md5): "+new string('a',32)};await Assert.ThrowsAsync<DomainException>(()=>firmware.ImportAsync(bad,"fixture.bin",new MemoryStream(bytes),bytes.Length,actor,home.Store,login,CancellationToken.None));
        Assert.False(NativeFirmwareLibrary.IsDeclared(core,"../fixture.bin"));
    }
    [Fact]
    public void CoreSelectionIsRevisionBoundAndCannotSelectAnotherSystem()
    {
        using var home=new TestWorkspace();using var firmware=new NativeFirmwareLibrary(home.Paths);var registry=new NativeCoreRegistry(home.Paths,Catalog(home),firmware);var rom=new GameRom("rom-isolated","1","nes","Original.nes","Original",new string('a',64),512,"");
        var initial=registry.Selection(rom);Assert.Equal("fceumm",initial.CoreId);Assert.Equal(1,registry.Select(rom,new("nestopia",0)).Revision);Assert.Throws<DomainException>(()=>registry.Select(rom,new("fceumm",0)));Assert.Throws<DomainException>(()=>registry.Select(rom,new("pcsx2",1)));Assert.Equal("nestopia",registry.Selection(rom).CoreId);
    }
}
