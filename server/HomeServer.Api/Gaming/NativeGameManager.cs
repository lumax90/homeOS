using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeGameLaunch(string Id,string Url,string Version,bool HasSave,bool LocalOnly,long InputEpoch=0,string[]? Discs=null,int DiscIndex=0,bool StateSupported=true,string StreamingBackend="standard");
public sealed record NativeGameStart(string ClientId,string Ticket,string StreamingBackend="standard");
public sealed record NativeGameCommand(string Action);
public sealed record NativeGameInput(long Sequence,ushort Buttons,long InputEpoch=0,short[]? Axes=null,NativePointer? Pointer=null,ushort[]? KeyCodes=null,short[]? Pressure=null,NativeAuxiliaryPad[]? Ports=null);
public sealed record NativeAuxiliaryPad(ushort Buttons,short[]? Axes=null,short[]? Pressure=null);
public sealed record NativeGameReceipt(bool Paused,bool HasSave,long InputEpoch,int DiscIndex=0);
public sealed record NativeClosingCheckpoint(string User,string Game,string Build,int Revision,bool StateSupported);

/// <summary>Headless server runner. Upstream engines own emulation/encoding/WebRTC; Home owns each device lease.</summary>
public sealed class NativeGameManager(MediaStore store, HomePaths paths, NativeRomLibrary roms, NativeSaveSnapshots saves,
    EventHub events,ILogger<NativeGameManager> logger,NativeCoreRegistry? cores=null,NativeFirmwareLibrary? firmware=null,NativeCoreOptions? coreOptions=null,NativeGameSettings? gameSettings=null,NativeStreamNetwork? network=null,IConfiguration? configuration=null,NativeSelkiesRuntime? selkiesRuntime=null) : BackgroundService
{
    private readonly NativeGameSettings _settings=gameSettings??new(paths);
    private sealed class Active(Account owner, string login, GameManifest game, GameSession session, string directory,
        Process engine, Process media, HttpClient http, RetroArchControl control, UdpClient pad, FileStream hostLock, int revision)
    {
        public Account Owner = owner; public string Login = login; public GameManifest Game = game; public GameSession Session = session;
        public string Directory = directory; public Process Engine = engine, Media = media; public HttpClient Http = http;
        public RetroArchControl Control = control; public UdpClient Pad = pad; public FileStream HostLock = hostLock;
        public bool Paused = true; public long InputSequence = -1, InputEpoch; public int Applied; public int SaveRevision = revision;
        public NativeInputBuffer Input = new();
        public NativePadFrame AppliedFrame;
        public UdpClient[] OtherPads=[];public NativeInputBuffer[] OtherInputs=[new(),new(),new()];public NativePadFrame[] OtherApplied=new NativePadFrame[3];public DateTimeOffset[] OtherNotBefore=new DateTimeOffset[3];public int InputPort=62401;
        public DateTimeOffset InputNotBefore;
        public double FrameMilliseconds=1000d/60+1;
        public string[] Discs=[];public int DiscIndex;
        public string CoreId="mgba",CoreHash="";public NativeCoreOption[]? Options;
        public NativeGpuProcess? GpuProcess;public NativeLinuxDisplay? LinuxDisplay;public bool ModernHost;public string Executable="";public bool LocalSockets;public bool StateSupported=true;
        public NativeSelkiesSession? Selkies;
        public int InputStreamConnected;
        public int InputProtected = 1; public long PolicyCheckedTicks = DateTime.UtcNow.Ticks;
        public DateTimeOffset LastSeen = DateTimeOffset.UtcNow, LastInput = DateTimeOffset.UtcNow;
        public Task<string>? EngineOut, EngineError, MediaOut, MediaError;
        public HashSet<string> Readers = new(StringComparer.Ordinal);
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _inputReady = new(0, 1);
    private string[] _recoveryDirectories=[];
    private readonly TaskCompletionSource _recoveryReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile Active? _active;
    public bool ContainerUpdateBusy => _active is not null || _gate.CurrentCount == 0 || _recoveryDirectories.Length > 0 && !_recoveryReady.Task.IsCompletedSuccessfully;
    public override async Task StartAsync(CancellationToken token)
    {
        var root = Path.Combine(paths.State, "games", "native-work");
        var pendingRecovery=new List<string>();
        if (Directory.Exists(root))
        {
            AppFiles.RejectLink(root);
            foreach (var directory in Directory.EnumerateDirectories(root).Take(1000))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
                foreach (var name in new[] { "engine", "media", "selkies", "sound", "display" }) { var child = Path.Combine(directory,name); if (Directory.Exists(child)) await OwnedNativeProcess.RecoverAsync(child,token); }
                if(File.Exists(Path.Combine(directory,"closing-checkpoint.json"))||File.Exists(Path.Combine(directory,"active-checkpoint.json")))pendingRecovery.Add(directory);
                else CleanupWork(directory);
            }
        }
        foreach(var session in store.GetActiveGameSessions().Where(session=>session.GameVersion.StartsWith("native-mgba-",StringComparison.Ordinal)||session.GameVersion.StartsWith("native-core-",StringComparison.Ordinal)))
        { var actor = store.GetAccount(session.UserId); if (actor is not null) store.CloseGameSession(session.Id,actor,session.LoginId); }
        _recoveryDirectories=pendingRecovery.ToArray();await base.StartAsync(token);
    }
    private async Task RecoverCheckpointAsync(string directory,CancellationToken token)
    {
        var isClosing=File.Exists(Path.Combine(directory,"closing-checkpoint.json"));var pending=MediaPaths.ResolveFile(directory,isClosing?"closing-checkpoint.json":"active-checkpoint.json");
        if(new FileInfo(pending).Length>4096)throw new DomainException("Yerel kayıt kurtarma tanımı geçersiz.",409);
        var closing=JsonSerializer.Deserialize<NativeClosingCheckpoint>(await File.ReadAllTextAsync(pending,token))??throw new DomainException("Yerel kayıt kurtarma tanımı geçersiz.",409);
        var owner=store.GetAccount(closing.User);if(owner is null||store.GetGameRom(closing.Game,owner) is null)return;
        if(store.GetGameCheckpoint(closing.Game,closing.Build,closing.User).Revision!=closing.Revision)return;
        var manifest=await saves.PublishAsync(closing.User,closing.Game,closing.Build,Path.Combine(directory,"engine","saves"),token,isClosing&&closing.StateSupported,!isClosing);
        using var data=JsonDocument.Parse(manifest);var empty=data.RootElement.TryGetProperty("files",out var recovered)&&recovered.GetArrayLength()==0;
        if(empty||store.RecoverNativeCheckpoint(closing.User,closing.Game,closing.Build,closing.Revision,manifest))
        {
            foreach(var name in new[]{"active-checkpoint.json","closing-checkpoint.json"}){var journal=Path.Combine(directory,name);if(File.Exists(journal)){AppFiles.RejectLink(journal);File.Delete(journal);}}
            CleanupWork(directory);
        }
    }
    public async Task<NativeGameLaunch> LaunchAsync(string gameId, GameStart input, Account actor, string login, CancellationToken token, string? requestedHost=null,string streamingBackend="standard",string? requestOrigin=null,string pathBase="")
    {
        if (!actor.IsAdmin) throw new DomainException("Sunucu emülasyonu sahip hesabını gerektirir.",403);
        streamingBackend=NativeSelkiesRuntime.Backend(streamingBackend);
        AppFiles.Id(input.ClientId);if(input.Ticket is not{Length:64}||input.Ticket.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Oyun oturumu bileti geçersiz.");
        var advertisedHosts=network?.Hosts(requestedHost)??["127.0.0.1"];
        var rom = store.GetGameRom(gameId,actor) ?? throw new DomainException("ROM bu profile ait değil veya bulunamadı.",404);
        if(_recoveryDirectories.Length>0)await _recoveryReady.Task.WaitAsync(token);
        await _gate.WaitAsync(token);
        try
        {
            var resolution=cores?.Resolve(rom);var profile=resolution?.Host??NativeRuntimeProfile.Read(paths)??throw new DomainException("Yerel oyun bileşenleri henüz etkinleştirilmedi.",409);
            if(resolution is null){if(rom.System!="gba")throw new DomainException("Bu oyun için emülasyon çekirdeğini kur.",409);profile.Verify();}
            if (_active is { } existing)
            {
                if (existing.Session.Id == input.ClientId && existing.Owner.Id == actor.Id && existing.Login == login && store.GetGameRuntimeSession(input.ClientId,input.Ticket) is not null)
                {if(Launch(existing).StreamingBackend!=streamingBackend)throw new DomainException("Yayın yöntemi sonraki oyun oturumunda uygulanır.",409);return Launch(existing);}
                throw new DomainException("Sunucu başka bir cihazın oyun oturumu için kullanılıyor. Önce o cihazdaki oyundan çık.",409);
            }
            foreach (var port in new[] { 62401,62402,62403,62404,62405,62406 })
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(value=>value.Port==port) || IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(value=>value.Port==port))
                    throw new DomainException("Yerel oyun prototipinin bağlantı portu zaten kullanılıyor.",409);
            var hostRoot = AppFiles.DirectoryIn(paths.State,"games");
            var game=resolution?.Game(rom)??profile.Game(rom);var coreId=resolution?.Definition.Id??"mgba";var binary=resolution?.Binary??profile.Core;var coreInfo=resolution?.Info??profile.CoreInfo;
            NativeSelkiesInstallation? installation=null;
            if(streamingBackend=="selkies")installation=await (selkiesRuntime??throw new DomainException("Selkies paketi bu sunucuda kurulu değil.",409)).RequireAsync(profile,coreId,binary.Sha256,token);
            var hostLock = new FileStream(Path.Combine(hostRoot,"native-host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            GameSession? session=null;Active? active=null;Process? engine=null,media=null;HttpClient? http=null;NativeGpuProcess? gpuProcess=null;NativeLinuxDisplay? linuxDisplay=null;var frontend=resolution?.Gpu?.Frontend.Path??profile.RetroArch.Path;
            NativeSelkiesSession? selkies=null;
            RetroArchControl? startingControl=null;UdpClient? startingPad=null;Task<string>? startingOutput=null,startingError=null;
            try
            {
                session = store.StartGameSession(game,input,actor,login); var checkpoint = store.GetGameCheckpoint(game.Id,game.Version,actor.Id);
                var root = AppFiles.DirectoryIn(paths.State,"games","native-work",AppFiles.Id(session.Id));
                await File.WriteAllTextAsync(Path.Combine(root,".home-native-work"),"Home.Native.Work.v1",token);
                var engineRoot = AppFiles.DirectoryIn(root,"engine"); var mediaRoot = AppFiles.DirectoryIn(root,"media");
                var privateHost=profile.LoopbackOnly||resolution?.ModernHost==true;var graphics=privateHost&&resolution?.Definition.Hardware==true;var width=rom.System=="3ds"?400:640;var height=480;var inputPort=privateHost?62420:62401;
                if(privateHost&&IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint=>endpoint.Port>=62420&&endpoint.Port<=62423))throw new DomainException("Oyun kumanda bağlantısı zaten kullanılıyor.",409);
                var renderer=resolution is null?null:NativeGpuRuntime.Renderer(resolution.Definition);
                var engineConfiguration=installation is not null?RetroArchHostConfiguration.CreateSelkies(engineRoot,inputPort,62406,renderer,width,height):privateHost?RetroArchHostConfiguration.CreatePrivateHost(engineRoot,inputPort,62406,renderer,width,height):RetroArchHostConfiguration.Create(engineRoot,inputPort,62406);
                await File.WriteAllTextAsync(Path.Combine(engineRoot,"retroarch.cfg"),engineConfiguration+_settings.ControllerConfiguration(actor.Id,coreId,binary.Sha256,rom.Id),token);
                var options=NativeEmulationSettings.Read(paths);options=options with{Quality=_settings.Quality(actor.Id,coreId,binary.Sha256,rom.Id)??options.Quality};
                await File.WriteAllTextAsync(Path.Combine(engineRoot,"record.cfg"),graphics?NativeEmulationSettings.Recording(options,width,height)+RetroArchHostConfiguration.LowDelayMux:NativeEmulationSettings.Recording(options)+(profile.LoopbackOnly||resolution?.Gpu?.LoopbackOnly==true?RetroArchHostConfiguration.LowDelayMux:""),token);
                await File.WriteAllTextAsync(Path.Combine(engineRoot,"core-options.cfg"),(coreId=="mgba"?NativeEmulationSettings.CoreOptions(options):NativeCoreConfiguration.Options(coreId))+_settings.Configuration(actor.Id,coreId,binary.Sha256,rom.Id)+NativeCoreConfiguration.RequiredOptions(coreId),token);
                NativeCoreConfiguration.PrepareSaveDirectories(coreId,Path.Combine(engineRoot,"saves"));
                var content=await roms.StageContentAsync(rom,engineRoot,token,canonicalizeCueTracks:true);File.SetAttributes(Path.Combine(engineRoot,content),FileAttributes.ReadOnly);
                var coreFile=Path.Combine(engineRoot,"cores",coreId+"_libretro"+profile.CoreExtension);
                File.Copy(binary.Path,coreFile,false);File.Copy(coreInfo.Path,Path.Combine(engineRoot,"info",coreId+"_libretro.info"),false);
                var restored = await saves.RestoreAsync(checkpoint,actor.Id,game.Id,game.Version,Path.Combine(engineRoot,"saves"),token);
                content=await roms.PrepareWritableContentAsync(rom,engineRoot,content,token);
                if(resolution is not null&&firmware is not null)await firmware.StageAsync(resolution.Definition,rom,Path.Combine(engineRoot,"system"),token);
                var secret = Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"); var path = "game-"+session.Id;
                Task<string>? mediaOut=null,mediaError=null;
                if(installation is not null)
                {
                    frontend=installation.Frontend;
                    selkies=await NativeSelkiesSession.PrepareAsync(root,installation,pathBase+"/game-native/"+session.Id+"/selkies",requestOrigin??"http://localhost",input.Ticket,width,height,token);
                }
                else
                {
                await File.WriteAllTextAsync(Path.Combine(mediaRoot,"mediamtx.yml"),MediaMtxGameConfiguration.Create(path,secret,62402,62403,62404,62405,advertisedHosts),token);
                media = MediaProcess.Start(profile.MediaMtx.Path,[Path.Combine(mediaRoot,"mediamtx.yml")],workingDirectory:mediaRoot); await OwnedNativeProcess.RecordAsync(mediaRoot,media,token);
                http = new HttpClient(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(5)};
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes("home-native:"+secret)));
                mediaOut = MediaProcess.ReadErrorTailAsync(media.StandardOutput); mediaError = MediaProcess.ReadErrorTailAsync(media.StandardError);
                await WaitMediaAsync(http,media,path,false,token);
                }
                var arguments=selkies is not null?new[]{"--config",Path.Combine(engineRoot,"retroarch.cfg"),"-L",coreFile,Path.Combine(engineRoot,content)}:new[]{"--config",Path.Combine(engineRoot,"retroarch.cfg"),"-L",coreFile,"--record",RetroArchHostConfiguration.LocalMpegTs(62402),"--recordconfig",Path.Combine(engineRoot,"record.cfg"),Path.Combine(engineRoot,content)};
                if(configuration?.GetValue<bool>("Home:Games:Diagnostics")==true)arguments=["--verbose",..arguments];
                if(privateHost&&!profile.Bundled){gpuProcess=NativeGpuProcess.Start(frontend,arguments,engineRoot);engine=gpuProcess.Process;}
                else
                {
                    if(graphics&&selkies is null)linuxDisplay=await NativeLinuxDisplay.StartAsync(root,width,height,token);
                    engine=MediaProcess.Start(frontend,arguments,workingDirectory:engineRoot,environment:selkies?.Environment??linuxDisplay?.Environment);
                }
                await OwnedNativeProcess.RecordAsync(engineRoot,engine,token,frontend);
                var control=startingControl=new RetroArchControl(engine,62406);var pad=startingPad=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));pad.Connect(IPAddress.Loopback,inputPort);
                var engineOut=startingOutput=gpuProcess is null?MediaProcess.ReadErrorTailAsync(engine.StandardOutput):gpuProcess.ReadLogAfterExitAsync(gpuProcess.OutputLog);
                var engineError=startingError=gpuProcess is null?MediaProcess.ReadErrorTailAsync(engine.StandardError):gpuProcess.ReadLogAfterExitAsync(gpuProcess.ErrorLog);
                if(selkies is not null)
                {
                    await control.WaitReadyAsync(TimeSpan.FromSeconds(30),token);
                    var frameMilliseconds=await control.FrameMillisecondsAsync(token);
                    await selkies.StartAsync(advertisedHosts,width,height,1000/(frameMilliseconds-1),options.Quality,token);
                    media=selkies.Process;http=selkies.Http;mediaOut=selkies.Output;mediaError=selkies.Error;
                }
                active = new(actor,login,game,session,root,engine,media!,http!,control,pad,hostLock,checkpoint.Revision)
                {CoreId=coreId,CoreHash=binary.Sha256,GpuProcess=gpuProcess,LinuxDisplay=linuxDisplay,Selkies=selkies,ModernHost=privateHost,Executable=frontend,StateSupported=resolution?.Definition.SaveState??true,LocalSockets=profile.LoopbackOnly||resolution?.Gpu?.LoopbackOnly==true,EngineOut=engineOut,EngineError=engineError,MediaOut=mediaOut,MediaError=mediaError};
                active.InputPort=inputPort;if(privateHost){for(var i=1;i<4;i++){var extra=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));extra.Connect(IPAddress.Loopback,inputPort+i);active.OtherPads=[..active.OtherPads,extra];}}
                if(selkies is null){await WaitMediaAsync(http!,engine,path,true,token);await control.StatusAsync(token);await control.EnablePacedHeadlessModeAsync(token);}
                if(active.LocalSockets){active.FrameMilliseconds=await control.FrameMillisecondsAsync(token);active.StateSupported=await control.SupportsStateAsync(token);}
                if(active.LocalSockets){var disk=await control.DiskStatusAsync(token);active.Discs=Enumerable.Range(0,disk.Count).Select(index=>"Disc "+(index+1)).ToArray();var savedDisk=Path.Combine(engineRoot,"saves","home-disc-index.json");if(File.Exists(savedDisk)){var index=JsonSerializer.Deserialize<int>(await File.ReadAllTextAsync(savedDisk,token));if(index>=0&&index<disk.Count)await control.SetDiskAsync(index,token);}active.DiscIndex=(await control.DiskStatusAsync(token)).Index;}
                await control.SetPausedAsync(true,token);if(restored){await control.LoadStateAsync(token);await control.SetPausedAsync(true,token);}
                await RetroPadProtocol.ReleaseAllAsync(pad,token);HomeServer.Operations.PackageTrust.AtomicJson(Path.Combine(root,"active-checkpoint.json"),new NativeClosingCheckpoint(actor.Id,game.Id,game.Version,checkpoint.Revision,false));_active=active;events.Publish("games.changed");return Launch(active);
            }
            catch(Exception startupError)
            {
                var startupExit=engine is not null&&engine.HasExited?engine.ExitCode:(int?)null;
                logger.LogWarning(startupError,"Native {Core} startup command failed",coreId);
                if (engine is not null) MediaProcess.Kill(engine); if(media is not null)MediaProcess.Kill(media);
                if(active is not null){await DisposeAsync(active);if(active.EngineError is{} error)logger.LogWarning("Native core {Core} could not start: {Tail}",active.CoreId,await error);}
                else
                {
                    if(engine is not null)await engine.WaitForExitAsync(CancellationToken.None);
                    if(startingError is not null)logger.LogWarning("Native core {Core} startup failed (exit {Exit}): {Output} {Tail}",coreId,startupExit,startingOutput is null?"":await startingOutput,await startingError);
                    if(startingControl is not null)await startingControl.DisposeAsync();startingPad?.Dispose();
                    if(gpuProcess is not null)gpuProcess.Dispose();else engine?.Dispose();if(linuxDisplay is not null)await linuxDisplay.DisposeAsync();
                    if(selkies is not null){if(selkies.Error is{} error&&selkies.Process.HasExited)logger.LogWarning("Selkies startup failed: {Tail}",await error);await selkies.DisposeAsync();}
                    else{media?.Dispose();http?.Dispose();}hostLock.Dispose();
                }
                if(session is not null){store.CloseGameSession(session.Id,actor,login);CleanupWork(Path.Combine(paths.State,"games","native-work",session.Id));}throw;
            }
        }
        finally{_gate.Release();}
    }
    internal async Task ConfigureIdleRuntimeAsync(Func<CancellationToken, Task> change, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_active is not null) throw new DomainException("Emülasyon ayarını değiştirmeden önce oyundan çık.", 409);
            await change(token);
        }
        finally { _gate.Release(); }
    }
    private static NativeGameLaunch Launch(Active active)=>new(active.Session.Id,active.Selkies?.ClientUrl??"/game-native/"+active.Session.Id+"/whep",active.Game.Version,active.SaveRevision>0,false,active.InputEpoch,active.Discs,active.DiscIndex,active.StateSupported,active.Selkies is null?"standard":"selkies");
    public NativeSelkiesSession RequireSelkies(string id,Account actor,string login)
    {var current=Require(id,actor,login);return current.Selkies is{ } session&&!session.Lifetime.IsCancellationRequested?session:throw new DomainException("Selkies oyun oturumu sona erdi.",410);}
    private static async Task WaitMediaAsync(HttpClient http,Process process,string path,bool ready,CancellationToken token)
    {
        for(var attempt=0;attempt<100;attempt++)
        {
            if(process.HasExited)throw new DomainException("Yerel oyun bileşeni başlatılamadı.",502);
            try
            {
                using var response=await http.GetAsync(ready?"http://127.0.0.1:62404/v3/paths/get/"+path:"http://127.0.0.1:62404/v3/config/global/get",token);
                if(response.IsSuccessStatusCode){if(!ready)return;using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));if(doc.RootElement.GetProperty("ready").GetBoolean())return;}
            }
            catch(HttpRequestException){}
            await Task.Delay(50,token);
        }
        throw new DomainException("Yerel oyun yayın kaynağı hazır hâle gelmedi.",502);
    }
    private Active Require(string id,Account actor,string login,bool verifySession=true)
    {
        var current=_active;if(current is null||current.Session.Id!=id||current.Owner.Id!=actor.Id||current.Login!=login)throw new DomainException("Yerel oyun oturumu bu cihaza veya profile ait değil.",404);
        if(verifySession)store.RequireGameSession(id,actor,login);return current;
    }
    public void RequireInputStream(string id,string ticket,Account actor,string login)
    { _=Require(id,actor,login);if(store.GetGameRuntimeSession(id,ticket) is null)throw new DomainException("Oyun giriş bileti geçersiz.",401); }
    public void OpenInputStream(string id,string ticket,Account actor,string login)
    { RequireInputStream(id,ticket,actor,login);var current=Require(id,actor,login);if(Interlocked.CompareExchange(ref current.InputStreamConnected,1,0)!=0)throw new DomainException("Oyun giriş bağlantısı zaten açık.",409); }
    public void ReleaseInputStream(string id,Account actor,string login)
    {var current=_active;if(current is null||current.Session.Id!=id||current.Owner.Id!=actor.Id||current.Login!=login)return;Interlocked.Exchange(ref current.InputStreamConnected,0);lock(current){current.Input.Clear();foreach(var extra in current.OtherInputs)extra.Clear();current.LastInput=DateTimeOffset.MinValue;}WakeInput();}
    public void Input(string id,NativeGameInput input,Account actor,string login)
    {
        // The request/WS handshake authenticates the owner; the lease monitor handles expiry/revocation off the input path.
        var current=Require(id,actor,login,false);RetroPadProtocol.Validate(input.Buttons,current.Game.Version.StartsWith("native-mgba-",StringComparison.Ordinal)?RetroPadProtocol.GbaButtons:ushort.MaxValue);
        var frame=NativePadFrame.From(input.Buttons,input.Axes,input.Pointer,input.KeyCodes,input.Pressure);
        if(input.Ports?.Length>3)throw new DomainException("Oyun kumanda sayısı geçersiz.");var extraFrames=(input.Ports??[]).Select(pad=>NativePadFrame.From(pad.Buttons,pad.Axes,pressure:pad.Pressure)).ToArray();
        lock(current){if(input.InputEpoch!=current.InputEpoch)throw new DomainException("Yerel oyun giriş dönemi değişti.",409);if(input.Sequence<=current.InputSequence)return;current.InputSequence=input.Sequence;current.LastSeen=DateTimeOffset.UtcNow;current.LastInput=current.LastSeen;current.Input.StageFrame(current.Paused?default:frame,AllowedButtons(current));for(var i=0;i<current.OtherInputs.Length;i++)current.OtherInputs[i].StageFrame(!current.Paused&&i<extraFrames.Length?extraFrames[i]:default,ushort.MaxValue);}WakeInput();
    }
    private void WakeInput(){try{_inputReady.Release();}catch(SemaphoreFullException){}}
    public async Task<NativeCoreOption[]> GetOptionsAsync(string id,Account actor,string login,CancellationToken token)
    {await _gate.WaitAsync(token);try{var current=Require(id,actor,login);if(!current.LocalSockets)return [];current.Options??=coreOptions?.Decorate(await current.Control.CoreOptionsAsync(token))??await current.Control.CoreOptionsAsync(token);_settings.Observe(actor.Id,current.CoreId,current.CoreHash,current.Options,await current.Control.ControllersAsync(token));return current.Options;}finally{_gate.Release();}}
    public async Task<NativeCoreOption[]> SetOptionAsync(string id,NativeCoreOptionChange input,Account actor,string login,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current=Require(id,actor,login);if(!current.Paused)throw new DomainException("Çekirdek ayarını değiştirmek için oyunu duraklat.",409);
            var options=current.Options??throw new DomainException("Önce çekirdek ayarlarını yükle.",409);var option=options.FirstOrDefault(option=>option.Key==input.Key)??throw new DomainException("Çekirdek ayarı bulunamadı.",404);
            if(option.Managed)throw new DomainException("Bu çekirdek ayarını Home yönetir.",409);var value=Array.IndexOf(option.Values,input.Value);if(value<0)throw new DomainException("Çekirdek ayarı desteklenmiyor.");
            NativeCoreOptions.ValidateChoice(input.Key,input.Value);var restart=NativeCoreOptions.NeedsRestart(option);if(!restart)await current.Control.SetCoreOptionAsync(option.Index,value,token);
            var rom=store.GetGameRom(current.Game.Id,actor)??throw new DomainException("ROM bulunamadı.",404);_settings.Observe(actor.Id,current.CoreId,current.CoreHash,options,await current.Control.ControllersAsync(token));var view=_settings.View(rom,current.CoreId,current.CoreId,current.CoreHash);_settings.Save(rom,current.CoreId,current.CoreId,current.CoreHash,new("game",view.Revision,new(){{input.Key,input.Value}}));current.Options=options.Select(item=>item.Key==input.Key?item with{Value=input.Value,PendingRestart=restart}:item).ToArray();return current.Options;
        }
        finally{_gate.Release();}
    }
    public async Task<NativeControllerPort[]> ControllersAsync(string id,Account actor,string login,CancellationToken token)
    {await _gate.WaitAsync(token);try{var current=Require(id,actor,login);return current.LocalSockets?await current.Control.ControllersAsync(token):[];}finally{_gate.Release();}}
    public async Task<NativeControllerPort[]> SetControllerAsync(string id,NativeControllerChange input,Account actor,string login,CancellationToken token)
    {await _gate.WaitAsync(token);try{var current=Require(id,actor,login);if(!current.LocalSockets||!current.Paused)throw new DomainException("Kumanda seçimini duraklatma menüsünden değiştir.",409);var rom=store.GetGameRom(current.Game.Id,actor)??throw new DomainException("ROM bulunamadı.",404);_settings.Observe(actor.Id,current.CoreId,current.CoreHash,current.Options??await current.Control.CoreOptionsAsync(token),await current.Control.ControllersAsync(token));var view=_settings.View(rom,current.CoreId,current.CoreId,current.CoreHash);var changes=view.GameControllers.Where(p=>p.Port!=input.Port).Append(input).ToArray();_settings.Save(rom,current.CoreId,current.CoreId,current.CoreHash,new("game",view.Revision,Controllers:changes));await ReleaseAsync(current,token);ResetInput(current);await current.Control.SetControllerAsync(input.Port,input.Device,token);return await current.Control.ControllersAsync(token);}finally{_gate.Release();}}
    private (string Id,string Name,string Hash) SettingsCore(GameRom rom)
    {var id=cores?.Selection(rom).CoreId??NativeSystems.Require(rom.System).Core;var hash=id=="mgba"?NativeRuntimeProfile.Read(paths)?.Core.Sha256:cores?.Installed(id)?.Binary.Sha256;if(hash is null)throw new DomainException("Önce bu oyunun emülasyon çekirdeğini kur.",409);return(id,id=="mgba"?"mGBA 0.10.5":cores?.Name(id)??id,hash);}
    public async Task<NativeGameSettingsView> SettingsAsync(string gameId,Account actor,string login,CancellationToken token)
    {await _gate.WaitAsync(token);try{var rom=store.GetGameRom(gameId,actor)??throw new DomainException("ROM bulunamadı.",404);var core=SettingsCore(rom);if(_active is{} current&&current.Game.Id==gameId&&current.Owner.Id==actor.Id&&current.Login==login&&current.Paused&&current.LocalSockets){current.Options??=coreOptions?.Decorate(await current.Control.CoreOptionsAsync(token))??await current.Control.CoreOptionsAsync(token);_settings.Observe(actor.Id,current.CoreId,current.CoreHash,current.Options,await current.Control.ControllersAsync(token));}return _settings.View(rom,core.Id,core.Name,core.Hash,_active?.Game.Id==gameId&&_active.Options?.Any(o=>o.PendingRestart)==true) with{ServerQuality=NativeEmulationSettings.Read(paths).Quality};}finally{_gate.Release();}}
    public async Task<NativeGameSettingsView> SaveSettingsAsync(string gameId,NativeSettingsChange input,Account actor,string login,CancellationToken token)
    {
        await _gate.WaitAsync(token);try
        {
            if(!actor.IsAdmin)throw new DomainException("Oyun emülasyon ayarları sahip hesabını gerektirir.",403);store.RequireRomImportActor(actor,login);var rom=store.GetGameRom(gameId,actor)??throw new DomainException("ROM bulunamadı.",404);var core=SettingsCore(rom);
            if(_active is{} active&&(input.Scope=="core"||active.Game.Id==gameId&&(!active.Paused||active.Login!=login)))throw new DomainException(input.Scope=="core"?"Emülatör varsayılanını değiştirmeden önce oyundan çık.":"Oyun ayarlarını kendi duraklatılmış oturumundan değiştir.",409);
            var before=_settings.View(rom,core.Id,core.Name,core.Hash);var oldQuality=_settings.Quality(actor.Id,core.Id,core.Hash,rom.Id);var result=_settings.Save(rom,core.Id,core.Name,core.Hash,input);
            var oldControllers=input.Scope=="game"?before.GameControllers:before.CoreControllers;var newControllers=input.Scope=="game"?result.GameControllers:result.CoreControllers;
            var restart=input.Values?.Count>0||input.Reset&&before.Options.Any(o=>o.Source=="game")||!oldControllers.OrderBy(p=>p.Port).SequenceEqual(newControllers.OrderBy(p=>p.Port))||oldQuality!=_settings.Quality(actor.Id,core.Id,core.Hash,rom.Id);
            if(_active is{} current&&current.Game.Id==gameId&&current.Options is{} observed&&restart)current.Options=observed.Select(o=>o with{PendingRestart=true}).ToArray();events.Publish("games.changed");return result with{RestartRequired=_active?.Game.Id==gameId&&restart,ServerQuality=NativeEmulationSettings.Read(paths).Quality};
        }finally{_gate.Release();}
    }
    public async Task<NativeGameReceipt> CommandAsync(string id,string action,Account actor,string login,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(action is "end" or "discard"&&(_active is null||_active.Session.Id!=id)&&store.GetClosedNativeGameSession(id,actor,login) is{} ended)
                return new(true,store.GetGameCheckpoint(ended.GameId,ended.GameVersion,actor.Id).Revision>0,0);
            var current=Require(id,actor,login);current.LastSeen=DateTimeOffset.UtcNow;
            if(action=="heartbeat"){store.PulseGameSession(id,current.Game,new(!current.Paused),actor,login);return Receipt(current);}
            if(action is "pause" or "resume")
            {
                await ReleaseAsync(current,token);ResetInput(current);
                await current.Control.SetPausedAsync(action=="pause",token);current.Paused=action=="pause";current.InputEpoch++;return Receipt(current);
            }
            if(action=="save"){await SaveAsync(current,token);return Receipt(current);}
            if(action.StartsWith("disc:",StringComparison.Ordinal)&&int.TryParse(action[5..],out var selected)){if(selected<0||selected>=current.Discs.Length)throw new DomainException("Oyun disk seçimi geçersiz.");await ReleaseAsync(current,token);ResetInput(current);await current.Control.SetPausedAsync(true,token);current.Paused=true;current.InputEpoch++;await current.Control.SetDiskAsync(selected,token);current.DiscIndex=selected;return Receipt(current);}
            if(action is "end" or "discard"){await FinishAsync(current,action=="end",token);return Receipt(current);}
            throw new DomainException("Yerel oyun komutu geçersiz.");
        }
        finally{_gate.Release();}
    }
    private static NativeGameReceipt Receipt(Active current)=>new(current.Paused,current.SaveRevision>0,current.InputEpoch,current.DiscIndex);
    private async Task SaveAsync(Active current,CancellationToken token,bool closing=false)
    {
        using var pulseBudget=CancellationTokenSource.CreateLinkedTokenSource(token);var pulses=PulseDuringSave();
        try
        {
        var pending=Path.Combine(current.Directory,"closing-checkpoint.json");var freshState=current.Engine.HasExited&&File.Exists(pending)&&current.StateSupported;
        if(!current.Engine.HasExited){await ReleaseAsync(current,token);ResetInput(current);await current.Control.SetPausedAsync(true,token);current.Paused=true;current.InputEpoch++;}
        var directory=Path.Combine(current.Directory,"engine","saves");var state=Path.Combine(directory,"game.state");var before=File.Exists(state)?File.GetLastWriteTimeUtc(state):DateTime.MinValue;
        if(current.LocalSockets&&!current.Engine.HasExited)current.StateSupported=await current.Control.SupportsStateAsync(token);
        if(current.StateSupported&&!current.Engine.HasExited){await current.Control.SaveStateAsync(token);for(var i=0;i<100;i++){if(File.Exists(state)&&File.GetLastWriteTimeUtc(state)>before&&new FileInfo(state).Length>0)break;await Task.Delay(25,token);}if(!File.Exists(state)||File.GetLastWriteTimeUtc(state)<=before)throw new DomainException("Yerel oyun anlık kaydı doğrulanamadı.",409);freshState=true;}
        if(current.Game.Version.StartsWith("native-core-",StringComparison.Ordinal)&&!current.Engine.HasExited)await current.Control.FlushSaveFilesAsync(token);
        if(current.Discs.Length>0&&!current.Engine.HasExited){current.DiscIndex=(await current.Control.DiskStatusAsync(token)).Index;await File.WriteAllTextAsync(Path.Combine(directory,"home-disc-index.json"),JsonSerializer.Serialize(current.DiscIndex),token);}
        if(closing){store.RequireGameSession(current.Session.Id,current.Owner,current.Login);HomeServer.Operations.PackageTrust.AtomicJson(pending,new NativeClosingCheckpoint(current.Owner.Id,current.Game.Id,current.Game.Version,current.SaveRevision,freshState));if(!current.Engine.HasExited){await current.Control.QuitAsync(token);await current.Engine.WaitForExitAsync(token);}}
        var manifest=await saves.PublishAsync(current.Owner.Id,current.Game.Id,current.Game.Version,directory,token,freshState,!freshState);
        using(var data=JsonDocument.Parse(manifest))if(data.RootElement.TryGetProperty("files",out var files)&&files.GetArrayLength()==0){store.PulseGameSession(current.Session.Id,current.Game,new(false),current.Owner,current.Login);if(File.Exists(pending))File.Delete(pending);return;}
        var checkpoint=store.PulseGameSession(current.Session.Id,current.Game,new(false,current.SaveRevision,new(){["native-save.json"]=manifest}),current.Owner,current.Login);current.SaveRevision=checkpoint.Revision;
        var activeJournal=Path.Combine(current.Directory,"active-checkpoint.json");
        if(!closing)HomeServer.Operations.PackageTrust.AtomicJson(activeJournal,new NativeClosingCheckpoint(current.Owner.Id,current.Game.Id,current.Game.Version,current.SaveRevision,false));
        else if(File.Exists(activeJournal)){AppFiles.RejectLink(activeJournal);File.Delete(activeJournal);}
        if(File.Exists(pending)){AppFiles.RejectLink(pending);File.Delete(pending);}
        }
        finally{pulseBudget.Cancel();try{await pulses;}catch(OperationCanceledException){}}
        async Task PulseDuringSave()
        {while(!pulseBudget.IsCancellationRequested){await Task.Delay(5000,pulseBudget.Token);try{store.PulseGameSession(current.Session.Id,current.Game,new(false),current.Owner,current.Login);current.LastSeen=DateTimeOffset.UtcNow;}catch(DomainException){return;}}}
    }
    private async Task FinishAsync(Active current,bool save,CancellationToken token)
    {
        if(save)await SaveAsync(current,token,current.ModernHost);
        try{if(!current.Engine.HasExited){await current.Control.QuitAsync(token);await current.Engine.WaitForExitAsync(token);}}
        finally{foreach(var name in new[]{"active-checkpoint.json","closing-checkpoint.json"}){var journal=Path.Combine(current.Directory,name);if(File.Exists(journal)){AppFiles.RejectLink(journal);File.Delete(journal);}}store.CloseGameSession(current.Session.Id,current.Owner,current.Login);if(ReferenceEquals(_active,current))_active=null;await DisposeAsync(current);CleanupWork(current.Directory);events.Publish("games.changed");}
    }
    private static void ResetInput(Active current)
    {lock(current){current.Input.Clear();current.Applied=0;current.AppliedFrame=default;current.InputNotBefore=default;foreach(var input in current.OtherInputs)input.Clear();Array.Clear(current.OtherApplied);Array.Clear(current.OtherNotBefore);}}
    private static void CleanupWork(string directory)
    {
        // Legacy/unmarked folders and uncommitted recovery journals are never removed here.
        if(!Directory.Exists(directory)||!Guid.TryParseExact(Path.GetFileName(directory),"N",out _)||!File.Exists(Path.Combine(directory,".home-native-work"))||File.Exists(Path.Combine(directory,"active-checkpoint.json"))||File.Exists(Path.Combine(directory,"closing-checkpoint.json")))return;
        try
        {
            var marker=MediaPaths.ResolveFile(directory,".home-native-work");if(new FileInfo(marker).Length>=64||File.ReadAllText(marker)!="Home.Native.Work.v1")return;
            var files=new List<string>();var folders=new List<string>();
            void Inventory(string folder){AppFiles.RejectLink(folder);foreach(var item in Directory.EnumerateFileSystemEntries(folder)){AppFiles.RejectLink(item);if(Directory.Exists(item))Inventory(item);else if(item!=marker)files.Add(item);}folders.Add(folder);}
            Inventory(directory);
            foreach(var file in files){var attributes=File.GetAttributes(file);if((attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(file,attributes&~FileAttributes.ReadOnly);File.Delete(file);}
            foreach(var folder in folders.Where(folder=>folder!=directory))Directory.Delete(folder);File.Delete(marker);Directory.Delete(directory);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}catch(DomainException){}
    }
    private static ushort AllowedButtons(Active current)=>current.Game.Version.StartsWith("native-mgba-",StringComparison.Ordinal)?RetroPadProtocol.GbaButtons:ushort.MaxValue;
    private static async Task ReleaseAsync(Active current,CancellationToken token){foreach(var packet in RetroPadProtocol.ReleaseAll(AllowedButtons(current)))await current.Pad.SendAsync(packet,token);for(var index=0;index<2;index++)for(var axis=0;axis<2;axis++)await current.Pad.SendAsync(RetroPadProtocol.Analog(index,axis,0),token);if(current.ModernHost){foreach(var packet in RetroPadProtocol.AnalogChanges(current.AppliedFrame,default).Concat(RetroPadProtocol.PeripheralChanges(current.AppliedFrame,default,true)))await current.Pad.SendAsync(packet,token);for(var i=0;i<current.OtherPads.Length;i++)foreach(var packet in RetroPadProtocol.Changes(current.OtherApplied[i].Buttons,0,ushort.MaxValue).Concat(RetroPadProtocol.AnalogChanges(current.OtherApplied[i],default)))await current.OtherPads[i].SendAsync(packet,token);}}
    private async Task MonitorInputProtectionAsync(CancellationToken token)
    {
        // One bounded monitor per host. COM/firewall RPC never runs on the 20 ms input loop.
        while(!token.IsCancellationRequested)
        {
            var current=_active;
            if(current is not null)
            {
                var executable=current.Executable;
                var safe=false;
                try{safe=current.LocalSockets?LoopbackInputs(current.InputPort,current.OtherPads.Length+1):NativeLocalInputGuard.InspectPorts(executable,[62401,62406]);}
                catch(Exception error){logger.LogWarning(error,"Native input policy check failed closed");}
                Volatile.Write(ref current.InputProtected,safe?1:0);
                Interlocked.Exchange(ref current.PolicyCheckedTicks,DateTime.UtcNow.Ticks);
            }
            await Task.Delay(1000,token);
        }
    }
    private static bool LoopbackInputs(int first,int count){var listeners=IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(endpoint=>endpoint.Port==62406||endpoint.Port>=first&&endpoint.Port<first+count).ToArray();return listeners.Length==count+1&&listeners.All(endpoint=>IPAddress.IsLoopback(endpoint.Address));}
    private async Task DisposeAsync(Active current)
    {
        MediaProcess.Kill(current.Engine);MediaProcess.Kill(current.Media);await current.Engine.WaitForExitAsync(CancellationToken.None);await current.Media.WaitForExitAsync(CancellationToken.None);
        await current.Control.DisposeAsync();current.Pad.Dispose();foreach(var pad in current.OtherPads)pad.Dispose();current.Http.Dispose();current.HostLock.Dispose();
        foreach(var task in new[]{current.EngineOut,current.EngineError,current.MediaOut,current.MediaError})if(task is not null)await task;
        if(configuration?.GetValue<bool>("Home:Games:Diagnostics")==true)logger.LogInformation("Native {Core} session exit {Exit}: {Output} {Error}",current.CoreId,current.Engine.ExitCode,current.EngineOut is null?"":await current.EngineOut,current.EngineError is null?"":await current.EngineError);
        if(current.Selkies is not null&&configuration?.GetValue<bool>("Home:Games:Diagnostics")==true)logger.LogInformation("Selkies session exit: {Output} {Error}",current.MediaOut is null?"":await current.MediaOut,current.MediaError is null?"":await current.MediaError);
        if(current.GpuProcess is not null)current.GpuProcess.Dispose();else current.Engine.Dispose();if(current.Selkies is not null)await current.Selkies.DisposeAsync();else current.Media.Dispose();
        if(current.LinuxDisplay is not null)await current.LinuxDisplay.DisposeAsync();
    }
    public async Task<HttpResponseMessage> WhepAsync(string id,string ticket,string method,string? reader,byte[] body,string? contentType,string? ifMatch,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current=_active;var session=store.GetGameRuntimeSession(id,ticket);
            if(current is null||session is null||current.Session.Id!=id||session.GameVersion!=current.Game.Version)throw new DomainException("Yerel oyun yayın oturumu sona erdi.",401);
            if(current.Selkies is not null)throw new DomainException("Yerel oyun yayın oturumu sona erdi.",410);
            if(reader is not null&&(!Guid.TryParse(reader,out _)||!current.Readers.Contains(reader)))throw new DomainException("Yerel oyun yayın okuyucusu bulunamadı.",404);
            if(body.Length>64*1024)throw new DomainException("Yerel yayın isteği çok büyük.",413);
            if(method=="POST"&&current.Readers.Count>=2)throw new DomainException("Yerel yayın okuyucusu sınırına ulaşıldı.",429);
            var target="http://127.0.0.1:62403/game-"+id+"/whep"+(reader is null?"":"/"+reader);
            using var request=new HttpRequestMessage(new HttpMethod(method),target);
            if(body.Length>0){request.Content=new ByteArrayContent(body);request.Content.Headers.ContentType=new MediaTypeHeaderValue(contentType??"application/sdp");}
            if(ifMatch is not null&&ifMatch.Length<256)request.Headers.TryAddWithoutValidation("If-Match",ifMatch);
            var response=await current.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if(response.Headers.Location is { } location)
            {
                var uri=new Uri(new Uri(target),location);var candidate=uri.AbsolutePath.Split('/').Last();
                if(uri.Host!="127.0.0.1"||uri.Port!=62403||!Guid.TryParse(candidate,out _)){response.Dispose();throw new DomainException("Yerel yayın yanıtı doğrulanamadı.",502);}
                current.Readers.Add(candidate);response.Headers.Location=new Uri("/game-native/"+id+"/whep/"+candidate,UriKind.Relative);
            }
            if(method=="DELETE"&&reader is not null)current.Readers.Remove(reader);
            return response;
        }
        finally{_gate.Release();}
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var protectionBudget=CancellationTokenSource.CreateLinkedTokenSource(token);
        var protection=Task.Run(()=>MonitorInputProtectionAsync(protectionBudget.Token),CancellationToken.None);
        var checkedAt=DateTimeOffset.MinValue;
        try
        {
            try
            {
                foreach(var directory in _recoveryDirectories)
                {
                    using var recoveryBudget=CancellationTokenSource.CreateLinkedTokenSource(token);recoveryBudget.CancelAfter(TimeSpan.FromMinutes(10));
                    try{await RecoverCheckpointAsync(directory,recoveryBudget.Token);}
                    catch(Exception error)when(error is DomainException or IOException or JsonException or KeyNotFoundException||error is OperationCanceledException&&!token.IsCancellationRequested){logger.LogWarning(error,"Pending native checkpoint retained for recovery");}
                }
            }
            finally{_recoveryReady.TrySetResult();}
            while(!token.IsCancellationRequested)
            {
                await _inputReady.WaitAsync(TimeSpan.FromMilliseconds(_active is null?1000:4),token);
                var current=_active;if(current is null)continue;
                try
                {
                    var now=DateTimeOffset.UtcNow;
                    if(await _gate.WaitAsync(0,token))
                    {
                        try
                        {
                            if(!ReferenceEquals(_active,current))continue;
                            NativePadFrame frame;lock(current){var neutral=current.Paused||now-current.LastInput>TimeSpan.FromMilliseconds(600);var edge=!current.Input.NextDesired.SameActions(current.AppliedFrame);frame=!neutral&&now<current.InputNotBefore&&edge?current.AppliedFrame:current.Input.NextFrame(neutral);}var desired=frame.Buttons;
                            // Upstream's bounded receiver drains a frame's changes together. No repeats while held.
                            foreach(var packet in RetroPadProtocol.Changes((ushort)current.Applied,(ushort)desired,AllowedButtons(current)))await current.Pad.SendAsync(packet,token);
                            foreach(var packet in RetroPadProtocol.AnalogChanges(current.AppliedFrame,frame))await current.Pad.SendAsync(packet,token);
                            if(current.ModernHost)foreach(var packet in RetroPadProtocol.PeripheralChanges(current.AppliedFrame,frame,current.Paused||now-current.LastInput>TimeSpan.FromMilliseconds(600)))await current.Pad.SendAsync(packet,token);
                            current.Applied=desired;
                            if(!current.AppliedFrame.SameActions(frame))current.InputNotBefore=now.AddMilliseconds(current.FrameMilliseconds);
                            current.AppliedFrame=frame;
                            for(var i=0;i<current.OtherPads.Length;i++)
                            {
                                NativePadFrame next;lock(current){var neutral=current.Paused||now-current.LastInput>TimeSpan.FromMilliseconds(600);next=!neutral&&now<current.OtherNotBefore[i]&&current.OtherInputs[i].NextDesired.Buttons!=current.OtherApplied[i].Buttons?current.OtherApplied[i]:current.OtherInputs[i].NextFrame(neutral);}
                                foreach(var packet in RetroPadProtocol.Changes(current.OtherApplied[i].Buttons,next.Buttons,ushort.MaxValue).Concat(RetroPadProtocol.AnalogChanges(current.OtherApplied[i],next)))await current.OtherPads[i].SendAsync(packet,token);
                                if(current.OtherApplied[i].Buttons!=next.Buttons)current.OtherNotBefore[i]=now.AddMilliseconds(current.FrameMilliseconds);current.OtherApplied[i]=next;
                            }
                        }
                        finally{_gate.Release();}
                    }
                    if(now-checkedAt<TimeSpan.FromSeconds(1))continue;checkedAt=now;
                    var valid=store.ValidateLoginSession(current.Login,current.Owner.Id);
                    try{store.RequireGameSession(current.Session.Id,current.Owner,current.Login);}catch(DomainException){valid=null;}
                    var protectedInput=NativeLocalInputGuard.IsRecentSafeResult(Volatile.Read(ref current.InputProtected)==1,Interlocked.Read(ref current.PolicyCheckedTicks),DateTime.UtcNow.Ticks);
                    if(valid is null||!valid.IsAdmin||!protectedInput||current.Engine.HasExited||current.Media.HasExited||current.Selkies?.HasExited==true||now-current.LastSeen>TimeSpan.FromSeconds(35))
                    {
                        await _gate.WaitAsync(token);try{if(ReferenceEquals(_active,current)){using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromMinutes(10));if(valid is not null&&protectedInput)try{await SaveAsync(current,limit.Token,current.ModernHost);}catch(Exception error){logger.LogWarning(error,"Native disconnect checkpoint was not committed");}store.CloseGameSession(current.Session.Id,current.Owner,current.Login);_active=null;await DisposeAsync(current);CleanupWork(current.Directory);events.Publish("games.changed");}}finally{_gate.Release();}
                    }
                }
                catch(Exception error)when(error is not OperationCanceledException){logger.LogWarning(error,"Native game lease check failed");}
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        finally
        {
            protectionBudget.Cancel();
            if(_active is{}current){store.CloseGameSession(current.Session.Id,current.Owner,current.Login);_active=null;await DisposeAsync(current);}
            try{await protection.WaitAsync(TimeSpan.FromSeconds(3));}catch(OperationCanceledException)when(protectionBudget.IsCancellationRequested){}catch(TimeoutException){logger.LogWarning("Native input protection monitor did not stop within its shutdown budget");}
        }
    }
    public override async Task StopAsync(CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await _gate.WaitAsync(budget.Token);
            try{if(_active is{} current)await FinishAsync(current,true,budget.Token);}
            finally{_gate.Release();}
        }
        catch(Exception error)when(error is DomainException or IOException or OperationCanceledException){logger.LogWarning(error,"Native shutdown checkpoint remains available for recovery");}
        await base.StopAsync(token);
    }
}
