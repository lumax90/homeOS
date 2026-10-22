using System.Diagnostics;
using System.Text.Json;
using HomeServer.Infrastructure;

namespace Home.NativeTests;

/// <summary>Diagnostic-only timestamp correlation. FFmpeg decodes the captured original fixture; this reads only PES timestamps, never decodes media.</summary>
public sealed class EncodedVideoTiming : IDisposable
{
    private readonly MemoryStream _capture=new();
    private readonly Dictionary<long,long> _arrivals=new();
    private int _videoPid=-1;
    private long? _pts;
    public void Observe(byte[] datagram,long at)
    {
        if(_capture.Length+datagram.Length>16*1024*1024)throw new InvalidDataException("Original timing capture exceeded its bound.");
        _capture.Write(datagram);
        for(var offset=0;offset+188<=datagram.Length;offset+=188)
        {
            var packet=datagram.AsSpan(offset,188);if(packet[0]!=0x47)throw new InvalidDataException("Original timing capture has invalid TS framing.");
            var pid=((packet[1]&31)<<8)|packet[2];var adaptation=(packet[3]>>4)&3;if(adaptation is 0 or 2)continue;
            var payload=4+(adaptation==3?1+packet[4]:0);if(payload>=188)continue;
            var data=packet[payload..];
            if((packet[1]&0x40)!=0&&data.Length>=14&&data[0]==0&&data[1]==0&&data[2]==1&&data[3] is >=0xe0 and <=0xef&&(data[7]&0x80)!=0)
            {
                _videoPid=pid;var p=data[9..];_pts=((long)(p[0]&14)<<29)|((long)p[1]<<22)|((long)(p[2]&254)<<14)|((long)p[3]<<7)|((long)p[4]>>1);
            }
            if(pid==_videoPid&&_pts is{} pts)_arrivals[pts]=at;
        }
    }
    public async Task<double[]> AnalyzeAsync(string ffmpeg,string root,IReadOnlyList<long> presses,CancellationToken token)
    {
        var ts=Path.Combine(root,"arrival-timing.ts");var rgb=Path.Combine(root,"arrival-timing.rgb");await File.WriteAllBytesAsync(ts,_capture.ToArray(),token);
        var metadata=await MediaProcess.RunAsync(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp","-of","json",ts],TimeSpan.FromSeconds(10),token,root);
        using var document=JsonDocument.Parse(metadata);var stamps=document.RootElement.GetProperty("frames").EnumerateArray().Select(frame=>frame.GetProperty("best_effort_timestamp").GetInt64()).ToArray();
        await MediaProcess.RunAsync(ffmpeg,["-v","error","-i",ts,"-an","-vf","format=rgb24,crop=1:1:0:0","-fps_mode","passthrough","-pix_fmt","rgb24","-f","rawvideo",rgb],TimeSpan.FromSeconds(10),token,root);
        var colors=await File.ReadAllBytesAsync(rgb,token);if(colors.Length!=stamps.Length*3)throw new InvalidDataException("Original timing frames did not align with FFmpeg timestamps.");
        var delays=new List<double>();foreach(var press in presses)
        {
            var index=Array.FindIndex(stamps,pts=>_arrivals.TryGetValue(pts,out var at)&&at>=press);
            for(;index>=0&&index<stamps.Length;index++)if(colors[index*3]>150&&colors[index*3+1]<90)break;
            if(index<0||index==stamps.Length||!_arrivals.TryGetValue(stamps[index],out var arrived))throw new InvalidDataException("Original fixture input transition was not correlated to an encoded frame.");
            delays.Add(Stopwatch.GetElapsedTime(press,arrived).TotalMilliseconds);
        }
        return delays.ToArray();
    }
    public void Dispose()=>_capture.Dispose();
}
