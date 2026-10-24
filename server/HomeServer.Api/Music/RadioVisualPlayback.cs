using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.LiveTv;

namespace HomeServer.Music;

public sealed record RadioVisualRequest(string? SceneId,long Revision);
public sealed record RadioVisualLaunch(string Url,bool Hls,string? SceneId,string? Generation,string? PosterUrl=null);
public sealed partial class RadioVisualPlayback(HomePaths paths,MediaTools tools,RadioPlayback radio,MusicSceneCache scenes,
    MusicExperiencePreferences preferences,LiveInputRelay relay):BackgroundService
{
    private sealed class Run(string id,string directory,CancellationTokenSource stop)
    {
        public string Id=id,Directory=directory;public CancellationTokenSource Stop=stop;public CancellationToken Token=stop.Token;public Task Work=Task.CompletedTask;public bool Failed;
    }
    private sealed class State(RadioVisualBinding binding)
    {
        public RadioVisualBinding Binding=binding;public object Sync=new();public SemaphoreSlim Gate=new(1,1);public long Revision;
        public string? Scene;public CancellationTokenSource? Pending;public Task<RadioVisualLaunch>? Operation;public Run? Active;
    }
    private readonly ConcurrentDictionary<string,State> _states=new();
    private string Root=>AppFiles.DirectoryIn(paths.Data,"radio-visual");
    public Task<RadioVisualLaunch> ConfigureAsync(string id,Account actor,string login,RadioVisualRequest input,CancellationToken token)
    {
        var binding=radio.VisualBinding(id,actor,login);
        if(input.Revision is <1 or >9_007_199_254_740_991)throw new DomainException("Görsel oynatma komutu geçersiz.");
        if(input.SceneId is not null){_=MusicSceneCatalog.Require(input.SceneId);if(!preferences.Read(actor.Id).OnlineScenes)throw new DomainException("Stok görüntüler kapalı.",409);}
        var state=_states.GetOrAdd(id,_=>new(binding));
        lock(state.Sync)
        {
            if(input.Revision<state.Revision||input.Revision==state.Revision&&input.SceneId!=state.Scene)throw new DomainException("Daha yeni bir görsel seçimi var.",409);
            if(input.Revision==state.Revision&&state.Operation is not null)return state.Operation.WaitAsync(token);
            state.Pending?.Cancel();state.Revision=input.Revision;state.Scene=input.SceneId;
            var pending=CancellationTokenSource.CreateLinkedTokenSource(binding.Lifetime,token);state.Pending=pending;
            return state.Operation=ReplaceAsync(state,id,actor,input,pending);
        }
    }
    private async Task<RadioVisualLaunch> ReplaceAsync(State state,string sessionId,Account actor,RadioVisualRequest input,CancellationTokenSource pending)
    {
        var entered=false;Run? run=null;
        try
        {
            await state.Gate.WaitAsync(pending.Token);entered=true;state.Binding.Validate();pending.Token.ThrowIfCancellationRequested();
            var old=state.Active;state.Active=null;if(old is not null)await StopAsync(old);
            if(input.SceneId is null)return new(state.Binding.AudioUrl,state.Binding.Station.Hls,null,null);
            var scene=await scenes.OpenAsync(input.SceneId,pending.Token)??throw new DomainException("Görsel sahne henüz hazırlanmadı.",409);
            if(!preferences.Read(actor.Id).OnlineScenes)throw new DomainException("Stok görüntüler kapalı.",409);
            var id=Guid.NewGuid().ToString("N");var directory=AppFiles.DirectoryIn(Root,id);
            await File.WriteAllTextAsync(Path.Combine(directory,".home-radio-visual"),"Home.RadioVisual.v1",pending.Token);
            run=new(id,directory,CancellationTokenSource.CreateLinkedTokenSource(state.Binding.Lifetime));state.Active=run;
            using var cancelStartup=pending.Token.Register(()=>run.Stop.Cancel());
            var link=relay.Register(state.Binding.Station.Url,false,state.Binding.Validate);
            run.Work=ProduceAsync(run,scene.Path,link);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(pending.Token);timeout.CancelAfter(TimeSpan.FromSeconds(40));
            var playlist=Path.Combine(directory,"index.m3u8");
            while(!File.Exists(playlist)){if(run.Work.IsCompleted)throw new DomainException("Radyo ve görüntü birlikte başlatılamadı.",502);await Task.Delay(100,timeout.Token);}
            if(run.Failed)throw new DomainException("Radyo ve görüntü birlikte başlatılamadı.",502);
            timeout.Token.ThrowIfCancellationRequested();state.Binding.Validate();
            lock(state.Sync)if(state.Revision!=input.Revision)throw new OperationCanceledException();
            return new($"/api/music/radio/sessions/{sessionId}/visual/{id}/index.m3u8",true,input.SceneId,id,$"/api/music/scenes/{input.SceneId}/poster");
        }
        catch{if(run is not null){if(ReferenceEquals(state.Active,run))state.Active=null;await StopAsync(run);}throw;}
        finally
        {
            if(entered)state.Gate.Release();lock(state.Sync){if(ReferenceEquals(state.Pending,pending))state.Pending=null;}pending.Dispose();
        }
    }
    private async Task ProduceAsync(Run run,string scene,(string Token,string Url) input)
    {
        try
        {
            await OwnedNativeProcess.RunAsync(run.Directory,tools.FfmpegExecutable,
                ["-hide_banner","-loglevel","error","-nostdin","-stream_loop","-1","-re","-protocol_whitelist","file,pipe","-i",scene,
                 "-protocol_whitelist","http,tcp,crypto","-rw_timeout","15000000","-i",input.Url,"-map","0:v:0","-map","1:a:0","-sn","-dn","-map_metadata","-1",
                 "-c:v","copy","-c:a","aac","-ac","2","-ar","48000","-b:a","128k","-threads","1","-shortest","-avoid_negative_ts","make_zero",
                 "-f","hls","-hls_time","2","-hls_list_size","16","-hls_delete_threshold","3","-hls_flags","delete_segments+independent_segments+temp_file",
                 "-hls_segment_type","fmp4","-hls_fmp4_init_filename","init.mp4","-hls_segment_filename",Path.Combine(run.Directory,"segment%06d.m4s"),Path.Combine(run.Directory,"index.m3u8")],
                TimeSpan.FromHours(12),run.Token,workingDirectory:run.Directory);
        }
        catch(OperationCanceledException)when(run.Token.IsCancellationRequested){}
        catch(Exception){run.Failed=true;}
        finally{relay.Remove(input.Token);}
    }
    private static async Task StopAsync(Run run)
    {run.Stop.Cancel();await run.Work;run.Stop.Dispose();if(Directory.Exists(run.Directory))AppFiles.DeleteOwnedDirectory(run.Directory);}
    public IResult Artifact(string id,string generation,string file,Account actor,string login)
    {
        _=radio.VisualBinding(id,actor,login);
        if(!_states.TryGetValue(id,out var state)||state.Active is not{}run||run.Id!=generation||run.Token.IsCancellationRequested)throw new DomainException("Görsel radyo akışı sona erdi.",410);
        if(!ArtifactName().IsMatch(file))throw new DomainException("Görsel radyo parçası bulunamadı.",404);
        if(run.Failed)throw new DomainException("Görsel radyo akışı kesildi.",502);
        return new Output(MediaPaths.ResolveFile(run.Directory,file),file.EndsWith(".m3u8",StringComparison.Ordinal)?"application/vnd.apple.mpegurl":"video/mp4",run.Token);
    }
    private sealed class Output(string path,string mime,CancellationToken token):IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {using var abort=token.Register(context.Abort);token.ThrowIfCancellationRequested();context.Response.Headers.CacheControl="no-store";await Results.Stream(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous),mime,enableRangeProcessing:true).ExecuteAsync(context);}
    }
    public override async Task StartAsync(CancellationToken token)
    {
        foreach(var directory in Directory.EnumerateDirectories(Root))
        {
            var marker=Path.Combine(directory,".home-radio-visual");if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _)||!File.Exists(marker))continue;
            AppFiles.RejectLink(directory);AppFiles.RejectLink(marker);if(File.ReadAllText(marker)!="Home.RadioVisual.v1")continue;
            await OwnedNativeProcess.RecoverAsync(directory,token);AppFiles.DeleteOwnedDirectory(directory);
        }
        await base.StartAsync(token);
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                foreach(var pair in _states)
                {
                    var state=pair.Value;if(!await state.Gate.WaitAsync(0,token))continue;
                    try
                    {
                        var valid=true;try{state.Binding.Validate();}catch(DomainException){valid=false;}
                        if(!valid){lock(state.Sync)state.Pending?.Cancel();if(state.Active is{}old){state.Active=null;await StopAsync(old);}_states.TryRemove(pair.Key,out _);}
                        else if(state.Active is{}run)
                        {
                            long bytes=0;foreach(var file in Directory.EnumerateFiles(run.Directory))try{bytes+=new FileInfo(file).Length;}catch(FileNotFoundException){}
                            if(bytes>64L*1024*1024){run.Failed=true;run.Stop.Cancel();}
                        }
                    }
                    finally{state.Gate.Release();}
                }
                await Task.Delay(1000,token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        finally
        {
            foreach(var state in _states.Values){lock(state.Sync)state.Pending?.Cancel();await state.Gate.WaitAsync();try{if(state.Active is{}run){state.Active=null;await StopAsync(run);}}finally{state.Gate.Release();}}
            _states.Clear();
        }
    }
    [GeneratedRegex(@"^(?:index\.m3u8|init\.mp4|segment[0-9]{6}\.m4s)$",RegexOptions.NonBacktracking)]private static partial Regex ArtifactName();
}
