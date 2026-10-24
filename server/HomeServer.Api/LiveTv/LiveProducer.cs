using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.LiveTv;

public interface ILiveProducer
{
    bool Available { get; }
    Task RunAsync(string directory,string input,bool radio,CancellationToken token);
    Task<ProbeResult> ProbeAsync(string input,CancellationToken token) => throw new DomainException("Canlı analiz kullanılamıyor.", 409);
    Task RunAsync(string directory,string input,LiveStreamPlan plan,CancellationToken token) => RunAsync(directory,input,plan.VideoStream is null,token);
}
public sealed class LiveProducer(MediaTools tools,EncodingSupport? encoding=null):ILiveProducer
{
    public bool Available=>tools.FfmpegAvailable;
    public Task<ProbeResult> ProbeAsync(string input,CancellationToken token)=>tools.ProbeLiveRelayAsync(input,token);
    public Task RunAsync(string directory,string input,bool radio,CancellationToken token)
    {
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-re","-threads","2","-protocol_whitelist","http,https,tcp,tls,crypto","-i",input};
        if(radio)args.AddRange(["-map","0:a:0","-vn"]);else args.AddRange(["-map","0:v:0","-map","0:a:0?","-c:v","libx264","-threads","2","-preset","veryfast","-pix_fmt","yuv420p","-vf","scale=-2:'min(ih,1080)'","-b:v","4000k","-maxrate","6000k","-bufsize","12000k","-force_key_frames","expr:gte(t,n_forced*4)"]);
        args.AddRange(["-sn","-c:a","aac","-b:a","128k","-ac","2","-f","hls","-hls_time","4","-hls_list_size","450","-hls_flags","delete_segments+independent_segments+temp_file","-hls_delete_threshold","2","-hls_segment_filename",Path.Combine(directory,"segment%09d.ts"),Path.Combine(directory,"index.m3u8")]);
        return OwnedNativeProcess.RunAsync(directory,tools.FfmpegExecutable,args,TimeSpan.FromHours(12),token);
    }

    public async Task RunAsync(string directory,string input,LiveStreamPlan plan,CancellationToken token)
    {
        if(plan.ToneMap&&(encoding is null||!(await encoding.InspectAsync(token)).ToneMapping))throw new DomainException("Canlı HDR dönüşümü için tone mapping kullanılamıyor.",409);
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-threads","2","-protocol_whitelist","http,tcp,crypto",
            "-rw_timeout","15000000","-reconnect","1","-reconnect_streamed","1","-reconnect_on_network_error","1",
            "-reconnect_on_http_error","408,429,500,502,503,504","-reconnect_delay_max","4","-analyzeduration","3000000","-probesize","4000000","-fflags","+genpts","-i",input};
        if(plan.VideoStream is{}video)
        {
            args.AddRange(["-map","0:"+video]);
            if(plan.CopyVideo){args.AddRange(["-c:v","copy"]);if(plan.Container=="mp4"&&plan.VideoCodec=="hevc")args.AddRange(["-tag:v","hvc1"]);}
            else args.AddRange(["-c:v","libx264","-threads","2","-preset","veryfast","-pix_fmt","yuv420p","-profile:v","high","-fpsmax","60",
                "-vf",(plan.ToneMap?EncodingSupport.ToneMapFilter+",":"")+"scale=-2:'min(ih,1080)'","-b:v","4000k","-maxrate","6000k","-bufsize","12000k","-force_key_frames","expr:gte(t,n_forced*4)"]);
            if(plan.ToneMap)args.AddRange(["-color_primaries","bt709","-color_trc","bt709","-colorspace","bt709"]);
        }
        else args.Add("-vn");
        if(plan.AudioStream is{}audio){args.AddRange(["-map","0:"+audio]);args.AddRange(plan.CopyAudio?["-c:a","copy"]:["-c:a","aac","-b:a","128k","-ac","2","-ar","48000"]);}
        if(plan.CopyAudio&&plan.AudioCodec=="aac"&&plan.Container=="mp4")args.AddRange(["-bsf:a","aac_adtstoasc"]);
        args.AddRange(["-sn","-dn","-map_metadata","-1","-avoid_negative_ts","make_zero","-f","hls","-hls_time","4","-hls_list_size",plan.Segments.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-hls_flags","delete_segments+independent_segments+temp_file","-hls_delete_threshold","3"]);
        if(plan.Container=="mp4")args.AddRange(["-hls_segment_type","fmp4","-hls_fmp4_init_filename","init.mp4"]);
        args.AddRange(["-hls_segment_filename",Path.Combine(directory,plan.Container=="mp4"?"segment%09d.m4s":"segment%09d.ts"),Path.Combine(directory,"index.m3u8")]);
        await OwnedNativeProcess.RunAsync(directory,tools.FfmpegExecutable,args,TimeSpan.FromHours(12),token,workingDirectory:directory);
    }
}
