using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.LiveTv;
public sealed class LiveSources(MediaStore store,ILiveHttp http,IDataProtectionProvider protection,EventHub events):IProductWorkHandler
{
 private readonly IDataProtector _protector=protection.CreateProtector("Home.LiveSources.v1");private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public string[] Kinds=>["live-source-sync"];
 public LiveSourceInput Input(string id)=>JsonSerializer.Deserialize<LiveSourceInput>(_protector.Unprotect(store.LiveSourceSecret(id)),Json)!;
  public string ChannelUrl(string id)=>_protector.Unprotect(store.LiveChannelSecret(id));
  public Dictionary<string,string>? ChannelHeaders(string id)=>store.LiveChannelHeadersSecret(id) is{} secret?JsonSerializer.Deserialize<Dictionary<string,string>>(_protector.Unprotect(secret),Json):null;
  public string? ChannelLogo(string id)=>store.LiveChannelLogoSecret(id) is{} secret?_protector.Unprotect(secret):store.GetLiveChannel(id).Logo;
  public object PublicSettings(string id){var input=Input(id);return new{input.AllowLan,playlistUrlConfigured=!string.IsNullOrWhiteSpace(input.PlaylistUrl),playlistFileConfigured=!string.IsNullOrWhiteSpace(input.PlaylistText),streamConfigured=!string.IsNullOrWhiteSpace(input.StreamUrl),epgConfigured=!string.IsNullOrWhiteSpace(input.EpgUrl),input.Group,input.Radio};}
 public LiveSource Save(string? id,LiveSourceInput input,Account actor,string? presetId=null)
 {
   var replace=input.PlaylistUrl is not null||input.PlaylistText is not null||input.StreamUrl is not null;
   if(id is not null){var prior=Input(id);if(!replace)input=input with{PlaylistUrl=prior.PlaylistUrl,PlaylistText=prior.PlaylistText,StreamUrl=prior.StreamUrl};input=input with{EpgUrl=input.ClearEpg?null:input.EpgUrl??prior.EpgUrl};}
   if(new[]{input.PlaylistUrl,input.PlaylistText,input.StreamUrl}.Count(value=>!string.IsNullOrWhiteSpace(value))!=1)throw new DomainException("Bir M3U listesi veya tek kanal bağlantısı seç.");
   input=input with{ClearEpg=false};
   if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>120||input.Name.Any(char.IsControl)||input.Tuners is<1 or>8||input.PlaylistText?.Length>8*1024*1024||input.Group is null||input.Group.Length>160||input.Group.Any(char.IsControl))throw new DomainException("Kanal kaynağının adını ve M3U adresi/dosyasını gir.");
  if(input.PlaylistUrl is{Length:>0}url)_=RemoteMediaHttp.Validate(url);if(input.StreamUrl is{Length:>0}stream)_=RemoteMediaHttp.Validate(stream);if(input.EpgUrl is{Length:>0}epg)_=RemoteMediaHttp.Validate(epg);if(input.PlaylistText is{} text)_=PlaylistGuide.Playlist(text);
  var result=store.SaveLiveSource(id,input,_protector.Protect(JsonSerializer.Serialize(input,Json)),actor,presetId,id is not null&&replace);events.Publish("live.changed");return result;
 }
  public ProductJob Queue(string id,Account actor){var source=store.GetLiveSource(id);if(!actor.IsAdmin)throw new DomainException("Kaynak yenileme yönetici gerektirir.",403);if(!source.Enabled)throw new DomainException("Önce TV kaynağını etkinleştir.",409);return store.QueueProductJob("live-source-sync",id,new{revision=source.Revision},actor);}
  public ProductJob? QueueWhenPossible(LiveSource source,Account actor){if(!source.Enabled)return null;try{return Queue(source.Id,actor);}catch(DomainException error)when(error.StatusCode==429){return null;}}
 public async Task RunAsync(ProductJob job,CancellationToken token)
 {
   _=store.RequireProductJob(job,true);var source=store.GetLiveSource(job.ScopeId!);if(!source.Enabled||source.Revision!=job.Input.GetProperty("revision").GetInt32())throw new DomainException("Kanal kaynağı değişti.",409);var input=Input(source.Id);
  try
  {
   ParsedChannel[] parsed;
   if(input.StreamUrl is{} stream)parsed=[new(PlaylistGuide.Id("Home.Live.Direct.v1"),input.Name,input.Group,null,null,stream,input.Radio,1)];
   else if(input.PlaylistText is{} text)parsed=PlaylistGuide.Playlist(text);
   else{var list=await LiveHttpRead.ReadAsync(http,input.PlaylistUrl!,input.AllowLan,8*1024*1024,token);parsed=PlaylistGuide.Playlist(Encoding.UTF8.GetString(list.Bytes),list.Uri);}
   _=store.RequireProductJob(job,true);var channels=store.SyncLiveChannels(source,parsed,_protector.Protect);store.ProductProgress(job,50,$"{parsed.Length} kanal okundu.");
   var programmes=0;if(input.EpgUrl is{Length:>0}epg){var guide=PlaylistGuide.Guide((await LiveHttpRead.ReadAsync(http,epg,input.AllowLan,32*1024*1024,token)).Bytes,channels);_=store.RequireProductJob(job,true);store.SaveLiveGuide(source,guide);programmes=guide.Length;}
   store.CompleteLiveSync(source,null);store.FinishProductJob(job,new{channels=channels.Count,programmes});events.Publish("live.changed");
  }catch(Exception ex){store.CompleteLiveSync(source,ex is DomainException?ex.Message:"Kanal/EPG kaynağı okunamadı.");throw;}
 }
}
public sealed class LiveSourceScheduler(MediaStore store,LiveSources service):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token){while(!token.IsCancellationRequested){foreach(var source in store.LiveSources().Where(source=>source.Enabled&&DateTimeOffset.Parse(source.NextSync)<=DateTimeOffset.UtcNow)){try{if(store.GetAccount(source.CreatedBy) is{Enabled:true,IsAdmin:true}actor)service.Queue(source.Id,actor);}catch(DomainException ex){store.CompleteLiveSync(source,ex.Message);}}try{await Task.Delay(60000,token);}catch(OperationCanceledException){break;}}}
}
