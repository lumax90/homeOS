using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.LiveTv;
public sealed class LivePlayback(MediaStore store,LiveSources sources,LiveInputRelay relay,HomePaths paths,ILiveProducer producer,EventHub events):BackgroundService
{
 private sealed class Running(string directory,CancellationTokenSource cancel){public string Directory{get;}=directory;public CancellationTokenSource Cancel{get;}=cancel;public CancellationToken Lifetime{get;}=cancel.Token;public Task? Task;}
  private readonly ConcurrentDictionary<string,Running> _running=new();private readonly CancellationTokenSource _shutdown=new();
  private readonly SemaphoreSlim _admission=new(1,1);
 public override Task StartAsync(CancellationToken token){store.RecoverLiveSessions();return base.StartAsync(token);}
 public async Task<LiveLaunch> StartAsync(string channelId,PlaybackActor actor,CancellationToken token,LivePlayInput? command=null)
 {
  if(!producer.Available)throw new DomainException("Canlı yayın için FFmpeg gerekli.",409);await _admission.WaitAsync(token);LiveSession? session=null;Running? running=null;LiveChannel? channel=null;
  try
  {
   token.ThrowIfCancellationRequested();var old=command?.ClientId is null?[]:_running.Keys.Select(store.GetLiveSession).OfType<LiveSession>().Where(s=>s.LoginId==actor.LoginId&&s.ClientId==command.ClientId).ToArray();session=store.AdmitLiveSession(channelId,actor,command?.ClientId,command?.ClientRevision??1);foreach(var previous in old)await CloseAsync(previous.Id,actor);
   channel=store.GetLiveChannel(channelId);var input=sources.Input(channel.SourceId);var directory=AppFiles.DirectoryIn(paths.Data,"live-cache",session.Id);running=new(directory,CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));_running[session.Id]=running;await File.WriteAllTextAsync(Path.Combine(directory,".home-live"),"Home.Live.v1",token);
   var link=relay.Register(sources.ChannelUrl(channelId),input.AllowLan,()=>store.RequireLiveSession(session.Id,actor),sources.ChannelHeaders(channelId));running.Task=Produce(running,session,channel,link);
  }
  catch{if(session is not null)await CloseAsync(session.Id,actor);throw;}
  finally{_admission.Release();}
  try
  {
   using var ready=CancellationTokenSource.CreateLinkedTokenSource(token,running!.Lifetime);ready.CancelAfter(TimeSpan.FromSeconds(45));while(!File.Exists(Path.Combine(running.Directory,"index.m3u8"))){if(running.Task!.IsCompleted)throw new DomainException(store.GetLiveSession(session!.Id)?.Error??"Canlı kaynak başlamadan sona erdi.",422);ready.Token.ThrowIfCancellationRequested();await Task.Delay(100,ready.Token);}ready.Token.ThrowIfCancellationRequested();_=store.RequireLiveSession(session!.Id,actor);store.SetLiveSession(session.Id,"active");store.MarkLiveWatched(session.Id,actor);events.Publish("live.changed");return new(session.Id,$"/api/live/sessions/{session.Id}/index.m3u8",MediaStore.PublicLiveChannel(channel!));
  }catch{await CloseAsync(session!.Id,actor);throw;}
  async Task Produce(Running active,LiveSession lease,LiveChannel selected,(string Token,string Url) link)
  {
   try
   {
    await producer.RunAsync(active.Directory,link.Url,selected.Radio,active.Cancel.Token);
   }
   catch(Exception){if(!active.Cancel.IsCancellationRequested)store.SetLiveSession(lease.Id,"failed","Canlı yayın kaynağı kesildi.");}
   finally{relay.Remove(link.Token);events.Publish("live.changed");}
  }
 }
 public async Task CloseClientAsync(string clientId,long revision,PlaybackActor actor){store.CloseLiveClient(actor,clientId,revision);foreach(var id in _running.Keys)if(store.GetLiveSession(id) is{} row&&row.LoginId==actor.LoginId&&row.ClientId==clientId&&row.ClientRevision<=revision)await CloseAsync(id,actor);}
 public object Touch(string id,PlaybackActor actor){var session=store.RequireLiveSession(id,actor,true);if(!_running.ContainsKey(id))throw new DomainException("Canlı oturum yeniden açılmalı.",410);return session;}
 public async Task CloseAsync(string id,PlaybackActor actor)
 {
  var session=store.GetLiveSession(id);if(session is null)return;if(session.UserId!=actor.UserId||session.LoginId!=actor.LoginId)throw new DomainException("Canlı oturum bulunamadı.",404);store.SetLiveSession(id,"closed");
  if(_running.TryRemove(id,out var running)){running.Cancel.Cancel();if(running.Task is not null)await running.Task;running.Cancel.Dispose();AppFiles.DeleteOwnedDirectory(running.Directory);}events.Publish("live.changed");
 }
 public IResult Artifact(string id,string file,PlaybackActor actor)
 {
  _=store.RequireLiveSession(id,actor,true);if(!_running.TryGetValue(id,out var running)||file!="index.m3u8"&&!(file.StartsWith("segment",StringComparison.Ordinal)&&file.EndsWith(".ts",StringComparison.Ordinal)&&file.Length==19&&file[7..16].All(char.IsAsciiDigit)))throw new DomainException("Canlı yayın parçası bulunamadı.",404);
  var path=MediaPaths.ResolveFile(running.Directory,file);return new LiveResult(path,file.EndsWith(".m3u8",StringComparison.Ordinal)?"application/vnd.apple.mpegurl":"video/mp2t",running.Cancel.Token);
 }
 private sealed class LiveResult(string path,string mime,CancellationToken lifetime):IResult
 {public async Task ExecuteAsync(HttpContext context){using var abort=lifetime.Register(context.Abort);lifetime.ThrowIfCancellationRequested();context.Response.Headers.CacheControl="no-store";await Results.Stream(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous),mime,enableRangeProcessing:true).ExecuteAsync(context);}}
 protected override async Task ExecuteAsync(CancellationToken token)
 {
  var root=Path.Combine(paths.Data,"live-cache");if(Directory.Exists(root))foreach(var directory in Directory.EnumerateDirectories(root))if(File.Exists(Path.Combine(directory,".home-live"))&&!_running.Values.Any(value=>value.Directory==directory)){await OwnedNativeProcess.RecoverAsync(directory,token);AppFiles.DeleteOwnedDirectory(directory);}
  while(!token.IsCancellationRequested)
  {
   foreach(var id in _running.Keys){var session=store.GetLiveSession(id);if(session is null)continue;try{var actor=new PlaybackActor(session.UserId,session.LoginId,session.DeviceId);_=store.RequireLiveSession(session.Id,actor);store.CheckTransientLimits(actor);}catch(DomainException){await CloseAsync(session.Id,new(session.UserId,session.LoginId,session.DeviceId));}}
   try{await Task.Delay(3000,token);}catch(OperationCanceledException){break;}
  }
 }
 public override async Task StopAsync(CancellationToken token){_shutdown.Cancel();foreach(var id in _running.Keys)if(store.GetLiveSession(id) is{} session)await CloseAsync(id,new(session.UserId,session.LoginId,session.DeviceId));await base.StopAsync(token);}
}
