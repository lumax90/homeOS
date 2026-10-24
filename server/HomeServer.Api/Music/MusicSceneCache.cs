using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Music;

public sealed record MusicScene(string Id,string Scene,string Label,string Url,string Author,string License,string LicenseUrl,string Source,string CommonsTitle);
public static class MusicSceneCatalog
{
    private static readonly Lazy<MusicScene[]> Items=new(()=>
    {
        using var stream=typeof(MusicSceneCatalog).Assembly.GetManifestResourceStream("HomeServer.Music.SceneCatalog.json")!;
        return JsonSerializer.Deserialize<MusicScene[]>(stream,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    });
    public static MusicScene Require(string id)=>Items.Value.FirstOrDefault(item=>item.Id==id)??throw new DomainException("Görsel sahne bulunamadı.",404);
}
public interface IMusicSceneSource { Task<HttpResponseMessage> GetAsync(string url,CancellationToken token); }
public sealed class MusicSceneSource(RemoteMediaHttp http):IMusicSceneSource
{ public Task<HttpResponseMessage> GetAsync(string url,CancellationToken token)=>http.GetAsync(url,false,token); }
public sealed record PreparedMusicScene(string Id,string Path,string Poster,double Duration);
public sealed record MusicScenePrepared(string Id,string PosterUrl);
public sealed class MusicSceneCache(HomePaths paths,MediaTools tools,IMusicSceneSource source):IHostedService
{
    private sealed record Manifest(int Format,string Id,string SourceUrl,string License,long VideoBytes,string VideoHash,long PosterBytes,string PosterHash,double Duration);
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly CancellationTokenSource _shutdown=new();
    private const long MaximumSource=128L*1024*1024,MaximumVideo=48L*1024*1024;
    private string Library=>AppFiles.DirectoryIn(paths.Data,"downloads","scene-library");
    public async Task<PreparedMusicScene> PrepareAsync(string id,CancellationToken token)
    {
        var scene=MusicSceneCatalog.Require(id);using var request=CancellationTokenSource.CreateLinkedTokenSource(token,_shutdown.Token);await _gate.WaitAsync(request.Token);string? work=null;
        try
        {
            var existing=await OpenAsync(id,token);if(existing is not null)return existing;
            if(!tools.FfmpegAvailable)throw new DomainException("Görsel sahneyi hazırlamak için FFmpeg gerekli.",409);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(request.Token);timeout.CancelAfter(TimeSpan.FromMinutes(4));
            work=AppFiles.DirectoryIn(paths.Data,"downloads","staging","music-scenes",Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(Path.Combine(work,".home-scene"),"Home.Scene.v1",timeout.Token);
            var input=Path.Combine(work,"source.webm");
            using(var response=await source.GetAsync(scene.Url,timeout.Token))
            {
                if(!response.IsSuccessStatusCode)throw new DomainException("Görsel kaynağa ulaşılamadı. Radyo çalmaya devam edebilir.",502);
                if(response.Content.Headers.ContentLength>MaximumSource)throw new DomainException("Görsel kaynak izin verilen boyutu aşıyor.",413);
                await using var body=await response.Content.ReadAsStreamAsync(timeout.Token);await using var output=new FileStream(input,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);
                var buffer=new byte[65536];long size=0;int count;
                while((count=await body.ReadAsync(buffer,timeout.Token))>0){size+=count;if(size>MaximumSource)throw new DomainException("Görsel kaynak izin verilen boyutu aşıyor.",413);await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);}
            }
            var video=Path.Combine(work,"scene.mp4");var poster=Path.Combine(work,"poster.jpg");
            await OwnedNativeProcess.RunAsync(work,tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-protocol_whitelist","file,pipe","-format_whitelist",MediaProcess.MediaFormats,
                "-i",input,"-t","30","-map","0:v:0","-an","-sn","-dn","-map_metadata","-1","-vf","scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=24",
                "-c:v","libx264","-threads","2","-preset","veryfast","-profile:v","baseline","-pix_fmt","yuv420p","-crf","24","-maxrate","3000k","-bufsize","6000k","-g","48","-keyint_min","48","-sc_threshold","0","-movflags","+faststart","-fs",MaximumVideo.ToString(System.Globalization.CultureInfo.InvariantCulture),video],TimeSpan.FromMinutes(3),timeout.Token,workingDirectory:work);
            var probe=await tools.ProbeAsync(video,timeout.Token);
            if(probe?.Duration is not >=1 or >31||probe.VideoCodec!="h264"||probe.Width is not >0 or >1280||probe.Height is not >0 or >720||probe.Streams?.Any(stream=>stream.Kind=="audio")==true)
                throw new DomainException("Görsel sahnenin sessiz video çıktısı doğrulanamadı.",422);
            await OwnedNativeProcess.RunAsync(work,tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-i",video,"-frames:v","1","-threads","1","-q:v","3",poster],TimeSpan.FromSeconds(20),timeout.Token,workingDirectory:work);
            var manifest=new Manifest(1,id,scene.Url,scene.License,new FileInfo(video).Length,await VerifiedFilePublication.HashAsync(video,timeout.Token),new FileInfo(poster).Length,await VerifiedFilePublication.HashAsync(poster,timeout.Token),probe.Duration.Value);
            PackageTrust.AtomicJson(Path.Combine(work,"manifest.json"),manifest);File.Delete(input);timeout.Token.ThrowIfCancellationRequested();
            var target=Path.Combine(Library,id);if(Directory.Exists(target))throw new DomainException("Görsel önbelleği değişti; yeniden dene.",409);
            Directory.Move(work,target);work=null;return new(id,Path.Combine(target,"scene.mp4"),Path.Combine(target,"poster.jpg"),probe.Duration.Value);
        }
        finally{try{if(work is not null&&Directory.Exists(work))AppFiles.DeleteOwnedDirectory(work);}finally{_gate.Release();}}
    }
    public async Task<PreparedMusicScene?> OpenAsync(string id,CancellationToken token)
    {
        var scene=MusicSceneCatalog.Require(id);var root=Path.Combine(Library,id);if(!Directory.Exists(root))return null;AppFiles.RejectLink(root);
        var file=MediaPaths.ResolveFile(root,"manifest.json");if(new FileInfo(file).Length>8192)throw new DomainException("Görsel önbelleği geçersiz.",409);
        var manifest=JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(file,token));
        var video=MediaPaths.ResolveFile(root,"scene.mp4");var poster=MediaPaths.ResolveFile(root,"poster.jpg");
        if(manifest is null||manifest.Format!=1||manifest.Id!=id||manifest.SourceUrl!=scene.Url||manifest.License!=scene.License||manifest.Duration is not >=1 or >31
            ||manifest.VideoBytes is <=0 or >MaximumVideo||manifest.PosterBytes is <=0 or >2*1024*1024
            ||new FileInfo(video).Length!=manifest.VideoBytes||new FileInfo(poster).Length!=manifest.PosterBytes
            ||await VerifiedFilePublication.HashAsync(video,token)!=manifest.VideoHash||await VerifiedFilePublication.HashAsync(poster,token)!=manifest.PosterHash)
            throw new DomainException("Görsel önbelleği doğrulanamadı.",409);
        return new(id,video,poster,manifest.Duration);
    }
    public async Task StartAsync(CancellationToken token)
    {
        var root=Path.Combine(paths.Data,"downloads","staging","music-scenes");if(!Directory.Exists(root))return;
        foreach(var directory in Directory.EnumerateDirectories(root))
        {
            var marker=Path.Combine(directory,".home-scene");if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _)||!File.Exists(marker))continue;
            AppFiles.RejectLink(directory);AppFiles.RejectLink(marker);if(File.ReadAllText(marker)!="Home.Scene.v1")continue;
            await OwnedNativeProcess.RecoverAsync(directory,token);AppFiles.DeleteOwnedDirectory(directory);
        }
    }
    public async Task StopAsync(CancellationToken token){_shutdown.Cancel();await _gate.WaitAsync(token);_gate.Release();}
}
