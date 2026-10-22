using System.Diagnostics;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace HomeServer.Operations;
public sealed class AdvancedOperations(MediaStore store,HomePaths paths,EventHub events):IProductWorkHandler
{
 public string[] Kinds=>["maintenance"];
 public MaintenancePreferences Preferences=>store.GetSetting("maintenance.preferences") is{}json?JsonSerializer.Deserialize<MaintenancePreferences>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))??new():new();
 public void SavePreferences(MaintenancePreferences value){if(value.IntervalHours is<1 or>720||value.CacheDays is<1 or>365)throw new DomainException("Bakım aralığı geçersiz.");store.SetSetting("maintenance.preferences",JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));}
 public ProductJob Queue(MaintenanceInput input,Account actor){if(!actor.IsAdmin||input.Action is not("integrity" or"compact" or"cache"))throw new DomainException("Bakım işlemi için yönetici ve geçerli işlem gerekli.",403);return store.QueueProductJob("maintenance",input.Action,input,actor);}
 public Task RunAsync(ProductJob job,CancellationToken token)
 {
  _=store.RequireProductJob(job,true);var action=job.Input.GetProperty("action").GetString();string message;
  if(action is"integrity" or"compact")message=store.DatabaseMaintenance(action);
  else
  {
   var removed=0;foreach(var name in new[]{"speech-work","segment-work"})
   {
    var root=Path.Combine(paths.Data,name);if(!Directory.Exists(root))continue;AppFiles.RejectLink(root);
    foreach(var directory in Directory.EnumerateDirectories(root))
    {
     token.ThrowIfCancellationRequested();var id=Path.GetFileName(directory);if(!Guid.TryParseExact(id,"N",out _))continue;var existing=store.GetProductJob(id);if(existing?.Status is"queued" or"running" or"applying" or"proposed"||Directory.GetLastWriteTimeUtc(directory)>DateTime.UtcNow.AddDays(-Preferences.CacheDays))continue;
     AppFiles.RejectLink(directory);var marker=Path.Combine(directory,name=="speech-work"?".home-speech":".home-segments");if(!File.Exists(marker))continue;AppFiles.RejectLink(marker);AppFiles.DeleteOwnedDirectory(directory);removed++;
    }
   }message=$"{removed} sona ermiş, Home'a ait analiz önbelleği temizlendi.";
  }
  store.FinishProductJob(job,new{message},message:message);events.Publish("system.changed");return Task.CompletedTask;
 }
}
public sealed class OperationSampler(MediaStore store,HomePaths paths,AdvancedOperations maintenance,ILogger<OperationSampler> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token)
 {
  using var process=Process.GetCurrentProcess();var previousCpu=process.TotalProcessorTime;var previous=DateTimeOffset.UtcNow;var nextMaintenance=DateTimeOffset.UtcNow.AddHours(1);
  while(!token.IsCancellationRequested)
  {
   try
   {
    process.Refresh();var now=DateTimeOffset.UtcNow;var cpu=process.TotalProcessorTime;var elapsed=(now-previous).TotalSeconds;var percent=elapsed>0?(cpu-previousCpu).TotalSeconds/elapsed/Environment.ProcessorCount*100:0;previous=now;previousCpu=cpu;long? free=null,total=null;
    try{var disk=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(paths.Data))!);free=disk.AvailableFreeSpace;total=disk.TotalSize;}catch(IOException){}
    var counts=store.OperationCounts();store.RecordOperationMetric(new(now.ToString("O"),Math.Clamp(percent,0,100),process.WorkingSet64,process.Threads.Count,free,total,store.GetDownloads().Count(download=>download.Status is"downloading" or"importing"),store.ActivePlaybackLeases().Count+store.ActiveLiveSessions().Count+store.ActivePodcastSessions().Count,counts.Jobs,counts.DatabaseBytes));
    if(now>=nextMaintenance){var prefs=maintenance.Preferences;nextMaintenance=now.AddHours(prefs.IntervalHours);if(prefs.Enabled&&store.GetAccount("1") is{Enabled:true,IsAdmin:true}owner)maintenance.Queue(new("cache"),owner);}
   }catch(Exception ex){logger.LogWarning("Ölçüm örneği alınamadı: {Type}",ex.GetType().Name);}
   try{await Task.Delay(30000,token);}catch(OperationCanceledException){break;}
  }
 }
}
public static class AdvancedOperationsEndpoints
{
 public static void MapAdvancedOperations(this RouteGroupBuilder api)
 {
  var group=api.MapGroup("/operations").RequireAuthorization("admin");
  group.MapGet("/metrics",(int? hours,MediaStore store)=>Results.Ok(store.OperationMetrics(hours??24)));
  group.MapGet("/logs",(long? after,string? level,string? q,int? limit,OperationJournal journal)=>Results.Ok(new{items=journal.Read(after??0,level,q,limit??100),dropped=journal.Dropped}));
  group.MapGet("/maintenance",(AdvancedOperations service)=>Results.Ok(service.Preferences));group.MapPut("/maintenance",(MaintenancePreferences input,AdvancedOperations service)=>{service.SavePreferences(input);return Results.NoContent();});
  group.MapPost("/maintenance",(MaintenanceInput input,CurrentAccount actor,AdvancedOperations service)=>Results.Ok(service.Queue(input,actor.User)));
  group.MapGet("/configuration",(MediaStore store)=>Results.File(JsonSerializer.SerializeToUtf8Bytes(store.ExportConfiguration(),new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}),"application/json","home-configuration.json"));
  group.MapPost("/configuration/preview",async(HttpContext context,MediaStore store,CancellationToken token)=>{var bundle=await Read<ConfigurationBundle>(context,token);store.ValidateConfiguration(bundle);return Results.Ok(new{currentHash=MediaStore.ConfigurationHash(store.ExportConfiguration()),keys=bundle.Settings.Keys,profiles=bundle.Profiles.Select(profile=>profile.Name)});});
  group.MapPost("/configuration/apply",async(HttpContext context,MediaStore store,EventHub events,CancellationToken token)=>{var input=await Read<ConfigurationApply>(context,token);var result=store.ApplyConfiguration(input);events.Publish("system.changed");return Results.Ok(result);});
 }
 private static async Task<T> Read<T>(HttpContext context,CancellationToken token){if(context.Features.Get<IHttpMaxRequestBodySizeFeature>() is{IsReadOnly:false}feature)feature.MaxRequestBodySize=4*1024*1024;return await context.Request.ReadFromJsonAsync<T>(token)??throw new DomainException("Yapılandırma gerekli.");}
}
