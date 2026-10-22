using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;

public interface ILiveProducer { bool Available {get;} Task RunAsync(string directory,string input,bool radio,CancellationToken token); }
public sealed class LiveProducer(MediaTools tools):ILiveProducer
{
    public bool Available=>tools.FfmpegAvailable;
    public Task RunAsync(string directory,string input,bool radio,CancellationToken token)
    {
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-re","-threads","2","-protocol_whitelist","http,https,tcp,tls,crypto","-i",input};
        if(radio)args.AddRange(["-map","0:a:0","-vn"]);else args.AddRange(["-map","0:v:0","-map","0:a:0?","-c:v","libx264","-threads","2","-preset","veryfast","-pix_fmt","yuv420p","-vf","scale=-2:'min(ih,1080)'","-b:v","4000k","-maxrate","6000k","-bufsize","12000k","-force_key_frames","expr:gte(t,n_forced*4)"]);
        args.AddRange(["-sn","-c:a","aac","-b:a","128k","-ac","2","-f","hls","-hls_time","4","-hls_list_size","450","-hls_flags","delete_segments+independent_segments+temp_file","-hls_delete_threshold","2","-hls_segment_filename",Path.Combine(directory,"segment%09d.ts"),Path.Combine(directory,"index.m3u8")]);
        return OwnedNativeProcess.RunAsync(directory,tools.FfmpegExecutable,args,TimeSpan.FromHours(12),token);
    }
}
