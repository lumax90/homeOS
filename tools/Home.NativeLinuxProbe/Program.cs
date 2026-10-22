using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Home.NativeTests;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
if(args is ["--prepare-debug",var debugRoot])
{
    Directory.CreateDirectory(debugRoot);await File.WriteAllTextAsync(Path.Combine(debugRoot,"retroarch.cfg"),RetroArchHostConfiguration.CreatePrivateHost(debugRoot,62420,62406,null));await File.WriteAllTextAsync(Path.Combine(debugRoot,"record.cfg"),RetroArchHostConfiguration.Recording+RetroArchHostConfiguration.LowDelayMux);await File.WriteAllTextAsync(Path.Combine(debugRoot,"core-options.cfg"),RetroArchHostConfiguration.CoreOptions);await File.WriteAllBytesAsync(Path.Combine(debugRoot,"game.gba"),SyntheticGba.Create(true));return;
}
if(args is ["--fixtures",var fixtureRoot])
{
    Directory.CreateDirectory(fixtureRoot);
    foreach(var system in NativeBasicSelection.Systems)
    {
        if(system=="ps1"){await File.WriteAllBytesAsync(Path.Combine(fixtureRoot,"ORIGINAL-ISOLATED-ps1.bin"),SyntheticPlayStationDisc.Create());await File.WriteAllTextAsync(Path.Combine(fixtureRoot,"ORIGINAL-ISOLATED-ps1.cue"),"FILE \"ORIGINAL-ISOLATED-ps1.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");continue;}
        var extension=system=="snes"?"sfc":system=="megadrive"?"md":system;await File.WriteAllBytesAsync(Path.Combine(fixtureRoot,"ORIGINAL-ISOLATED-"+system+"."+extension),system=="gba"?SyntheticGba.Create(true):SyntheticCartridges.Create(system));
    }
    await File.WriteAllTextAsync(Path.Combine(fixtureRoot,"README.txt"),"ORIGINAL SYNTHETIC NATIVE DIAGNOSTICS. Isolated automated tests only; no user or internet ROM/BIOS/media.\n");return;
}
if(args is not["--verify-build",var bundleRoot,var graphicsCore]||!OperatingSystem.IsLinux())throw new ArgumentException("Use --verify-build <isolated Linux runtime build> <original GPU fixture .so>");
bundleRoot=Path.GetFullPath(bundleRoot);var definition=JsonSerializer.Deserialize<NativeBundleManifest>(await File.ReadAllTextAsync(Path.Combine(bundleRoot,"build-manifest.json")),json)??throw new InvalidDataException("Build manifest required.");
var paths=new HomePaths("","",null,NativeRuntimePath:bundleRoot);var frontend=NativeRuntimeBundle.File(paths,definition.Frontend);var media=NativeRuntimeBundle.File(paths,definition.MediaMtx);var mgba=definition.Cores.Single(c=>c.Id=="mgba");var core=NativeRuntimeBundle.File(paths,mgba.Binary);
foreach(var asset in definition.Cores.SelectMany(c=>new[]{c.Binary,c.Info}).Concat([definition.Frontend,definition.MediaMtx])){var file=NativeRuntimeBundle.File(paths,asset);VerifiedFilePublication.Require(file.Path,file.Bytes,file.Sha256);}
using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(3));var token=budget.Token;var temporary=Path.Combine(Path.GetTempPath(),"home-native-linux-build-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
var software=await Probe(false,core.Path,Path.Combine(temporary,"software"));
var graphics=await Probe(true,Path.GetFullPath(graphicsCore),Path.Combine(temporary,"graphics"));
var proof=new{format=1,platform="linux-x64",frontendSha256=frontend.Sha256,coreSha256=core.Sha256,mediamtxSha256=media.Sha256,error=(string?)null,heldFrames=software.HeldFrames,saveRestore=software.Restored,h264Opus=software.Codecs,loopbackControl=software.Loopback&&graphics.Loopback,headless=true,headlessGraphics=graphics.Codecs&&graphics.Restored,software,graphics,verifiedAt=DateTimeOffset.UtcNow,originalIsolatedFixturesOnly=true,userRomLaunched=false};
var proofPath=Path.Combine(bundleRoot,"proof.json");await File.WriteAllTextAsync(proofPath,JsonSerializer.Serialize(proof,json),token);var descriptor=new NativeBundleAsset("proof.json",new FileInfo(proofPath).Length,StateFolders.Hash(proofPath));
await File.WriteAllTextAsync(Path.Combine(bundleRoot,"manifest.json"),JsonSerializer.Serialize(definition with{Proof=descriptor,HeadlessGraphics=true},json),token);
var validation=paths with{Data=Path.Combine(temporary,"settings")};Directory.CreateDirectory(validation.Data);NativeRuntimeBundle.Profile(validation)!.Verify();
Console.WriteLine(JsonSerializer.Serialize(proof,json));Directory.Delete(temporary,true);

async Task<ProofResult> Probe(bool hardware,string binary,string directory)
{
    Directory.CreateDirectory(directory);var renderer=hardware?"glcore":null;var config=RetroArchHostConfiguration.CreatePrivateHost(directory,62420,62406,renderer,400,480);
    var staged=Path.Combine(directory,"cores",hardware?"original_gpu_libretro.so":"mgba_libretro.so");File.Copy(binary,staged);binary=staged;
    if(hardware)await File.WriteAllTextAsync(Path.Combine(directory,"info","original_gpu_libretro.info"),"display_name = \"Original isolated GPU diagnostic\"\ncorename = \"Original diagnostic\"\nsystemname = \"Original diagnostic\"\nsystemid = \"diagnostic\"\nsupported_extensions = \"fixture\"\nsavestate = \"true\"\n",token);
    else File.Copy(Path.Combine(bundleRoot,"cores","mgba_libretro.info"),Path.Combine(directory,"info","mgba_libretro.info"));
    await File.WriteAllTextAsync(Path.Combine(directory,"retroarch.cfg"),config,token);await File.WriteAllTextAsync(Path.Combine(directory,"record.cfg"),NativeEmulationSettings.Recording(new(),hardware?400:240,hardware?480:160)+RetroArchHostConfiguration.LowDelayMux,token);
    await File.WriteAllTextAsync(Path.Combine(directory,"core-options.cfg"),RetroArchHostConfiguration.CoreOptions,token);
    var content=Path.Combine(directory,hardware?"game.fixture":"game.gba");await File.WriteAllBytesAsync(content,hardware?Encoding.ASCII.GetBytes("ORIGINAL ISOLATED GPU TEST"):SyntheticGba.Create(true),token);
    var secret=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));var mediaConfig=Path.Combine(directory,"mediamtx.yml");await File.WriteAllTextAsync(mediaConfig,MediaMtxGameConfiguration.Create("diagnostic",secret,62402,62403,62404,62405),token);
    Process? engine=null,mtx=null;NativeLinuxDisplay? display=null;var tails=new List<Task<string>>();
    try
    {
        if(!hardware){mtx=MediaProcess.Start(media.Path,[mediaConfig],workingDirectory:directory);tails.Add(MediaProcess.ReadErrorTailAsync(mtx.StandardOutput));tails.Add(MediaProcess.ReadErrorTailAsync(mtx.StandardError));}
        else display=await NativeLinuxDisplay.StartAsync(directory,400,480,token);
        var recording=hardware?Path.Combine(directory,"record.ts"):RetroArchHostConfiguration.LocalMpegTs(62402);
        engine=MediaProcess.Start(frontend.Path,["--verbose","--config",Path.Combine(directory,"retroarch.cfg"),"-L",binary,"--record",recording,"--recordconfig",Path.Combine(directory,"record.cfg"),content],workingDirectory:directory,environment:display?.Environment);
        tails.Add(MediaProcess.ReadErrorTailAsync(engine.StandardOutput));tails.Add(MediaProcess.ReadErrorTailAsync(engine.StandardError));
        await using var control=new RetroArchControl(engine,62406);
        for(var i=0;;i++){try{await control.StatusAsync(token);break;}catch(DomainException)when(i<15&&!engine.HasExited){await Task.Delay(100,token);}}
        await control.EnablePacedHeadlessModeAsync(token);await control.SetPausedAsync(false,token);
        var address=hardware?"1000":"2000000";var initial=await Memory(address,hardware?8:12);var initialFrames=Frame(initial,hardware);
        using var pad=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));pad.Connect(IPAddress.Loopback,62420);
        foreach(var packet in RetroPadProtocol.Changes(0,1<<8,ushort.MaxValue))await pad.SendAsync(packet,token);
        byte[] observed=[];var held=0;
        for(var i=0;i<60;i++){await Task.Delay(50,token);observed=await Memory(address,hardware?8:12);held=hardware?BinaryPrimitives.ReadInt16LittleEndian(observed.AsSpan(4))==1?(int)(Frame(observed,true)-initialFrames):0:BinaryPrimitives.ReadInt32LittleEndian(observed.AsSpan(8));if(held>=12)break;}
        if(held<10)throw new InvalidDataException("Actual loopback held input was not observed.");
        await control.SetPausedAsync(true,token);var saved=await Memory(address,hardware?8:12);await control.SaveStateAsync(token);var state=Path.Combine(directory,"saves","game.state");
        for(var i=0;i<80&&!File.Exists(state);i++)await Task.Delay(25,token);if(!File.Exists(state)||new FileInfo(state).Length==0)throw new InvalidDataException("Native save was not produced.");
        foreach(var packet in RetroPadProtocol.ReleaseAll(ushort.MaxValue))await pad.SendAsync(packet,token);await control.SetPausedAsync(false,token);await Task.Delay(450,token);await control.SetPausedAsync(true,token);var advanced=await Memory(address,hardware?8:12);
        if(Frame(advanced,hardware)<=Frame(saved,hardware)+10)throw new InvalidDataException("Original native test did not advance.");await control.LoadStateAsync(token);var restored=advanced;var exact=false;
        // Loading is an upstream queued task; observe completion instead of reading the pre-load RAM.
        for(var wait=0;wait<100;wait++)
        {
            await Task.Delay(20,token);await control.SetPausedAsync(true,token);restored=await Memory(address,hardware?8:12);
            exact=Frame(restored,hardware)>=Frame(saved,hardware)&&Frame(restored,hardware)<=Frame(saved,hardware)+2;if(exact)break;
        }
        if(!exact)throw new InvalidDataException($"Native frame counter was not restored: saved={Frame(saved,hardware)}, advanced={Frame(advanced,hardware)}, restored={Frame(restored,hardware)}.");
        var listeners=IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(e=>e.Port==62406||e.Port>=62420&&e.Port<=62423).ToArray();var local=listeners.Length==5&&listeners.All(e=>IPAddress.IsLoopback(e.Address));if(!local)throw new InvalidDataException("Native command/input listeners are not loopback-only.");
        if((await control.ControllersAsync(token)).Length!=4||(await control.CoreOptionsAsync(token)).Length==0)throw new InvalidDataException("Native controls/options missing.");
        var codecs=false;
        if(!hardware)
        {
            using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10)};http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes("home-native:"+secret)));
            using var response=await http.PostAsync("http://127.0.0.1:62403/diagnostic/whep",new StringContent(SyntheticWhep.Offer(),Encoding.UTF8,"application/sdp"),token);var sdp=await response.Content.ReadAsStringAsync(token);codecs=(int)response.StatusCode==201&&sdp.Contains("H264")&&sdp.Contains("opus");if(!codecs)throw new InvalidDataException("H.264/Opus WHEP negotiation failed.");
            if(response.Headers.Location is{} resource){using var removed=await http.DeleteAsync(new Uri(new Uri("http://127.0.0.1:62403/diagnostic/whep"),resource),token);removed.EnsureSuccessStatusCode();}
        }
        await control.QuitAsync(token);await engine.WaitForExitAsync(token);
        if(hardware)
        {
            var streams=await MediaProcess.RunAsync("/usr/bin/ffprobe",["-v","error","-show_entries","stream=codec_name","-of","json",recording],TimeSpan.FromSeconds(10),token);using var info=JsonDocument.Parse(streams);var names=info.RootElement.GetProperty("streams").EnumerateArray().Select(s=>s.GetProperty("codec_name").GetString()).ToArray();codecs=names.Contains("h264")&&names.Contains("opus");
            var pixel=Path.Combine(directory,"decoded.rgb");await MediaProcess.RunAsync("/usr/bin/ffmpeg",["-v","error","-i",recording,"-vf","scale=1:1","-frames:v","1","-pix_fmt","rgb24","-f","rawvideo",pixel],TimeSpan.FromSeconds(10),token);var rgb=await File.ReadAllBytesAsync(pixel,token);if(!codecs||rgb.Length!=3||Math.Max(rgb[0],rgb[1])<100)throw new InvalidDataException("Headless OpenGL recording did not decode its actual diagnostic framebuffer.");
        }
        return new(held,exact,codecs,local,hardware?"Xvfb + Mesa software OpenGL, decoded framebuffer":"windowless null video, live WHEP",Frame(saved,hardware),Frame(advanced,hardware),Frame(restored,hardware));
    }
    finally
    {
        if(engine is not null){if(engine.HasExited)Console.Error.WriteLine($"Original native {(hardware?"GL":"GBA")} fixture process exit: {engine.ExitCode}");MediaProcess.Kill(engine);await engine.WaitForExitAsync(CancellationToken.None);engine.Dispose();}if(mtx is not null){if(mtx.HasExited)Console.Error.WriteLine($"Diagnostic MediaMTX exited: {mtx.ExitCode}");MediaProcess.Kill(mtx);await mtx.WaitForExitAsync(CancellationToken.None);mtx.Dispose();}if(display is not null)await display.DisposeAsync();foreach(var tail in tails){var text=await tail;if(text.Length>0)Console.Error.WriteLine(text);}
    }
}
static uint Frame(byte[] memory,bool hardware)=>BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan(hardware?0:4));
async Task<byte[]> Memory(string address,int count)
{
    using var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));socket.Connect(IPAddress.Loopback,62406);await socket.SendAsync(Encoding.ASCII.GetBytes($"READ_CORE_MEMORY {address} {count}\n"),token);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(3));var text=Encoding.ASCII.GetString((await socket.ReceiveAsync(deadline.Token)).Buffer).Trim();var fields=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(fields.Length!=count+2||fields[0]!="READ_CORE_MEMORY")throw new InvalidDataException("Invalid native diagnostic reply: "+text);return fields.Skip(2).Select(value=>Convert.ToByte(value,16)).ToArray();
}
record ProofResult(int HeldFrames,bool Restored,bool Codecs,bool Loopback,string Mode,uint SavedFrame,uint AdvancedFrame,uint RestoredFrame);
