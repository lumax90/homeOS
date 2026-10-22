using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Home.NativeTests;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

if(args.Length<6||args.Length%2!=0||args[0]!="--candidate"||args[2]!="--ffmpeg"||args[4]!="--test-root")throw new ArgumentException("Use --candidate <reviewed descriptor> --ffmpeg <installed decoder> --test-root <isolated root> [--record-config <experiment>] [--frontend-build <reviewed host>]");
var extra=args.Skip(6).Chunk(2).ToDictionary(pair=>pair[0],pair=>pair[1]);if(extra.Keys.Any(key=>key is not("--record-config" or "--frontend-build")))throw new ArgumentException("Unknown diagnostic option.");
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
var candidate=JsonSerializer.Deserialize<NativeRuntimeCandidate>(await File.ReadAllTextAsync(args[1]),json)??throw new InvalidDataException("Candidate required.");
VerifiedFilePublication.Require(candidate.Proof.Path,candidate.Proof.Bytes,candidate.Proof.Sha256);NativeRuntimeCandidate.RequireProof(candidate.Profile,await File.ReadAllTextAsync(candidate.Proof.Path));
var profile=candidate.Profile with{Enabled=true};profile.Verify();
var frontend=profile.RetroArch.Path;var tuned=false;
if(extra.TryGetValue("--frontend-build",out var receipt)){using var document=JsonDocument.Parse(await File.ReadAllTextAsync(receipt));var build=document.RootElement;if(build.GetProperty("sourceCommit").GetString()!="69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576"||build.GetProperty("homeHostAdapter").GetString()!="home-host-1"||!build.GetProperty("hostAdapter").GetProperty("loopbackOnly").GetBoolean())throw new InvalidDataException("Reviewed loopback-only host adapter required.");frontend=build.GetProperty("executablePath").GetString()!;VerifiedFilePublication.Require(frontend,build.GetProperty("executableBytes").GetInt64(),build.GetProperty("executableSha256").GetString()!);tuned=true;}
foreach(var port in new[]{62401,62402,62406,62408})if(IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint=>endpoint.Port==port))throw new InvalidOperationException("A user game or probe is already using native test ports; do not disturb it.");
var root=AppFiles.DirectoryIn(Path.GetFullPath(args[5]),"attempt-"+Guid.NewGuid().ToString("N"));
await File.WriteAllBytesAsync(Path.Combine(root,"SYNTHETIC-ONLY.gba"),SyntheticGba.Create(latencyPixelOnly:true));
await File.WriteAllTextAsync(Path.Combine(root,"README.txt"),"Original isolated ARM video-delay fixture only. No user ROM, browser, desktop capture or library is used.\n");
await File.WriteAllTextAsync(Path.Combine(root,"retroarch.cfg"),RetroArchHostConfiguration.Create(root,62401,62406));
var recording=extra.TryGetValue("--record-config",out var experiment)?await File.ReadAllTextAsync(experiment):RetroArchHostConfiguration.Recording+(tuned?RetroArchHostConfiguration.LowDelayMux:"");
await File.WriteAllTextAsync(Path.Combine(root,"record.cfg"),recording);
await File.WriteAllTextAsync(Path.Combine(root,"core-options.cfg"),RetroArchHostConfiguration.CoreOptions);
File.Copy(profile.Core.Path,Path.Combine(root,"cores","mgba_libretro.dll"));File.Copy(profile.CoreInfo.Path,Path.Combine(root,"info","mgba_libretro.info"));
using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(40));var token=budget.Token;
using var timing=new EncodedVideoTiming();using var incoming=new UdpClient(new IPEndPoint(IPAddress.Loopback,62402));using var forwarding=new UdpClient();var packets=Task.Run(async()=>{try{while(!token.IsCancellationRequested){var packet=await incoming.ReceiveAsync(token);timing.Observe(packet.Buffer,Stopwatch.GetTimestamp());await forwarding.SendAsync(packet.Buffer,new IPEndPoint(IPAddress.Loopback,62408),token);}}catch(OperationCanceledException){}},CancellationToken.None);
using var decoder=MediaProcess.Start(Path.GetFullPath(args[3]),["-hide_banner","-loglevel","warning","-fflags","nobuffer","-flags","low_delay","-threads","1","-probesize","65536","-analyzeduration","0","-i","udp://127.0.0.1:62408?fifo_size=4096&overrun_nonfatal=1","-map","0:v:0","-an","-vf","format=rgb24,crop=1:1:0:0","-fps_mode","passthrough","-f","rawvideo","-pix_fmt","rgb24","pipe:1"],workingDirectory:root);
var decoderError=MediaProcess.ReadErrorTailAsync(decoder.StandardError);var observations=Channel.CreateBounded<ColorFrame>(new BoundedChannelOptions(256){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=true});
var frames=Task.Run(async()=>{var pixel=new byte[3];try{while(true){await decoder.StandardOutput.BaseStream.ReadExactlyAsync(pixel,token);observations.Writer.TryWrite(new(Stopwatch.GetTimestamp(),pixel[0],pixel[1],pixel[2]));}}catch(Exception error)when(error is EndOfStreamException or OperationCanceledException){observations.Writer.TryComplete();}},CancellationToken.None);
using var engine=MediaProcess.Start(frontend,["--verbose","--config",Path.Combine(root,"retroarch.cfg"),"-L",Path.Combine(root,"cores","mgba_libretro.dll"),"--record",RetroArchHostConfiguration.LocalMpegTs(62402),"--recordconfig",Path.Combine(root,"record.cfg"),Path.Combine(root,"SYNTHETIC-ONLY.gba")],workingDirectory:root);
var engineOut=MediaProcess.ReadErrorTailAsync(engine.StandardOutput);var engineError=MediaProcess.ReadErrorTailAsync(engine.StandardError);
await using var control=new RetroArchControl(engine,62406);using var pad=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));pad.Connect(IPAddress.Loopback,62401);
string? error=null;var delays=new List<double>();var presses=new List<long>();double[] encodedDelays=[];
try
{
    for(var i=0;i<100&&!IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint=>endpoint.Port==62406);i++){if(engine.HasExited)throw new InvalidOperationException("Native fixture engine exited.");await Task.Delay(20,token);}
    await control.StatusAsync(token);await control.EnablePacedHeadlessModeAsync(token);await RetroPadProtocol.ReleaseAllAsync(pad,token);
    await WaitColor(false,0);await Task.Delay(500,token);
    for(var sample=0;sample<3;sample++)
    {
        while(observations.Reader.TryRead(out _)){}
        var started=Stopwatch.GetTimestamp();presses.Add(started);await pad.SendAsync(RetroPadProtocol.Button(8,true),token);var red=await WaitColor(true,started);delays.Add(Stopwatch.GetElapsedTime(started,red.At).TotalMilliseconds);
        started=Stopwatch.GetTimestamp();await pad.SendAsync(RetroPadProtocol.Button(8,false),token);await WaitColor(false,started);await Task.Delay(250,token);
    }
    await control.QuitAsync(token);await engine.WaitForExitAsync(token);budget.Cancel();await packets;encodedDelays=await timing.AnalyzeAsync(Path.GetFullPath(args[3]),root,presses,CancellationToken.None);
}
catch(Exception failure){error=failure.ToString();Environment.ExitCode=1;}
finally
{
    MediaProcess.Kill(engine);MediaProcess.Kill(decoder);budget.Cancel();await engine.WaitForExitAsync(CancellationToken.None);await decoder.WaitForExitAsync(CancellationToken.None);await frames;await packets;
    await File.WriteAllTextAsync(Path.Combine(root,"engine.stdout.log"),await engineOut);await File.WriteAllTextAsync(Path.Combine(root,"engine.stderr.log"),await engineError);await File.WriteAllTextAsync(Path.Combine(root,"decoder.stderr.log"),await decoderError);
}
var report=new{testedAt=DateTimeOffset.UtcNow,root,recording,frontendSha256=StateFolders.Hash(frontend),decodedVideoDelayMilliseconds=delays,encodedVideoDelayMilliseconds=encodedDelays,error,syntheticIsolatedTest=true,latencyPixelOnly=true,decoderThreads=1,launchedUserRom=false,browserMeasured=false,includesMediaMtxOrWebrtc=false};
await File.WriteAllTextAsync(Path.Combine(root,"native-video-delay.json"),JsonSerializer.Serialize(report,json));Console.WriteLine(JsonSerializer.Serialize(report,json));
async Task<ColorFrame> WaitColor(bool red,long after)
{
    while(await observations.Reader.WaitToReadAsync(token))while(observations.Reader.TryRead(out var frame))if(frame.At>=after&&(red?frame.R>140&&frame.G<100:frame.G>140&&frame.R<100))return frame;
    throw new InvalidOperationException("The native fixture's encoded color transition was not observed.");
}
record ColorFrame(long At,byte R,byte G,byte B);
