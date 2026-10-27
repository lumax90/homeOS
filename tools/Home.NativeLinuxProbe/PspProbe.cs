using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

internal static class PspProbe
{
    public static async Task RunAsync(string frontendRoot,string packageRoot,string fixture,string output)
    {
        if(!OperatingSystem.IsLinux())throw new InvalidOperationException("PSP proof requires Linux.");
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
        var manifest=JsonSerializer.Deserialize<NativePspManifest>(File.ReadAllText(Path.Combine(packageRoot,"manifest.json")),json)!;
        var frontend=Path.Combine(frontendRoot,"retroarch");var core=Path.Combine(packageRoot,"ppsspp_libretro.so");
        Directory.CreateDirectory(output);var work=Path.Combine(output,"work");Directory.CreateDirectory(work);
        var cfg=RetroArchHostConfiguration.CreatePrivateHost(work,62420,62406,null);
        var stagedCore=Path.Combine(work,"cores","ppsspp_libretro.so");File.Copy(core,stagedCore,false);
        File.Copy(Path.Combine(packageRoot,"ppsspp_libretro.info"),Path.Combine(work,"info","ppsspp_libretro.info"),false);
        var system=Path.Combine(work,"system","PPSSPP");Copy(Path.Combine(packageRoot,"assets"),system);
        File.WriteAllText(Path.Combine(work,"retroarch.cfg"),cfg);
        File.WriteAllText(Path.Combine(work,"core-options.cfg"),NativeCoreConfiguration.Options("ppsspp")+NativeCoreConfiguration.RequiredOptions("ppsspp",pspSoftware:true));
        File.WriteAllText(Path.Combine(work,"record.cfg"),NativeEmulationSettings.Recording(new())+RetroArchHostConfiguration.LowDelayMux);
        File.Copy(fixture,Path.Combine(work,"game.elf"));
        using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(3));var token=budget.Token;
        using var process=MediaProcess.Start(frontend,["--verbose","--config",Path.Combine(work,"retroarch.cfg"),"-L",stagedCore,"--record",Path.Combine(work,"record.ts"),"--recordconfig",Path.Combine(work,"record.cfg"),Path.Combine(work,"game.elf")],workingDirectory:work);
        var stdout=MediaProcess.ReadErrorTailAsync(process.StandardOutput);var stderr=MediaProcess.ReadErrorTailAsync(process.StandardError);
        try
        {
            await using var control=new RetroArchControl(process,62406);await control.WaitReadyAsync(TimeSpan.FromSeconds(30),token);
            await control.EnablePacedHeadlessModeAsync(token);await control.SetPausedAsync(false,token);
            // PPSSPP exposes retro_get_memory_data, but does not register frontend
            // memory maps for READ_CORE_MEMORY. Observe its real uncompressed
            // save-state RAM, marked only by our original isolated diagnostic.
            byte[] initial=await CaptureCounters();
            var heldInitial=BinaryPrimitives.ReadUInt32LittleEndian(initial.AsSpan(4));
            using var pad=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));pad.Connect(IPAddress.Loopback,62420);
            await pad.SendAsync(RetroPadProtocol.Button(0,true,ushort.MaxValue),token);
            await pad.SendAsync(RetroPadProtocol.Analog(0,0,20000),token);
            byte[] observed=initial;
            for(var i=0;i<10;i++){await Task.Delay(300,token);observed=await CaptureCounters();if(BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(4))>=heldInitial+10&&BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(8))>150)break;}
            var input=BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(4))>=heldInitial+10&&BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(8))>150;
            if(!input)throw new InvalidDataException("PSP held button/analog input was not observed.");
            var saved=await CaptureCounters(resume:false);var state=Path.Combine(work,"saves","game.state");var preserved=Path.Combine(output,"preserved.state");File.Copy(state,preserved,false);
            await pad.SendAsync(RetroPadProtocol.Button(0,false,ushort.MaxValue),token);await pad.SendAsync(RetroPadProtocol.Analog(0,0,0),token);
            await control.SetPausedAsync(false,token);await Task.Delay(500,token);var advanced=await CaptureCounters(resume:false);
            if(BinaryPrimitives.ReadUInt32LittleEndian(advanced)<=BinaryPrimitives.ReadUInt32LittleEndian(saved)+5)throw new InvalidDataException("PSP diagnostic did not advance beyond its saved state.");
            File.Copy(preserved,state,true);await control.LoadStateAsync(token);var restored=false;
            var savedFrame=BinaryPrimitives.ReadUInt32LittleEndian(saved);
            await Task.Delay(300,token);var restoredCounters=await CaptureCounters(resume:false);var restoredFrame=BinaryPrimitives.ReadUInt32LittleEndian(restoredCounters);
            restored=restoredFrame>=savedFrame&&restoredFrame<=savedFrame+2;
            if(!restored)throw new InvalidDataException("PSP state did not restore its actual RAM counters.");
            await control.QuitAsync(token);await process.WaitForExitAsync(token);
            var data=await MediaProcess.RunAsync("/usr/bin/ffprobe",["-v","error","-show_streams","-of","json",Path.Combine(work,"record.ts")],TimeSpan.FromSeconds(10),token);
            using var probe=JsonDocument.Parse(data);var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var codecs=streams.Any(s=>s.GetProperty("codec_name").GetString()=="h264")&&streams.Any(s=>s.GetProperty("codec_name").GetString()=="opus");
            await MediaProcess.RunAsync("/usr/bin/ffmpeg",["-v","error","-i",Path.Combine(work,"record.ts"),"-vf","scale=1:1","-frames:v","120","-pix_fmt","rgb24","-f","rawvideo",Path.Combine(work,"pixel.rgb")],TimeSpan.FromSeconds(10),token);
            var pixel=File.ReadAllBytes(Path.Combine(work,"pixel.rgb"));var software=false;
            for(var i=0;i+2<pixel.Length;i+=3)if(Math.Max(pixel[i],pixel[i+1])>80){software=true;break;} // Loader may legitimately begin with a black frame.
            if(!codecs||!software)throw new InvalidDataException("PSP software framebuffer/audio output did not decode.");
            await MediaProcess.RunAsync("/usr/bin/ffmpeg",["-v","error","-i",Path.Combine(work,"record.ts"),"-t","2","-vn","-f","s16le","-ac","1",Path.Combine(work,"audio.pcm")],TimeSpan.FromSeconds(10),token);
            var pcm=File.ReadAllBytes(Path.Combine(work,"audio.pcm"));var sound=false;for(var i=0;i+1<pcm.Length;i+=2)if(Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i)))>100){sound=true;break;}
            if(!sound)throw new InvalidDataException("PSP's original fixture audio signal was not decoded.");
            var result=new{format=1,coreSha256=StateFolders.Hash(core),frontendSha256=StateFolders.Hash(frontend),softwareFrames=software,inputVerified=input,saveRestore=restored,h264Opus=codecs,audioSignal=sound,ramObservation="original marker in actual uncompressed native state",savedFrame,advancedFrame=BinaryPrimitives.ReadUInt32LittleEndian(advanced),restoredFrame,error=(string?)null,originalIsolatedFixture=true,internetMediaAcquired=false};
            var proof=Path.Combine(packageRoot,"proof.json");File.WriteAllText(proof,JsonSerializer.Serialize(result,json));
            File.WriteAllText(Path.Combine(packageRoot,"manifest.json"),JsonSerializer.Serialize(manifest with{Proof=new("proof.json",new FileInfo(proof).Length,StateFolders.Hash(proof))},json));
            Console.WriteLine(JsonSerializer.Serialize(result,json));
            async Task<byte[]> CaptureCounters(bool resume=true)
            {
                var path=Path.Combine(work,"saves","game.state");var marker="HOME-ORIGINAL-PSP-RAM-PROBE-V1!\0"u8.ToArray();
                for(var attempt=0;attempt<10;attempt++)
                {
                    await control.SetPausedAsync(true,token);if(File.Exists(path))File.Delete(path);await control.SaveStateAsync(token);
                    long last=-1;var quiet=0;
                    for(var wait=0;wait<200;wait++)
                    {
                        await Task.Delay(25,token);if(!File.Exists(path))continue;var bytes=new FileInfo(path).Length;
                        if(bytes>256*1024*1024)throw new InvalidDataException("PSP diagnostic state exceeded its bound.");
                        quiet=bytes>0&&bytes==last?quiet+1:0;last=bytes;if(quiet<4)continue;
                        var data=await File.ReadAllBytesAsync(path,token);var found=new List<byte[]>();
                        for(var at=0;at<=data.Length-marker.Length-12;)
                        {
                            var index=data.AsSpan(at).IndexOf(marker);if(index<0)break;at+=index;var counters=data.AsSpan(at+marker.Length,12).ToArray();
                            if(BinaryPrimitives.ReadUInt32LittleEndian(counters)>0)found.Add(counters);at+=marker.Length;
                        }
                        if(found.Count==1){if(resume)await control.SetPausedAsync(false,token);return found[0];}
                        if(found.Count>1)throw new InvalidDataException("PSP diagnostic RAM marker was ambiguous.");break;
                    }
                    await control.SetPausedAsync(false,token);await Task.Delay(150,token);
                }
                throw new InvalidDataException("PSP native state did not contain the original fixture's running RAM counter.");
            }
        }
        finally{var exited=process.HasExited?process.ExitCode:(int?)null;MediaProcess.Kill(process);await process.WaitForExitAsync(CancellationToken.None);File.WriteAllText(Path.Combine(output,"engine.log"),$"engineExitBeforeCleanup={exited?.ToString()??"running"}\n"+await stdout+"\n"+await stderr);}
    }
    private static void Copy(string source,string destination)
    {Directory.CreateDirectory(destination);foreach(var file in Directory.EnumerateFiles(source))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)),false);foreach(var directory in Directory.EnumerateDirectories(source))Copy(directory,Path.Combine(destination,Path.GetFileName(directory)));}
}
