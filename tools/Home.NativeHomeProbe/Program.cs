using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using Home.NativeTests;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

if(args.Length is not(10 or 14)||args[0]!="--review-root"||args[2]!="--core-build"||args[4]!="--test-root"||args[6]!="--frontend-build"||args[8]!="--input-proof"||args.Length==14&&(args[10]!="--gpu-profile"||args[12]!="--legacy-candidate"))throw new ArgumentException("Use --review-root <cache> --core-build <receipt> --test-root <isolated root> --frontend-build <receipt> --input-proof <actual native probe report> [--gpu-profile <proven host> --legacy-candidate <retained original profile>]");
var root=Path.GetFullPath(args[1]);var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
var retro=JsonSerializer.Deserialize<Review>(await File.ReadAllTextAsync(Path.Combine(root,"retroarch","review.json")),json)!;
var mtx=JsonSerializer.Deserialize<Review>(await File.ReadAllTextAsync(Path.Combine(root,"mediamtx","review.json")),json)!;
var core=JsonSerializer.Deserialize<CoreBuild>(await File.ReadAllTextAsync(args[3]),json)!;
NativeRuntimeFile Asset(string id,Reviewed file)=>new(Path.Combine(root,id,"files",file.Path),file.Bytes,file.Sha256);
var profile=new NativeRuntimeProfile(1,true,core.SourceVersion,core.SourceCommit,Asset("retroarch",retro.Files.Single(value=>Path.GetFileName(value.Path)=="retroarch.exe")),new(core.CorePath,core.CoreBytes,core.CoreSha256),Asset("retroarch",retro.Files.Single(value=>Path.GetFileName(value.Path)=="mgba_libretro.info")),Asset("mediamtx",mtx.Files.Single(value=>Path.GetFileName(value.Path)=="mediamtx.exe")),retro.Files.Select(file=>Asset("retroarch",file)).ToArray());
if(args.Length>=10)
{
    var frontend=JsonSerializer.Deserialize<FrontendBuild>(await File.ReadAllTextAsync(args[7]),json)??throw new InvalidDataException("Frontend build required.");
    if(frontend.SourceVersion!="1.22.2"||frontend.SourceCommit!="69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576"||frontend.UpstreamInputFix!="cfb1204314dd6b061a7b1cda77221735939bf5ba"||!frontend.PatchedPrototype||!Path.GetFullPath(frontend.ExecutablePath).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Exact isolated upstream frontend build required.");
    using var proof=JsonDocument.Parse(await File.ReadAllTextAsync(args[9]));var document=proof.RootElement;
    if(document.GetProperty("error").ValueKind!=JsonValueKind.Null||document.GetProperty("frontendSha256").GetString()!=frontend.ExecutableSha256||document.GetProperty("coreSha256").GetString()!=core.CoreSha256)throw new InvalidDataException("Proof must match these exact frontend/core binaries.");
    var streaming=document.GetProperty("streaming");
    if(!streaming.GetProperty("simultaneousHeldInputVerified").GetBoolean()||streaming.GetProperty("held").GetProperty("consecutiveHeldFrames").GetUInt32()<10||!streaming.GetProperty("nativeSaveRestoreVerified").GetBoolean()||!streaming.GetProperty("windowless").GetBoolean()||!streaming.GetProperty("privateInputGuard").GetBoolean()||streaming.GetProperty("whepStatus").GetInt32()!=201||!streaming.GetProperty("negotiatedH264Opus").GetBoolean())throw new InvalidDataException("Actual held/combined/core/save/transport proof required; source harness evidence is insufficient.");
    profile=profile with{RetroArch=new(frontend.ExecutablePath,frontend.ExecutableBytes,frontend.ExecutableSha256),Dependencies=frontend.Dependencies,ContinuousInputVerified=true};
}
NativeRuntimeCandidate? retained=null;
if(args.Length==14){retained=JsonSerializer.Deserialize<NativeRuntimeCandidate>(await File.ReadAllTextAsync(args[13]),json)??throw new InvalidDataException("Retained original runtime required.");VerifiedFilePublication.Require(retained.Proof.Path,retained.Proof.Bytes,retained.Proof.Sha256);NativeRuntimeCandidate.RequireProof(retained.Profile,await File.ReadAllTextAsync(retained.Proof.Path));if(retained.Profile.Core.Sha256!=core.CoreSha256)throw new InvalidDataException("Original released GBA core must be preserved.");profile=retained.Profile with{Enabled=true};}
profile.Verify();
var test=AppFiles.DirectoryIn(Path.GetFullPath(args[5]),"attempt-"+Guid.NewGuid().ToString("N"));var paths=new HomePaths(Path.Combine(test,"data"),Path.Combine(test,"thumbnails"),null);
var store=new MediaStore(paths);store.Initialize();store.CreateOwner(new("Synthetic isolated owner","not-a-runtime-credential"));var owner=store.GetAccount("1")!;var login=store.CreateLoginSession(owner.Id,store.GetAccountSecret(owner.Id)!.Version,"isolated native test");
var gameRoot=AppFiles.DirectoryIn(paths.State,"games");
var proofPath=retained?.Proof.Path??Path.GetFullPath(args[9]);
var candidate=new NativeRuntimeCandidate(1,"1.22.2","69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576","cfb1204314dd6b061a7b1cda77221735939bf5ba","1.21.2",profile with{Enabled=false},new(proofPath,new FileInfo(proofPath).Length,StateFolders.Hash(proofPath)));
await File.WriteAllTextAsync(Path.Combine(gameRoot,"native-runtime-candidate.json"),JsonSerializer.Serialize(candidate,json));
using var library=new NativeRomLibrary(paths,store);using var content=new MemoryStream(SyntheticGba.Create());var rom=await library.ImportAsync(content,"SYNTHETIC-ISOLATED-TEST.gba",content.Length,owner,login.Id,CancellationToken.None);
NativeGpuRuntime? gpu=null;NativeCoreRegistry? cores=null;NativeFirmwareLibrary? firmware=null;NativeCoreOptions? coreOptions=null;
if(args.Length==14){await File.WriteAllTextAsync(Path.Combine(gameRoot,"native-gpu-profile.json"),await File.ReadAllTextAsync(args[11]));gpu=new(paths);firmware=new(paths);coreOptions=new(paths);cores=new(paths,new RetroArchCoreCatalog(new NativeEnvironment()),firmware,gpu);}
var manager=new NativeGameManager(store,paths,library,new NativeSaveSnapshots(paths),new EventHub(),NullLogger<NativeGameManager>.Instance,cores,firmware,coreOptions);
var setup=new NativeRuntimeSetup(paths,manager,new NativeRuntimeVerifier(),new EventHub(),gpu);
var initial=setup.Status(owner,true);if(!initial.Prepared||initial.Enabled||initial.Available)throw new InvalidDataException("Candidate prepared itself as an enabled runtime.");
var enabled=await setup.ConfigureAsync(new(true,true,initial.Version),owner,true,CancellationToken.None);if(!enabled.Available)throw new InvalidDataException("Home setup did not enable the exact verified runtime.");
await manager.StartAsync(CancellationToken.None);string? error=null;object? result=null;
try
{
    using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(45));var token=budget.Token;var ticket=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    var launch=await manager.LaunchAsync(rom.Id,new(Guid.NewGuid().ToString("N"),ticket),owner,login.Id,token);
    try { await setup.ConfigureAsync(new(false,false,enabled.Version),owner,true,token);throw new InvalidDataException("Runtime changed while a native game was active."); }
    catch(DomainException failure)when(failure.StatusCode==409){}
    try { using var rejected=await manager.WhepAsync(launch.Id,new string('0',64),"OPTIONS",null,[],null,null,token);throw new InvalidDataException("Wrong native stream ticket accepted."); }
    catch(DomainException failure)when(failure.StatusCode==401){}
    try { using var rejected=await manager.WhepAsync(launch.Id,ticket,"DELETE",Guid.NewGuid().ToString(),[],null,null,token);throw new InvalidDataException("Foreign native stream reader accepted."); }
    catch(DomainException failure)when(failure.StatusCode==404){}
    using var negotiated=await manager.WhepAsync(launch.Id,ticket,"POST",null,Encoding.UTF8.GetBytes(SyntheticWhep.Offer()),"application/sdp",null,token);
    var answer=await negotiated.Content.ReadAsStringAsync(token);var location=negotiated.Headers.Location;
    if((int)negotiated.StatusCode!=201||location is null||location.IsAbsoluteUri||!location.ToString().StartsWith("/game-native/"+launch.Id+"/whep/",StringComparison.Ordinal)||!answer.Contains("H264",StringComparison.Ordinal)||!answer.Contains("opus",StringComparison.Ordinal))throw new InvalidDataException("Home's authenticated native stream proxy did not negotiate or scope its reader URL.");
    var reader=location.ToString().Split('/').Last();using var removed=await manager.WhepAsync(launch.Id,ticket,"DELETE",reader,[],null,null,token);
    if(!removed.IsSuccessStatusCode)throw new InvalidDataException("Home's stream proxy did not close its reader.");
    var running=await manager.CommandAsync(launch.Id,"resume",owner,login.Id,token);
    var repeated=await manager.LaunchAsync(rom.Id,new(launch.Id,ticket),owner,login.Id,token);
    if(repeated.Id!=launch.Id||repeated.InputEpoch!=running.InputEpoch||store.GetActiveGameSessions().Count!=1)throw new InvalidDataException("Idempotent Home launch lost its current input epoch or opened another session.");
    // Original ARM fixture only: observe actual KEYINPUT while the background Windows policy monitor is active.
    await Task.Delay(1200,token);long inputSequence=0;var inputLatencyMilliseconds=new List<double>();
    for(var sample=0;sample<3;sample++)
    {
        var clock=Stopwatch.StartNew();manager.Input(launch.Id,new(++inputSequence,1<<8,running.InputEpoch),owner,login.Id);
        while((await ReadFixtureKeys(token)&1)!=0){if(clock.Elapsed>TimeSpan.FromMilliseconds(500))throw new InvalidDataException("Native input was blocked beyond the 500ms isolated regression budget.");await Task.Delay(10,token);}
        inputLatencyMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
        manager.Input(launch.Id,new(++inputSequence,0,running.InputEpoch),owner,login.Id);
        var releaseBudget=Stopwatch.StartNew();while((await ReadFixtureKeys(token)&1)==0){if(releaseBudget.Elapsed>TimeSpan.FromMilliseconds(500))throw new InvalidDataException("Native release was blocked beyond its isolated regression budget.");await Task.Delay(10,token);}
        await Task.Delay(150,token);
    }
    var saved=await manager.CommandAsync(launch.Id,"save",owner,login.Id,token);
    if(gpu is not null){var actual=await manager.GetOptionsAsync(launch.Id,owner,login.Id,token);if(actual.Length==0)throw new InvalidDataException("Home did not expose actual mGBA options.");var selected=actual.First(option=>!option.Managed&&!NativeCoreOptions.NeedsRestart(option)&&option.Values.Length>1);var changed=await manager.SetOptionAsync(launch.Id,new(selected.Key,selected.Values.First(value=>value!=selected.Value)),owner,login.Id,token);if(changed.First(option=>option.Key==selected.Key).Value==selected.Value)throw new InvalidDataException("Home did not persist the actual core option.");if((await manager.ControllersAsync(launch.Id,owner,login.Id,token)).Length!=4)throw new InvalidDataException("Home did not expose all controller ports.");}
    var checkpoint=store.GetGameCheckpoint(rom.Id,profile.Version,owner.Id);if(!saved.HasSave||!checkpoint.Files.ContainsKey("native-save.json"))throw new InvalidDataException("Native binary snapshot receipt was not committed.");
    await manager.CommandAsync(launch.Id,"end",owner,login.Id,token);if(store.GetActiveGameSessions().Count!=0)throw new InvalidDataException("Native session remained open.");
    if(!(await manager.CommandAsync(launch.Id,"end",owner,login.Id,token)).HasSave)throw new InvalidDataException("A lost exit response could not be retried idempotently.");
    var second=await manager.LaunchAsync(rom.Id,new(Guid.NewGuid().ToString("N"),Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))),owner,login.Id,token);if(!second.HasSave)throw new InvalidDataException("Committed native snapshot was not restored.");
    await manager.CommandAsync(second.Id,"discard",owner,login.Id,token);
    var disabled=await setup.ConfigureAsync(new(false,false,enabled.Version),owner,true,token);if(disabled.Enabled||disabled.Available||!disabled.Prepared)throw new InvalidDataException("Home setup did not safely disable the idle runtime.");
    result=new{launch,modernHost=gpu is not null,originalSaveVersionPreserved=launch.Version==profile.Version,setupEnabledOnlyOnCommand=true,setupChangeDuringGameplayRejected=true,setupDisabledAfterExit=true,inputLatencyMilliseconds,proxyWhepStatus=(int)negotiated.StatusCode,readerScopedToHome=true,foreignReaderAndTicketRejected=true,readerClosed=true,idempotentInputEpoch=repeated.InputEpoch,saveRevision=checkpoint.Revision,nativeSnapshotRestored=second.HasSave,activeSessions=store.GetActiveGameSessions().Count,normalUserLibraryTouched=false,syntheticIsolatedTest=true,browserDecodeVerified=false};
}
catch(Exception failure){error=failure.ToString();Environment.ExitCode=1;}
finally{await manager.StopAsync(CancellationToken.None);manager.Dispose();firmware?.Dispose();}
await File.WriteAllTextAsync(Path.Combine(test,"home-native-smoke.json"),JsonSerializer.Serialize(new{testedAt=DateTimeOffset.UtcNow,test,result,error},json));
Console.WriteLine(JsonSerializer.Serialize(new{test,result,error},json));
async Task<ushort> ReadFixtureKeys(CancellationToken token)
{
    using var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));socket.Connect(IPAddress.Loopback,62406);
    await socket.SendAsync(Encoding.ASCII.GetBytes("READ_CORE_MEMORY 2000000 2\n"),token);
    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(2));
    var fields=Encoding.ASCII.GetString((await socket.ReceiveAsync(deadline.Token)).Buffer).Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
    if(fields.Length!=4||fields[0]!="READ_CORE_MEMORY"||fields[1]!="2000000")throw new InvalidDataException("Unexpected original fixture key response.");
    return BinaryPrimitives.ReadUInt16LittleEndian(fields.Skip(2).Select(field=>Convert.ToByte(field,16)).ToArray());
}
record Reviewed(string Path,long Bytes,string Sha256);record Review(Reviewed[] Files);record CoreBuild(string SourceVersion,string SourceCommit,string CorePath,string CoreSha256,long CoreBytes);
record FrontendBuild(string SourceVersion,string SourceCommit,string UpstreamInputFix,bool PatchedPrototype,string ExecutablePath,long ExecutableBytes,string ExecutableSha256,NativeRuntimeFile[] Dependencies);
sealed class NativeEnvironment:IWebHostEnvironment
{
    public string ApplicationName{get;set;}="Home.NativeHomeProbe";public string EnvironmentName{get;set;}="Testing";
    public string ContentRootPath{get;set;}=Path.GetFullPath("server/HomeServer.Api");public string WebRootPath{get;set;}="";
    public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
