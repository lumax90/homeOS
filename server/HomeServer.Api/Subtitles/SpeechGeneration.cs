using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Subtitles;

public sealed record SpeechSettings(string? Executable=null,string? ModelPath=null,int Threads=2);
public sealed record SpeechInput(string Language="tr",int? AudioStream=null,bool TranslateToEnglish=false,bool AutoApply=false);
public sealed record SpeechPlan(SpeechInput Options,string Fingerprint,int MatchRevision,int AudioStream,string ModelFingerprint,string SettingsHash);
public sealed record SpeechResult(string Language,int Cues,string Engine,string Preview,string Fingerprint,int MatchRevision,string? SubtitleId=null);
public sealed class SpeechGeneration(MediaStore store,HomePaths paths,MediaTools tools,MediaAnalysisService analysis,SubtitleService subtitles,EventHub events):IProductWorkHandler
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public string[] Kinds=>["asr"];
    public SpeechSettings Settings=>store.GetSetting("speech.settings") is{}value?JsonSerializer.Deserialize<SpeechSettings>(value,Json)??new():new();
    private static string Hash(object value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));
    private static string FileStamp(string path){AppFiles.RejectLink(path);return MediaAnalysisService.Fingerprint(new FileInfo(path));}
    public object Capabilities(){var settings=Settings;return new{engine="whisper.cpp",configured=File.Exists(settings.Executable)&&File.Exists(settings.ModelPath),ffmpeg=tools.FfmpegAvailable,settings,modelName=settings.ModelPath is null?null:Path.GetFileName(settings.ModelPath),runtime="local-cpu"};}
    public void Save(SpeechSettings input)
    {
        if(input.Threads is <1 or >16||input.Executable?.Length>2000||input.ModelPath?.Length>2000)throw new DomainException("Konuşma modeli ayarları geçersiz.");
        foreach(var path in new[]{input.Executable,input.ModelPath}.Where(value=>!string.IsNullOrWhiteSpace(value)))if(!Path.IsPathFullyQualified(path!)||!File.Exists(path))throw new DomainException("Yerel araç ve model için var olan tam dosya yolunu seç.");
        store.SetSetting("speech.settings",JsonSerializer.Serialize(input,Json));
    }
    public async Task<ProductJob> QueueAsync(string id,SpeechInput input,Account actor,CancellationToken token)
    {
        if(!actor.IsAdmin)throw new DomainException("Konuşmadan altyazı üretimi yönetici gerektirir.",403);
        if(input.Language is null||input.Language.Length is <2 or >12||input.Language.Any(c=>!char.IsAsciiLetter(c)&&c!='-'))throw new DomainException("Konuşma dili geçersiz.");
        var media=await analysis.InspectAsync(id,token);if(store.GetMedia(id,actor) is null||media.Media.Kind!="video"||media.Probe.Duration is not >0 or >28800)throw new DomainException("En fazla sekiz saatlik erişilebilir video seç.");
        var settings=Settings;if(!File.Exists(settings.Executable)||!File.Exists(settings.ModelPath)||!tools.FfmpegAvailable)throw new DomainException("whisper.cpp aracı, yerel model ve FFmpeg ayarlarını tamamla.",409);
        var audio=(media.Probe.Streams??[]).Where(stream=>stream.Kind=="audio").OrderByDescending(stream=>stream.IsDefault).FirstOrDefault(stream=>input.AudioStream is null||stream.Index==input.AudioStream);if(audio is null)throw new DomainException("Ses izi bulunamadı.");
        return store.QueueProductJob("asr",id,new SpeechPlan(input,media.Fingerprint,media.Media.MatchRevision,audio.Index,FileStamp(settings.ModelPath!),Hash(settings)),actor);
    }
    private async Task<AnalyzedMedia> Source(ProductJob job,SpeechPlan plan,CancellationToken token)
    {
        _=store.RequireProductJob(job,true);var media=await analysis.InspectAsync(job.ScopeId!,token);var settings=Settings;
        if(media.Fingerprint!=plan.Fingerprint||media.Media.MatchRevision!=plan.MatchRevision||Hash(settings)!=plan.SettingsHash||!File.Exists(settings.ModelPath)||FileStamp(settings.ModelPath!)!=plan.ModelFingerprint)throw new DomainException("Kaynak dosya, eşleme veya model değişti; yeni üretim işi gerekli.",409);return media;
    }
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        var plan=job.Input.Deserialize<SpeechPlan>(Json)!;var media=await Source(job,plan,token);var existing=store.GetManagedSubtitles(media.Media.Id,true).FirstOrDefault(subtitle=>subtitle.Id==job.Id);
        if(existing is not null){store.FinishProductJob(job,new SpeechResult(existing.Language,0,"whisper.cpp","",plan.Fingerprint,plan.MatchRevision,existing.Id));return;}
        var directory=AppFiles.DirectoryIn(paths.Data,"speech-work",AppFiles.Id(job.Id));await File.WriteAllTextAsync(Path.Combine(directory,".home-speech"),Hash(plan),token);await OwnedNativeProcess.RecoverAsync(directory,token);
        var settings=Settings;var duration=media.Probe.Duration!.Value;var cues=new List<SubtitleCue>();var windows=(int)Math.Ceiling(duration/600);
        for(var index=0;index<windows;index++)
        {
            var start=Math.Max(0,index*600-3);var length=Math.Min(duration-start,606);var output=Path.Combine(directory,$"window-{index:000}");var vtt=output+".vtt";
            if(!File.Exists(vtt))
            {
                var wave=output+".wav";if(File.Exists(wave)){AppFiles.RejectLink(wave);File.Delete(wave);}
                await OwnedNativeProcess.RunAsync(directory,tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-threads","2","-protocol_whitelist","file,pipe","-format_whitelist",MediaProcess.MediaFormats,"-ss",start.ToString("0.###",CultureInfo.InvariantCulture),"-i",media.Path,"-map",$"0:{plan.AudioStream}","-t",length.ToString("0.###",CultureInfo.InvariantCulture),"-vn","-sn","-ar","16000","-ac","1","-c:a","pcm_s16le",wave],TimeSpan.FromMinutes(3),token);
                var arguments=new List<string>{"--model",settings.ModelPath!,"--file",wave,"--language",plan.Options.Language.ToLowerInvariant(),"--threads",settings.Threads.ToString(CultureInfo.InvariantCulture),"--no-gpu","--output-vtt","--output-file",output+".partial","--no-prints"};if(plan.Options.TranslateToEnglish)arguments.Add("--translate");
                try{await OwnedNativeProcess.RunAsync(directory,settings.Executable!,arguments,TimeSpan.FromMinutes(45),token);var generated=output+".partial.vtt";if(!File.Exists(generated)||new FileInfo(generated).Length>2*1024*1024)throw new DomainException("Konuşma aracı geçerli bir altyazı çıktısı üretmedi.",422);File.Move(generated,vtt,false);}
                finally{if(File.Exists(wave))File.Delete(wave);}
            }
            AppFiles.RejectLink(vtt);var text=await File.ReadAllTextAsync(vtt,token);
            foreach(var cue in SubtitleTiming.Read(text))
            {
                var absolute=cue with{Start=cue.Start+start,End=Math.Min(duration,cue.End+start)};var midpoint=(absolute.Start+absolute.End)/2;
                if(midpoint>=index*600&&midpoint<Math.Min(duration,(index+1)*600)&&absolute.End>absolute.Start)cues.Add(absolute);
            }
            _=await Source(job,plan,token);store.ProductProgress(job,(index+1)*95/windows,$"Konuşma üretimi: {index+1}/{windows} ses penceresi.");
        }
        if(cues.Count==0)throw new DomainException("Bu ses izinde yazıya çevrilebilen konuşma bulunamadı.",422);
        var resultText=SubtitleTiming.Transform(cues,0,1);if(Encoding.UTF8.GetByteCount(resultText)>2*1024*1024)throw new DomainException("Üretilen altyazı boyut sınırını aşıyor.",413);
        var final=Path.Combine(directory,"result.vtt");await File.WriteAllTextAsync(final,resultText,new UTF8Encoding(false),token);
        var result=new SpeechResult(plan.Options.TranslateToEnglish?"en":plan.Options.Language=="auto"?"und":plan.Options.Language,cues.Count,"whisper.cpp",string.Join('\n',cues.Take(5).SelectMany(cue=>cue.Lines)),plan.Fingerprint,plan.MatchRevision);
        store.FinishProductJob(job,result,"proposed","Konuşmadan altyazı hazır; ilk satırları inceleyip sürümü ekle.");events.Publish("subtitles.changed");
        if(plan.Options.AutoApply)await ApplyAsync(job.Id,job.Revision,store.GetAccount(job.UserId)!,token);
    }
    public async Task<ManagedSubtitle> ApplyAsync(string id,int revision,Account actor,CancellationToken token)
    {
        if(!actor.IsAdmin)throw new DomainException("Altyazı üretimi yönetici gerektirir.",403);if(store.GetProductJob(id)?.Kind!="asr")throw new DomainException("Konuşma üretimi bulunamadı.",404);var job=store.ClaimProductApply(id,revision,actor);
        if(job.Kind!="asr")throw new DomainException("İş bir konuşma üretimi değil.");if(job.Status=="completed")return store.GetManagedSubtitles(job.ScopeId!,true).FirstOrDefault(subtitle=>subtitle.Id==job.Id)??throw new DomainException("Altyazı sürümü daha sonra kaldırıldı.",409);
        try
        {
            var plan=job.Input.Deserialize<SpeechPlan>(Json)!;var media=await Source(job,plan,token);var result=job.Result!.Value.Deserialize<SpeechResult>(Json)!;
            var path=MediaPaths.ResolveFile(AppFiles.DirectoryIn(paths.Data,"speech-work",job.Id),"result.vtt");if(new FileInfo(path).Length>2*1024*1024)throw new DomainException("Altyazı çıktısı değişti.",409);
            var version=await subtitles.UploadAsync(media.Media.Id,result.Language,"Konuşmadan üretildi · "+result.Language,"vtt",await File.ReadAllBytesAsync(path,token),token,job.Id,expectedMatchRevision:plan.MatchRevision,publication:new("generated",plan.Fingerprint,plan.MatchRevision,JobId:job.Id,JobRevision:job.Revision));
            store.FinishProductJob(job,result with{SubtitleId=version.Id});events.Publish("subtitles.changed");return version;
        }
        catch(Exception ex){store.FinishProductJob(job,null,"failed",ex is DomainException?ex.Message:"Üretilen altyazı yayımlanamadı.");throw;}
    }
}
public static class SpeechEndpoints
{
    public static void MapSpeech(this RouteGroupBuilder api)
    {
        var group=api.MapGroup("/speech").RequireAuthorization("admin");group.MapGet("/settings",(SpeechGeneration service)=>Results.Ok(service.Capabilities()));
        group.MapPut("/settings",(SpeechSettings input,SpeechGeneration service)=>{service.Save(input);return Results.Ok(service.Capabilities());});
        group.MapPost("/media/{id}",async(string id,SpeechInput input,CurrentAccount actor,SpeechGeneration service,CancellationToken token)=>Results.Ok(await service.QueueAsync(id,input,actor.User,token)));
        group.MapPost("/jobs/{id}/apply",async(string id,ProductJobCommand input,CurrentAccount actor,SpeechGeneration service,CancellationToken token)=>Results.Ok(await service.ApplyAsync(id,input.Revision,actor.User,token)));
    }
}
