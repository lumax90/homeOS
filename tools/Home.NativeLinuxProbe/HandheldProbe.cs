using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

internal static class HandheldProbe
{
    public static async Task RunAsync(string frontendRoot,string packageRoot,string id,string fixture,string output,string? selkiesRoot=null)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
        var manifest=JsonSerializer.Deserialize<NativeHandheldManifest>(File.ReadAllText(Path.Combine(packageRoot,"manifest.json")),json)!;
        var core=manifest.Cores.Single(core=>core.Id==id);var frontend=Path.Combine(frontendRoot,"retroarch");var binary=Path.Combine(packageRoot,core.Binary.Path);
        var work=Path.Combine(output,"work");Directory.CreateDirectory(work);var ds=id=="desmume";var width=ds?256:400;var height=ds?384:480;
        var alternate=selkiesRoot is not null;
        var config=alternate?RetroArchHostConfiguration.CreateSelkies(work,62420,62406,ds?null:"glcore",width,height):RetroArchHostConfiguration.CreatePrivateHost(work,62420,62406,ds?null:"glcore",width,height);
        File.WriteAllText(Path.Combine(work,"retroarch.cfg"),config);File.WriteAllText(Path.Combine(work,"core-options.cfg"),NativeCoreConfiguration.Options(id)+NativeCoreConfiguration.RequiredOptions(id,handheld:ds));
        File.WriteAllText(Path.Combine(work,"record.cfg"),NativeEmulationSettings.Recording(new(),width,height)+RetroArchHostConfiguration.LowDelayMux);
        var staged=Path.Combine(work,"cores",id+"_libretro.so");File.Copy(binary,staged,false);File.Copy(Path.Combine(packageRoot,core.Info.Path),Path.Combine(work,"info",id+"_libretro.info"),false);
        var content=Path.Combine(work,ds?"game.nds":"game.3dsx");File.Copy(fixture,content,false);NativeCoreConfiguration.PrepareSaveDirectories(id,Path.Combine(work,"saves"));
        using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(3));var token=budget.Token;
        await using var display=ds||alternate?null:await NativeLinuxDisplay.StartAsync(work,width,height,token);
        NativeSelkiesSession? selkies=null;var publisherToken=new string('a',64);
        if(alternate){var installed=JsonSerializer.Deserialize<NativeSelkiesManifest>(File.ReadAllText(Path.Combine(selkiesRoot!,"manifest.json")),json)!;if(installed.BaseFrontendSha256!=StateFolders.Hash(frontend))throw new InvalidDataException("Selkies base runtime identity changed.");var target=Path.Combine(selkiesRoot!,installed.Frontend.Path);selkies=await NativeSelkiesSession.PrepareAsync(output,new(installed,target),"/handheld-proof","http://127.0.0.1",publisherToken,width,height,token);frontend=target;}
        await using var alternateSession=selkies;
        var arguments=alternate?new[]{"--verbose","--config",Path.Combine(work,"retroarch.cfg"),"-L",staged,content}:new[]{"--verbose","--config",Path.Combine(work,"retroarch.cfg"),"-L",staged,"--record",Path.Combine(work,"record.ts"),"--recordconfig",Path.Combine(work,"record.cfg"),content};
        using var process=MediaProcess.Start(frontend,arguments,workingDirectory:work,environment:selkies?.Environment??display?.Environment);
        var stdout=MediaProcess.ReadErrorTailAsync(process.StandardOutput);var stderr=MediaProcess.ReadErrorTailAsync(process.StandardError);
        try
        {
            await using var control=new RetroArchControl(process,62406,ds?null:TimeSpan.FromSeconds(15));await control.WaitReadyAsync(TimeSpan.FromSeconds(30),token);if(!alternate)await control.EnablePacedHeadlessModeAsync(token);await control.SetPausedAsync(false,token);
            if(selkies is not null)await selkies.StartAsync(["127.0.0.1"],width,height,60,"auto",token);
            var initial=await Counters();var firstHeld=BinaryPrimitives.ReadUInt32LittleEndian(initial.AsSpan(4));
            using var pad=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));pad.Connect(IPAddress.Loopback,62420);
            await pad.SendAsync(RetroPadProtocol.Button(8,true,ushort.MaxValue),token);
            var pointer=new NativePointer(0,16384,true,1);var frame=NativePadFrame.From(1<<8,[0,0,0,0],pointer);
            foreach(var packet in RetroPadProtocol.PeripheralChanges(default,frame))await pad.SendAsync(packet,token);
            byte[] held=initial;for(var attempt=0;attempt<8;attempt++){await Task.Delay(300,token);held=await Counters();if(BinaryPrimitives.ReadUInt32LittleEndian(held.AsSpan(4))>=firstHeld+8&&BinaryPrimitives.ReadUInt32LittleEndian(held.AsSpan(8))>0)break;}
            var input=BinaryPrimitives.ReadUInt32LittleEndian(held.AsSpan(4))>=firstHeld+8;var touch=BinaryPrimitives.ReadUInt32LittleEndian(held.AsSpan(8))>0;
            if(!input||!touch)throw new InvalidDataException($"{id} actual button/stylus counters were not observed.");
            var saved=await Counters(false);var state=Path.Combine(work,"saves","game.state");var keep=Path.Combine(output,"preserved.state");File.Copy(state,keep,false);
            await RetroPadProtocol.ReleaseAllAsync(pad,token);foreach(var packet in RetroPadProtocol.PeripheralChanges(frame,default,true))await pad.SendAsync(packet,token);await control.SetPausedAsync(false,token);await Task.Delay(500,token);var advanced=await Counters(false);
            File.Copy(keep,state,true);await control.LoadStateAsync(token);await Task.Delay(300,token);var restored=await Counters(false);
            var savedFrame=BinaryPrimitives.ReadUInt32LittleEndian(saved);var restoredFrame=BinaryPrimitives.ReadUInt32LittleEndian(restored);var restore=restoredFrame>=savedFrame&&restoredFrame<=savedFrame+2;
            if(!restore||BinaryPrimitives.ReadUInt32LittleEndian(advanced)<=savedFrame+3)throw new InvalidDataException(id+" native state did not restore the original RAM counter.");
            if(selkies is not null)
            {
                var alternatePixel=Path.Combine(work,"selkies-window.rgb");
                using(var capture=MediaProcess.Start("/usr/bin/ffmpeg",["-v","error","-f","x11grab","-video_size",$"{width}x{height}","-i",selkies.Environment["DISPLAY"],"-vf","scale=1:2","-frames:v","1","-pix_fmt","rgb24","-f","rawvideo",alternatePixel],workingDirectory:work,environment:selkies.Environment))
                {var errors=MediaProcess.ReadErrorTailAsync(capture.StandardError);using var wait=CancellationTokenSource.CreateLinkedTokenSource(token);wait.CancelAfter(TimeSpan.FromSeconds(10));try{await capture.WaitForExitAsync(wait.Token);if(capture.ExitCode!=0)throw new InvalidDataException(await errors);}finally{MediaProcess.Kill(capture);await capture.WaitForExitAsync(CancellationToken.None);await errors;}}
                var alternateVideo=File.ReadAllBytes(alternatePixel).Any(value=>value>90);using var request=selkies.ReadRequest("api/health");using var response=await selkies.Http.SendAsync(request,token);response.EnsureSuccessStatusCode();
                if(!alternateVideo)throw new InvalidDataException(id+" Selkies display did not expose the actual native framebuffer.");
                var alternateResult=new{format=1,coreSha256=StateFolders.Hash(binary),baseFrontendSha256=StateFolders.Hash(Path.Combine(frontendRoot,"retroarch")),frontendSha256=StateFolders.Hash(frontend),selkiesVersion="2.1.0",videoFrames=alternateVideo,inputVerified=input,touchVerified=touch,saveRestore=restore,publisherHealthy=true,width,height,originalIsolatedFixture=true,internetMediaAcquired=false,error=(string?)null};
                File.WriteAllText(Path.Combine(packageRoot,id+"-selkies-proof.json"),JsonSerializer.Serialize(alternateResult,json));Console.WriteLine(JsonSerializer.Serialize(alternateResult,json));await control.QuitAsync(token);await process.WaitForExitAsync(token);return;
            }
            await control.QuitAsync(token);await process.WaitForExitAsync(token);
            var probe=await MediaProcess.RunAsync("/usr/bin/ffprobe",["-v","error","-show_streams","-of","json",Path.Combine(work,"record.ts")],TimeSpan.FromSeconds(10),token);
            using var streams=JsonDocument.Parse(probe);var names=streams.RootElement.GetProperty("streams").EnumerateArray().Select(stream=>stream.GetProperty("codec_name").GetString()).ToArray();var codecs=names.Contains("h264")&&names.Contains("opus");
            var pixel=Path.Combine(work,"pixels.rgb");await MediaProcess.RunAsync("/usr/bin/ffmpeg",["-v","error","-i",Path.Combine(work,"record.ts"),"-vf","scale=1:2","-frames:v","120","-pix_fmt","rgb24","-f","rawvideo",pixel],TimeSpan.FromSeconds(10),token);
            var colours=File.ReadAllBytes(pixel);var video=colours.Any(value=>value>90);if(!video||!codecs)throw new InvalidDataException(id+" dual-screen native framebuffer/codecs did not decode.");
            var result=new{format=1,coreSha256=StateFolders.Hash(binary),frontendSha256=StateFolders.Hash(frontend),videoFrames=video,inputVerified=input,touchVerified=touch,saveRestore=restore,h264Opus=codecs,savedFrame,advancedFrame=BinaryPrimitives.ReadUInt32LittleEndian(advanced),restoredFrame,originalIsolatedFixture=true,internetMediaAcquired=false,error=(string?)null};
            var proof=Path.Combine(packageRoot,id+"-proof.json");File.WriteAllText(proof,JsonSerializer.Serialize(result,json));var asset=new NativeBundleAsset(id+"-proof.json",new FileInfo(proof).Length,StateFolders.Hash(proof));
            File.WriteAllText(Path.Combine(packageRoot,"manifest.json"),JsonSerializer.Serialize(manifest with{Cores=manifest.Cores.Select(item=>item.Id==id?item with{Proof=asset}:item).ToArray()},json));Console.WriteLine(JsonSerializer.Serialize(result,json));
            async Task<byte[]> Counters(bool resume=true)
            {
                var path=Path.Combine(work,"saves","game.state");var marker=System.Text.Encoding.ASCII.GetBytes(ds?"HOME-ORIGINAL-NDS-RAM-PROBE-V1!\0":"HOME-ORIGINAL-3DS-RAM-PROBE-V1!\0");
                for(var attempt=0;attempt<10;attempt++)
                {
                    try{await control.SetPausedAsync(true,token);}catch(HomeServer.Core.DomainException)when(!process.HasExited){await Task.Delay(250,token);continue;}
                    if(File.Exists(path))File.Delete(path);await control.SaveStateAsync(token);
                    long last=-1;var quiet=0;for(var wait=0;wait<200;wait++)
                    {
                        await Task.Delay(25,token);if(!File.Exists(path))continue;var length=new FileInfo(path).Length;if(length>256*1024*1024)throw new InvalidDataException("Native diagnostic state is too large.");quiet=length>0&&length==last?quiet+1:0;last=length;if(quiet<4)continue;
                        var data=await File.ReadAllBytesAsync(path,token);if(!ds){var at=data.AsSpan().IndexOf(new byte[]{0x28,0xb5,0x2f,0xfd});if(at>=0)data=Decompress(data[at..]);}
                        var matches=new List<byte[]>();for(var at=0;at<=data.Length-44;){var index=data.AsSpan(at).IndexOf(marker);if(index<0)break;at+=index;var value=data.AsSpan(at+32,12).ToArray();if(BinaryPrimitives.ReadUInt32LittleEndian(value)>0)matches.Add(value);at+=32;}
                        if(matches.Count==1){if(resume)await control.SetPausedAsync(false,token);return matches[0];}if(matches.Count>1)throw new InvalidDataException("Original diagnostic RAM marker is ambiguous.");break;
                    }
                    await control.SetPausedAsync(false,token);await Task.Delay(150,token);
                }
                throw new InvalidDataException(id+" native state does not contain the running original diagnostic.");
            }
        }
        finally{var code=process.HasExited?process.ExitCode:(int?)null;MediaProcess.Kill(process);await process.WaitForExitAsync(CancellationToken.None);File.WriteAllText(Path.Combine(output,"engine.log"),$"exitBeforeCleanup={code?.ToString()??"running"}\n"+await stdout+"\n"+await stderr);}
    }
    private static byte[] Decompress(byte[] data)
    {
        var frame=ZSTD_findFrameCompressedSize(data,(nuint)data.Length);if(ZSTD_isError(frame)!=0||frame>(nuint)data.Length)throw new InvalidDataException("Citra state compressed frame is incomplete: "+Marshal.PtrToStringAnsi(ZSTD_getErrorName(frame)));
        if(frame!=(nuint)data.Length)data=data[..(int)frame]; // RetroArch can append/pad its state container beyond the core payload.
        var size=ZSTD_getFrameContentSize(data,(nuint)data.Length);if(size>=ulong.MaxValue-1)size=ZSTD_decompressBound(data,(nuint)data.Length);
        if(size is 0 or >512*1024*1024)throw new InvalidDataException("Citra diagnostic state decompression bound exceeded: "+size);
        var result=new byte[(int)size];var written=ZSTD_decompress(result,(nuint)result.Length,data,(nuint)data.Length);if(ZSTD_isError(written)!=0||written>(nuint)result.Length)throw new InvalidDataException("Citra native state decompression failed: "+Marshal.PtrToStringAnsi(ZSTD_getErrorName(written)));if(written!=(nuint)result.Length)Array.Resize(ref result,(int)written);return result;
    }
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern ulong ZSTD_getFrameContentSize(byte[] source,nuint size);
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern ulong ZSTD_decompressBound(byte[] source,nuint size);
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern nuint ZSTD_findFrameCompressedSize(byte[] source,nuint size);
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr ZSTD_getErrorName(nuint result);
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern nuint ZSTD_decompress(byte[] output,nuint capacity,byte[] source,nuint size);
    [DllImport("libzstd.so.1",CallingConvention=CallingConvention.Cdecl)]private static extern uint ZSTD_isError(nuint result);
}
