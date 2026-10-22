using System.Globalization;
using System.Numerics;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using HomeServer.Subtitles;

namespace HomeServer.Playback;

public sealed class SegmentAnalysis(MediaStore store,HomePaths paths,MediaAnalysisService analysis,MediaTools tools,SubtitleService subtitles,EventHub events):IProductWorkHandler
{
 private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public string[] Kinds=>["segment-analysis"];
 public async Task<ProductJob> QueueAsync(string id,SegmentAnalysisInput options,Account actor,CancellationToken token)
 {
  if(!actor.IsAdmin)throw new DomainException("Dosya analizi yönetici gerektirir.",403);var media=await analysis.InspectAsync(id,token);if(media.Media.Kind!="video"||media.Probe.Duration is not>0 or >28800)throw new DomainException("Süresi okunmuş video gerekli.");
  if(media.Media.DurationSeconds is null)store.SaveProbe(id,media.Probe,media.Media.HasThumbnail);
  return store.QueueProductJob("segment-analysis",id,new SegmentAnalysisPlan(options,media.Fingerprint,media.Media.MatchRevision,store.GetMediaSegments(id,actor).Revision),actor);
 }
 public async Task RunAsync(ProductJob job,CancellationToken token)
 {
  var actor=store.RequireProductJob(job,true);var plan=job.Input.Deserialize<SegmentAnalysisPlan>(Json)!;var media=await analysis.InspectAsync(job.ScopeId!,token);if(media.Fingerprint!=plan.Fingerprint||media.Media.MatchRevision!=plan.MatchRevision)throw new DomainException("Analiz dosyası değişti.",409);
  var duration=media.Probe.Duration!.Value;var segments=new List<MediaSegment>();var evidence=new List<string>();var warnings=new List<string>();
  if(plan.Options.Chapters)foreach(var chapter in media.Probe.Chapters??[])
  {
   var name=chapter.Title.ToLowerInvariant();var kind=name.Contains("intro")||name.Contains("opening")||name.Contains("açılış")?"intro":name.Contains("recap")||name.Contains("previously")||name.Contains("önceki bölüm")?"recap":name.Contains("credits")||name.Contains("jenerik")||name.Contains("closing")?"credits":name.Contains("commercial")||name.Contains("advert")||name.Contains("reklam")?"commercial":null;
   if(kind is not null&&chapter.EndSeconds>chapter.StartSeconds&&chapter.EndSeconds<=duration)segments.Add(new(kind,chapter.StartSeconds,chapter.EndSeconds,"chapter",.99));
  }
  var directory=AppFiles.DirectoryIn(paths.Data,"segment-work",job.Id);await File.WriteAllTextAsync(Path.Combine(directory,".home-segments"),"Home.Segments.v1",token);
  try
  {
   if(plan.Options.SharedAudio&&media.Media.Content is{Kind:"show"}content)
   {
    var references=store.GetContentFiles(content.Id,actor).Where(file=>file.Id!=media.Media.Id&&file.Available&&file.Episodes?.Any(ep=>media.Media.Episodes?.Any(own=>own.EpisodeId==ep.EpisodeId)!=true)==true).DistinctBy(file=>file.Episodes!.First().EpisodeId).Take(3).ToArray();
    if(references.Length<2)warnings.Add("Tekrarlanan ses için en az iki farklı bölüm dosyası gerekli.");
    else
    {
     var head=await Features(media,0,Math.Min(600,duration*.3),directory,"target-head",token);var tail=plan.Options.Credits?await Features(media,Math.Max(0,duration-360),Math.Min(360,duration),directory,"target-tail",token):[];
     var heads=new List<(double Start,double End,double Score)>();var tails=new List<(double Start,double End,double Score)>();
     for(var index=0;index<references.Length;index++)
     {
      var other=await analysis.InspectAsync(references[index].Id,token);var length=other.Probe.Duration??0;if(length<=0)continue;
      var shared=Match(head,await Features(other,0,Math.Min(600,length*.3),directory,$"ref-{index}-head",token),token);if(shared is{}first)heads.Add(first);
      if(plan.Options.Credits){var sharedTail=Match(tail,await Features(other,Math.Max(0,length-360),Math.Min(360,length),directory,$"ref-{index}-tail",token),token);if(sharedTail is{}last)tails.Add((Math.Max(0,duration-360)+last.Start,Math.Max(0,duration-360)+last.End,last.Score));}
      store.ProductProgress(job,20+(index+1)*20,"Bölümler arasındaki tekrarlanan ses karşılaştırılıyor.");
     }
     AddConsensus("intro",heads);AddConsensus("credits",tails);
    }
   }
   if(plan.Options.Recap&&!segments.Any(segment=>segment.Kind=="recap"))
   {
    var track=subtitles.List(media).FirstOrDefault(track=>track.Playable);
    if(track is not null)
    {
     var cues=SubtitleTiming.Read(await subtitles.WebVttAsync(media,track.Id,0,0,token));var cue=cues.FirstOrDefault(cue=>cue.Start<120&&cue.Lines.Any(line=>line.Contains("previously on",StringComparison.OrdinalIgnoreCase)||line.Contains("önceki bölüm",StringComparison.OrdinalIgnoreCase)||line.Contains("last time on",StringComparison.OrdinalIgnoreCase)));
     if(cue is not null){var end=segments.Where(segment=>segment.Kind=="intro"&&segment.StartSeconds>cue.End).Select(segment=>segment.StartSeconds).DefaultIfEmpty(0).Min();if(end>cue.Start&&end-cue.Start<300)segments.Add(new("recap",cue.Start,end,"subtitle+intro",.75));else warnings.Add("Özet başlangıcı bulundu; bitiş sınırı elle belirlenmeli.");}
    }
   }
   _=store.RequireProductJob(job,true);var current=store.PlaybackSource(media.Media.Id,actor);if(current.Fingerprint!=plan.Fingerprint||current.Media.MatchRevision!=plan.MatchRevision)throw new DomainException("Dosya analiz sırasında değişti.",409);
   if(segments.Count==0)warnings.Add("Yeterli kanıtlı atlama aralığı bulunamadı; elle işaretleyebilirsin.");
   var proposal=new SegmentProposal(segments.OrderBy(segment=>segment.StartSeconds).ToArray(),evidence.ToArray(),warnings.ToArray(),plan.Fingerprint,plan.MatchRevision,plan.SegmentRevision);
   store.FinishProductJob(job,proposal,"proposed","Atlama işaretleri hazır; aralıkları incele.");events.Publish("product.changed");
   if(plan.Options.AutoApply&&segments.Count>0&&segments.All(segment=>segment.Confidence>=.9))Apply(job.Id,job.Revision,actor);
  }
  finally{await OwnedNativeProcess.RecoverAsync(directory,CancellationToken.None);if(File.Exists(Path.Combine(directory,".home-segments")))AppFiles.DeleteOwnedDirectory(directory);}
  void AddConsensus(string kind,List<(double Start,double End,double Score)> matches)
  {
   if(segments.Any(segment=>segment.Kind==kind))return;
   var agreed=matches.Where(candidate=>matches.Count(other=>Math.Abs(other.Start-candidate.Start)<5&&Math.Abs(other.End-candidate.End)<8)>=2).ToArray();if(agreed.Length<2)return;
   var start=agreed.Average(value=>value.Start);var end=agreed.Average(value=>value.End);segments.Add(new(kind,start,end,"shared-audio",Math.Clamp(agreed.Average(value=>value.Score),0,1)));evidence.Add($"{kind}: {agreed.Length} farklı bölümde ortak ses · {start:0.0}–{end:0.0} sn");
  }
 }
 public MediaSegmentSet Apply(string id,int revision,Account actor)
 {
  var saved=store.GetProductJob(id);if(saved?.Kind!="segment-analysis")throw new DomainException("Atlama analizi bulunamadı.",404);var job=store.ClaimProductApply(id,revision,actor);if(job.Status=="completed")return store.GetMediaSegments(job.ScopeId!,actor);
  try{_=store.RequireProductJob(job,true);var result=job.Result!.Value.Deserialize<SegmentProposal>(Json)!;var set=store.SaveMediaSegments(job.ScopeId!,new(result.SegmentRevision,result.MatchRevision,result.Fingerprint,result.Segments),actor);store.FinishProductJob(job,set);events.Publish("segments.changed");return set;}
  catch(Exception ex){store.FinishProductJob(job,null,"failed",ex is DomainException?ex.Message:"Atlama işaretleri kaydedilemedi.");throw;}
 }
 private sealed record Fingerprint(uint Shape,double Energy);
 private async Task<Fingerprint[]> Features(AnalyzedMedia media,double start,double duration,string directory,string key,CancellationToken token)
 {
  var audio=(media.Probe.Streams??[]).Where(stream=>stream.Kind=="audio").OrderByDescending(stream=>stream.IsDefault).FirstOrDefault();if(audio is null||duration<15)return[];
  var path=Path.Combine(directory,key+".pcm");if(File.Exists(path))File.Delete(path);
  await OwnedNativeProcess.RunAsync(directory,tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-threads","2","-protocol_whitelist","file,pipe","-format_whitelist",MediaProcess.MediaFormats,"-ss",start.ToString("0.###",CultureInfo.InvariantCulture),"-i",media.Path,"-map",$"0:{audio.Index}","-t",duration.ToString("0.###",CultureInfo.InvariantCulture),"-vn","-sn","-ac","1","-ar","8000","-c:a","pcm_f32le","-f","f32le",path],TimeSpan.FromMinutes(3),token);
  if(new FileInfo(path).Length>20*1024*1024)throw new DomainException("Ses işareti analiz sınırı aşıldı.");var bytes=await File.ReadAllBytesAsync(path,token);var result=new List<Fingerprint>();var previous=new double[16];
  for(var offset=0;offset+4000*4<=bytes.Length;offset+=4000*4)
  {
   token.ThrowIfCancellationRequested();var bands=new double[16];double energy=0;
   for(var band=0;band<16;band++)
   {
    var frequency=100*Math.Pow(1.22,band);var coefficient=2*Math.Cos(2*Math.PI*frequency/8000);double one=0,two=0;
    for(var sample=0;sample<4000;sample++){var value=BitConverter.ToSingle(bytes,offset+sample*4);if(!float.IsFinite(value))value=0;if(band==0)energy+=value*value;var next=value+coefficient*one-two;two=one;one=next;}
    bands[band]=Math.Log(1e-12+Math.Max(0,one*one+two*two-coefficient*one*two));
   }
   uint shape=0;for(var band=0;band<15;band++){if(bands[band]>bands[band+1])shape|=1u<<band;if(bands[band]-previous[band]>bands[band+1]-previous[band+1])shape|=1u<<(band+16);}result.Add(new(shape,energy/4000));previous=bands;
  }
  File.Delete(path);return result.ToArray();
 }
 private static (double Start,double End,double Score)? Match(Fingerprint[] target,Fingerprint[] reference,CancellationToken token)
 {
  var prior=new int[reference.Length+1];var best=0;var ending=0;double score=0;
  for(var i=0;i<target.Length;i++)
  {
   token.ThrowIfCancellationRequested();var row=new int[reference.Length+1];for(var j=0;j<reference.Length;j++)
   {var difference=BitOperations.PopCount(target[i].Shape^reference[j].Shape);if(target[i].Energy>1e-6&&reference[j].Energy>1e-6&&difference<=5){row[j+1]=prior[j]+1;if(row[j+1]>best){best=row[j+1];ending=i+1;score=1-difference/64d;}}}prior=row;
  }
  return best is>=30 and<=480?((ending-best)*.5,ending*.5,score):null;
 }
}
public static class SegmentEndpoints
{
 public static void MapSegments(this RouteGroupBuilder api)
 {
  api.MapGet("/media/{id}/segments",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.GetMediaSegments(id,actor.User)));
  api.MapPut("/media/{id}/segments",(string id,SegmentInput input,CurrentAccount actor,MediaStore store,EventHub events)=>{var result=store.SaveMediaSegments(id,input,actor.User);events.Publish("segments.changed");return Results.Ok(result);}).RequireAuthorization("admin");
  api.MapPost("/media/{id}/segments/analyze",async(string id,SegmentAnalysisInput input,CurrentAccount actor,SegmentAnalysis service,CancellationToken token)=>Results.Ok(await service.QueueAsync(id,input,actor.User,token))).RequireAuthorization("admin");
  api.MapPost("/segments/jobs/{id}/apply",(string id,ProductJobCommand input,CurrentAccount actor,SegmentAnalysis service)=>Results.Ok(service.Apply(id,input.Revision,actor.User))).RequireAuthorization("admin");
  api.MapGet("/player/preferences",(CurrentAccount actor,MediaStore store)=>Results.Ok(store.GetPlayerPreferences(actor.User.Id)));
  api.MapPut("/player/preferences",(PlayerPreferences input,CurrentAccount actor,MediaStore store)=>{store.SavePlayerPreferences(actor.User.Id,input);return Results.NoContent();});
 }
}
