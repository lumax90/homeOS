using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class HeadlessLanTests
{
    [Fact]
    public async Task RestartCleansOnlyMarkedFinishedWorkAndKeepsPendingOrLegacyFiles()
    {
        using var home=new TestWorkspace();var root=AppFiles.DirectoryIn(home.Paths.State,"games","native-work");var finished=AppFiles.DirectoryIn(root,Guid.NewGuid().ToString("N"));File.WriteAllText(Path.Combine(finished,".home-native-work"),"Home.Native.Work.v1");var content=Path.Combine(finished,"game.gba");File.WriteAllBytes(content,[1,2]);File.SetAttributes(content,FileAttributes.ReadOnly);
        var legacy=AppFiles.DirectoryIn(root,Guid.NewGuid().ToString("N"));File.WriteAllText(Path.Combine(legacy,"keep.txt"),"Existing unmarked work");
        var pending=AppFiles.DirectoryIn(root,Guid.NewGuid().ToString("N"));File.WriteAllText(Path.Combine(pending,".home-native-work"),"Home.Native.Work.v1");File.WriteAllText(Path.Combine(pending,"active-checkpoint.json"),JsonSerializer.Serialize(new NativeClosingCheckpoint("missing-owner","rom-fixture","native-fixture",0,false)));
        using var library=new NativeRomLibrary(home.Paths,home.Store);using var manager=new NativeGameManager(home.Store,home.Paths,library,new(home.Paths),new(),NullLogger<NativeGameManager>.Instance);await manager.StartAsync(CancellationToken.None);try{Assert.False(Directory.Exists(finished));Assert.True(File.Exists(Path.Combine(legacy,"keep.txt")));Assert.True(File.Exists(Path.Combine(pending,"active-checkpoint.json")));}finally{await manager.StopAsync(CancellationToken.None);}
    }
    [Fact]
    public void LanIcePublishesOnlyMediaAndKeepsCommandsApiAndSourceOnLoopback()
    {
        var network=new NativeStreamNetwork(new ConfigurationBuilder().Build());var hosts=network.Hosts("192.168.1.30");
        using var data=JsonDocument.Parse(MediaMtxGameConfiguration.Create("game-fixture",new string('a',64),62402,62403,62404,62405,hosts));var config=data.RootElement;
        Assert.Equal("0.0.0.0:62405",config.GetProperty("webrtcLocalUDPAddress").GetString());Assert.Equal("0.0.0.0:62405",config.GetProperty("webrtcLocalTCPAddress").GetString());Assert.Equal("127.0.0.1:62403",config.GetProperty("webrtcAddress").GetString());Assert.Equal("127.0.0.1:62404",config.GetProperty("apiAddress").GetString());Assert.Equal("udp+mpegts://127.0.0.1:62402",config.GetProperty("paths").GetProperty("game-fixture").GetProperty("source").GetString());
        Assert.Contains("192.168.1.30",config.GetProperty("webrtcAdditionalHosts").EnumerateArray().Select(x=>x.GetString()));Assert.Empty(config.GetProperty("webrtcICEServers2").EnumerateArray());Assert.False(config.GetProperty("webrtcIPsFromInterfaces").GetBoolean());
        var user=Assert.Single(config.GetProperty("authInternalUsers").EnumerateArray());Assert.Equal("127.0.0.1",Assert.Single(user.GetProperty("ips").EnumerateArray()).GetString());
    }
    [Theory]
    [InlineData("http://192.168.1.30")]
    [InlineData("192.168.1.30:62405")]
    [InlineData("host\nanything")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("bad/host")]
    public void AdvertisedHostCannotBeAUrlWildcardOrConfigurationInjection(string host)
    {Assert.Throws<DomainException>(()=>new NativeStreamNetwork(new ConfigurationBuilder().Build()).Hosts(host));}
    [Fact]
    public void ExplicitNatAddressOverridesTheProxyHostWithoutChangingPrivateInput()
    {
        var settings=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Home:Gaming:PublicHost","192.168.20.3"}}).Build();Assert.Equal(new[]{"192.168.20.3","127.0.0.1"},new NativeStreamNetwork(settings).Hosts("home-container"));Assert.Equal(new[]{"127.0.0.1"},new NativeStreamNetwork(new ConfigurationBuilder().Build()).Hosts("localhost"));
    }
    [Fact]
    public async Task BundledProfileMovesWithTheImageAndRemoteToggleDoesNotRewriteExistingWindowsState()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Owner","fixture"));var user=home.Store.GetAccount("1")!;var paths=Bundle(home);var profile=NativeRuntimeProfile.Read(paths)!;var version=profile.Version;
        var legacy=Path.Combine(AppFiles.DirectoryIn(paths.State,"games"),"native-runtime-profile.json");File.WriteAllText(legacy,"EXISTING WINDOWS PROFILE MUST BE PRESERVED");
        using var library=new NativeRomLibrary(paths,home.Store);using var manager=new NativeGameManager(home.Store,paths,library,new(paths),new(),NullLogger<NativeGameManager>.Instance);var setup=new NativeRuntimeSetup(paths,manager,new FileVerifier(),new());
        var status=setup.Status(user,false);Assert.True(status.Available);Assert.True(status.CanConfigure);Assert.False(status.LocalOnly);Assert.False(status.ClientLocal);Assert.Equal("linux-x64",status.Platform);
        await setup.ConfigureAsync(new(false,ExpectedVersion:version),user,false,CancellationToken.None);Assert.False(NativeRuntimeProfile.Read(paths)!.Enabled);Assert.Equal("EXISTING WINDOWS PROFILE MUST BE PRESERVED",File.ReadAllText(legacy));
        var backupService=new HomeServer.Operations.BackupService(paths,home.Store);var backup=await backupService.CreateAsync(Guid.NewGuid().ToString("N"),CancellationToken.None);await using(var input=File.OpenRead(backupService.FilePath(backup.Id))){var restored=await backupService.PrepareRestoreAsync(input,CancellationToken.None);Assert.False(NativeRuntimeBundle.Settings(paths with{StatePath=Path.Combine(paths.Data,"states",restored.Id)}).Enabled);}
        var relocated=Path.Combine(home.Root,"relocated-runtime");Directory.Move(paths.NativeRuntimePath!,relocated);var moved=paths with{NativeRuntimePath=relocated};Assert.Equal(version,NativeRuntimeProfile.Read(moved)!.Version);Assert.False(NativeRuntimeProfile.Read(moved)!.Enabled);Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Fact]
    public void BundledNativeEvidenceIsBoundToActualSoftwareHashesAndThePlatform()
    {
        using var home=new TestWorkspace();var paths=Bundle(home);var profile=NativeRuntimeProfile.Read(paths)!;NativeRuntimeProfile.ValidateDefinition(profile);Assert.Equal(".so",profile.CoreExtension);
        Assert.Throws<DomainException>(()=>NativeRuntimeBundle.RequireProof(profile with{RetroArch=profile.RetroArch with{Sha256=new string('f',64)}},File.ReadAllText(profile.Proof!.Path)));
        var definition=NativeRuntimeBundle.Read(paths)!;var escaping=definition with{Frontend=definition.Frontend with{Path="../outside.exe"}};File.WriteAllText(Path.Combine(paths.NativeRuntimePath!,"manifest.json"),JsonSerializer.Serialize(escaping,new JsonSerializerOptions(JsonSerializerDefaults.Web)));Assert.Throws<DomainException>(()=>NativeRuntimeProfile.Read(paths));Assert.True(File.Exists(profile.Core.Path));
    }
    [Fact]
    public void LinuxInstallerRejectsExecutablesWrongArchitectureAndTruncatedElfFiles()
    {
        using var home=new TestWorkspace();var path=home.Write("library.so",new byte[512]);var bytes=File.ReadAllBytes(path);bytes[0]=0x7f;bytes[1]=69;bytes[2]=76;bytes[3]=70;bytes[4]=2;bytes[5]=1;bytes[16]=3;bytes[18]=62;File.WriteAllBytes(path,bytes);NativeCoreInstaller.RequireLinuxLibrary(path);
        bytes[18]=183;File.WriteAllBytes(path,bytes);Assert.Throws<DomainException>(()=>NativeCoreInstaller.RequireLinuxLibrary(path));bytes[18]=62;bytes[16]=2;File.WriteAllBytes(path,bytes);Assert.Throws<DomainException>(()=>NativeCoreInstaller.RequireLinuxLibrary(path));File.WriteAllBytes(path,bytes[..63]);Assert.Throws<DomainException>(()=>NativeCoreInstaller.RequireLinuxLibrary(path));
    }
    [Fact]
    public async Task NonLoopbackOwnerUsesNormalCsrfAndSessionRulesWithoutALocalBrowser()
    {
        using var home=new TestWorkspace();await using var factory=new Factory(home);using var client=factory.CreateClient();await Session(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner","isolated-lan-password"))).EnsureSuccessStatusCode();await Session(client);
        var status=await client.GetFromJsonAsync<NativeSetupStatus>("/api/games/native/status");Assert.False(status!.ClientLocal);Assert.False(status.LocalOnly);
        (await client.PutAsJsonAsync("/api/games/native/options",new NativeEmulationOptions(Quality:"low"))).EnsureSuccessStatusCode();Assert.Equal("low",NativeEmulationSettings.Read(home.Paths).Quality);
        client.DefaultRequestHeaders.Remove("X-Home-User");Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync("/api/games/native/options",new NativeEmulationOptions(1,"quality"))).StatusCode);await Session(client);client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/games/native/options",new NativeEmulationOptions(1,"quality"))).StatusCode);await Session(client);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/games/native/setup",new NativeSetupCommand(true))).StatusCode); // Missing runtime, rather than a local-client restriction.
        using var anonymous=factory.CreateClient();using var offer=new HttpRequestMessage(HttpMethod.Post,"/game-native/"+new string('a',32)+"/whep");offer.Headers.Authorization=new("Bearer",new string('a',64));Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.SendAsync(offer)).StatusCode);Assert.Empty(home.Store.GetActiveGameSessions());
    }
    private static HomePaths Bundle(TestWorkspace home)
    {
        var root=AppFiles.DirectoryIn(home.Root,"SYNTHETIC-NONEXECUTABLE-BUNDLE");Directory.CreateDirectory(Path.Combine(root,"cores"));
        NativeBundleAsset Asset(string path){var file=Path.Combine(root,path);File.WriteAllBytes(file,[1,2,3]);return new(path,3,StateFolders.Hash(file));}
        var frontend=Asset("retroarch");var media=Asset("mediamtx");var core=Asset("cores/mgba_libretro.so");var info=Asset("cores/mgba_libretro.info");
        var proof=new{format=1,platform="linux-x64",frontendSha256=frontend.Sha256,coreSha256=core.Sha256,mediamtxSha256=media.Sha256,error=(string?)null,heldFrames=12,saveRestore=true,h264Opus=true,loopbackControl=true,headless=true,syntheticUnitProof=true};var file=Path.Combine(root,"proof.json");File.WriteAllText(file,JsonSerializer.Serialize(proof));
        var definition=new NativeBundleManifest(1,"linux-x64","1.22.2","69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576","cfb1204314dd6b061a7b1cda77221735939bf5ba","home-host-1","1.21.2",frontend,media,[new("mgba","0.10.5","26b7884bc25a5933960f3cdcd98bac1ae14d42e2","MPL-2.0",core,info,"https://fixture.invalid/source")],new("proof.json",new FileInfo(file).Length,StateFolders.Hash(file)));
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(definition,new JsonSerializerOptions(JsonSerializerDefaults.Web)));return home.Paths with{NativeRuntimePath=root};
    }
    private sealed class FileVerifier:INativeRuntimeVerifier
    {public void Verify(NativeRuntimeProfile profile){NativeRuntimeProfile.ValidateDefinition(profile);foreach(var file in new[]{profile.Core,profile.CoreInfo,profile.MediaMtx,profile.RetroArch})VerifiedFilePublication.Require(file.Path,file.Bytes,file.Sha256);}}
    private static async Task Session(HttpClient client){var info=await client.GetFromJsonAsync<JsonElement>("/api/session");foreach(var header in new[]{"X-CSRF-TOKEN","X-Home-User","X-Home-Session"})client.DefaultRequestHeaders.Remove(header);client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",info.GetProperty("csrfToken").GetString());if(info.GetProperty("authenticated").GetBoolean()){client.DefaultRequestHeaders.Add("X-Home-User",info.GetProperty("userId").GetString());client.DefaultRequestHeaders.Add("X-Home-Session",info.GetProperty("sessionId").GetString());}}
    private sealed class Factory(TestWorkspace home):WebApplicationFactory<Program>
    {protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"Home:DataPath",home.Paths.Data},{"Home:FfmpegPath","absent"},{"Home:FfprobePath","absent"},{"Home:Downloads:LoopbackOnly","true"}}));builder.ConfigureServices(services=>services.AddSingleton<IStartupFilter,RemotePeer>());}}
    private sealed class RemotePeer:IStartupFilter
    {public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use((context,continuation)=>{context.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.40");context.Connection.LocalIpAddress=IPAddress.Parse("192.0.2.10");return continuation(context);});next(app);};}
}
