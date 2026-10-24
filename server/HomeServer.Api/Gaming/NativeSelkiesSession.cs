using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

/// <summary>One demand-started video/audio publisher; Home retains the shared input and save contract.</summary>
public sealed class NativeSelkiesSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop=new();
    private readonly CancellationToken _lifetime;
    private readonly string _directory,_soundDirectory,_viewerToken;
    private readonly NativeSelkiesInstallation _installation;
    private NativeLinuxDisplay? _display;
    private Process? _sound,_process;
    private Task<string>? _soundOutput,_soundError;
    private int _disposed,_readers;
    public string Prefix{get;}
    public string Origin{get;}
    public string ClientUrl=>Prefix+"/#shared&token="+_viewerToken;
    public Process Process=>_process??throw new InvalidOperationException("Selkies has not started.");
    public HttpClient Http{get;}=new(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(8)};
    public Task<string>? Output{get;private set;}
    public Task<string>? Error{get;private set;}
    public CancellationToken Lifetime=>_lifetime;
    public bool HasExited=>_sound is null||_sound.HasExited||_process is null||_process.HasExited;
    public IReadOnlyDictionary<string,string> Environment{get;private set;}=new Dictionary<string,string>();
    private NativeSelkiesSession(string directory,NativeSelkiesInstallation installation,string prefix,string origin,string viewerToken)
    {_lifetime=_stop.Token;_directory=AppFiles.DirectoryIn(directory,"selkies");_soundDirectory=AppFiles.DirectoryIn(directory,"sound");_installation=installation;Prefix=prefix;Origin=origin;_viewerToken=viewerToken;}
    public static async Task<NativeSelkiesSession> PrepareAsync(string directory,NativeSelkiesInstallation installation,string prefix,string origin,string viewerToken,int width,int height,CancellationToken token)
    {
        if(!OperatingSystem.IsLinux()||!File.Exists("/usr/bin/pulseaudio"))throw new DomainException("Selkies için yerel ses paketi gerekli.",409);
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.PathAndQuery!="/"||uri.UserInfo.Length!=0)
            throw new DomainException("Oyun yayın adresi geçersiz.");
        if(prefix.Length>512||!prefix.StartsWith('/')||prefix.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('/' or '-' or '_')))
            throw new DomainException("Oyun yayın adresi geçersiz.");
        var session=new NativeSelkiesSession(directory,installation,prefix,origin,viewerToken);
        try
        {
            foreach(var folder in new[]{session._directory,session._soundDirectory})File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            session._display=await NativeLinuxDisplay.StartAsync(directory,width,height,token);
            var socket=Path.Combine(session._soundDirectory,"native");if(System.Text.Encoding.UTF8.GetByteCount(socket)>100)throw new DomainException("Selkies çalışma dizini yolu çok uzun.",409);
            var environment=new Dictionary<string,string>(session._display.Environment)
            {
                ["HOME"]=session._directory,["PULSE_RUNTIME_PATH"]=session._soundDirectory,["PULSE_SERVER"]="unix:"+socket,
                ["PULSE_SINK"]="home_game",["PULSE_SOURCE"]="home_game.monitor",["PYTHONDONTWRITEBYTECODE"]="1",
                ["SDL_VIDEODRIVER"]="x11",["SDL_RENDER_DRIVER"]="software",["SDL_FRAMEBUFFER_ACCELERATION"]="0"
            };
            session.Environment=environment;
            var config=Path.Combine(session._soundDirectory,"pulse.pa");
            await File.WriteAllTextAsync(config,$"load-module module-native-protocol-unix socket={socket} auth-anonymous=1\nload-module module-null-sink sink_name=home_game rate=48000 channels=2\nset-default-sink home_game\nset-default-source home_game.monitor\n",token);
            session._sound=MediaProcess.Start("/usr/bin/pulseaudio",["--daemonize=no","--use-pid-file=no","--exit-idle-time=-1","--disallow-exit","--log-target=stderr","--log-level=error","-n","--file="+config],workingDirectory:session._soundDirectory,environment:environment);
            session._soundOutput=MediaProcess.ReadErrorTailAsync(session._sound.StandardOutput);session._soundError=MediaProcess.ReadErrorTailAsync(session._sound.StandardError);
            await OwnedNativeProcess.RecordAsync(session._soundDirectory,session._sound,token);
            for(var attempt=0;attempt<100;attempt++)
            {
                token.ThrowIfCancellationRequested();if(session._sound.HasExited)throw new DomainException("Selkies ses oturumu başlatılamadı.",502);
                if(File.Exists(socket))return session;await Task.Delay(50,token);
            }
            throw new DomainException("Selkies ses oturumu başlatılamadı.",502);
        }
        catch{await session.DisposeAsync();throw;}
    }
    public async Task StartAsync(string[] advertisedHosts,int width,int height,double framesPerSecond,string quality,CancellationToken token)
    {
        using var startup=CancellationTokenSource.CreateLinkedTokenSource(token);startup.CancelAfter(TimeSpan.FromSeconds(45));
        try{await StartPublisherAsync(advertisedHosts,width,height,framesPerSecond,quality,startup.Token);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new DomainException("Selkies yayın oturumu hazır hâle gelmedi.",502);}
    }
    private async Task StartPublisherAsync(string[] advertisedHosts,int width,int height,double framesPerSecond,string quality,CancellationToken token)
    {
        if(!OperatingSystem.IsLinux())throw new DomainException("Selkies şu anda Linux x64 sunucuda kullanılabilir.",409);
        var master=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var tokens=Path.Combine(_directory,"tokens.json");
        await File.WriteAllTextAsync(tokens,JsonSerializer.Serialize(new Dictionary<string,object>{{_viewerToken,new{role="viewer",slot=(int?)null,mk_control=false}}}),token);
        File.SetUnixFileMode(tokens,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var rtc=Path.Combine(_directory,"rtc.json");await File.WriteAllTextAsync(rtc,"{\"iceServers\":[]}",token);
        var addresses=new List<IPAddress>();
        foreach(var host in advertisedHosts.Where(host=>host!="127.0.0.1"||advertisedHosts.Length==1))
        {
            if(IPAddress.TryParse(host,out var parsed))addresses.Add(parsed);
            else addresses.AddRange(await Dns.GetHostAddressesAsync(host,token));
        }
        var publicIps=addresses.Where(address=>address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6&&!address.IsIPv6LinkLocal)
            .GroupBy(address=>address.AddressFamily).Select(group=>group.First().ToString()).Take(2).ToArray();
        if(publicIps.Length==0)throw new DomainException("Oyun yayını için geçerli sunucu IP adresi veya adı gerekli.");
        var fps=Math.Clamp(framesPerSecond,20,120).ToString("0.######",CultureInfo.InvariantCulture);var bitrate=quality=="low"?"1200":quality=="quality"?"4000":"2500";
        var environment=new Dictionary<string,string>(Environment){["SELKIES_MASTER_TOKEN"]=master};
        _process=MediaProcess.Start(_installation.Manifest.Python,["-m","selkies",
            "--addr=127.0.0.1","--port=62403","--subfolder="+Prefix,"--allowed-origins="+Origin,"--frame-ancestors=self",
            "--enable-https=false","--enable-basic-auth=false","--tokens-file="+tokens,"--mode=webrtc","--enable-dual-mode=false",
            "--webrtc-udp-mux-port=62405","--webrtc-tcp-mux-port=62405","--webrtc-public-ip="+string.Join(',',publicIps),"--webrtc-ice-lite=true","--rtc-config-json="+rtc,
            "--encoder=h264enc","--use-cpu=true|locked","--cpu-encode-threading=false|locked","--video-fullcolor=false|locked","--video-10bit=false|locked",
            "--framerate="+fps+"-"+fps,"--video-bitrate="+bitrate+",100-"+bitrate,"--rate-control-mode=cbr","--congestion-control=true",
            "--enable-resize=false","--manual-resolution=true","--manual-width="+width,"--manual-height="+height,
            "--audio-device-name=home_game.monitor","--audio-bitrate=64000","--gamepad-enabled=false","--gamepad-on-start=false","--command-enabled=false",
            "--enable-clipboard=false","--enable-binary-clipboard=false","--file-transfers=none","--enable-sharing=false","--enable-collab=false",
            "--microphone-enabled=false","--webcam-enabled=false","--enable-webrtc-statistics=false","--ui-title=Home"],workingDirectory:_directory,environment:environment);
        Output=MediaProcess.ReadErrorTailAsync(_process.StandardOutput);Error=MediaProcess.ReadErrorTailAsync(_process.StandardError);
        await OwnedNativeProcess.RecordAsync(_directory,_process,token,_installation.Manifest.PythonTarget);
        for(var attempt=0;attempt<100;attempt++)
        {
            token.ThrowIfCancellationRequested();if(_process.HasExited)throw new DomainException("Selkies yayın oturumu başlatılamadı.",502);
            try{using var response=await Http.GetAsync(Target("api/health"),token);if(response.IsSuccessStatusCode)return;}catch(HttpRequestException){}
            await Task.Delay(100,token);
        }
        throw new DomainException("Selkies yayın oturumu hazır hâle gelmedi.",502);
    }
    public string Target(string asset)=>"http://127.0.0.1:62403"+Prefix+"/"+asset;
    public HttpRequestMessage ReadRequest(string asset,bool head=false)
    {var request=new HttpRequestMessage(head?HttpMethod.Head:HttpMethod.Get,Target(asset));request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_viewerToken);return request;}
    public void AcquireReader()
    {if(Interlocked.Increment(ref _readers)>2){Interlocked.Decrement(ref _readers);throw new DomainException("Yerel yayın okuyucusu sınırına ulaşıldı.",429);}}
    public void ReleaseReader()=>Interlocked.Decrement(ref _readers);
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;_stop.Cancel();
        if(_process is not null){MediaProcess.Kill(_process);await _process.WaitForExitAsync(CancellationToken.None);if(Output is not null)await Output;if(Error is not null)await Error;_process.Dispose();}
        if(_sound is not null){MediaProcess.Kill(_sound);await _sound.WaitForExitAsync(CancellationToken.None);if(_soundOutput is not null)await _soundOutput;if(_soundError is not null)await _soundError;_sound.Dispose();}
        if(_display is not null)await _display.DisposeAsync();Http.Dispose();_stop.Dispose();
    }
}
