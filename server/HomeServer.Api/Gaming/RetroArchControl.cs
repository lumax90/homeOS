using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HomeServer.Core;
using System.Text.Json;
using System.Globalization;

namespace HomeServer.Gaming;

/// <summary>Named upstream commands via local UDP. Caller must verify the native listener's firewall precondition.</summary>
public sealed class RetroArchControl : IAsyncDisposable
{
    private readonly Process _process;
    private readonly UdpClient _socket;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly TimeSpan _queryBudget;
    public RetroArchControl(Process process, int port,TimeSpan? queryBudget=null)
    {
        if (port is < 1024 or > 65535) throw new DomainException("Yerel oyun komut portu geçersiz.");
        _queryBudget=queryBudget??TimeSpan.FromSeconds(3);if(_queryBudget<TimeSpan.FromSeconds(1)||_queryBudget>TimeSpan.FromSeconds(30))throw new DomainException("Yerel oyun komut süresi geçersiz.");
        _process = process; _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); _socket.Connect(IPAddress.Loopback, port);
    }
    public async Task<string> StatusAsync(CancellationToken token)
    { await _commands.WaitAsync(token); try { return await QueryAsync(token); } finally { _commands.Release(); } }
    public async Task WaitReadyAsync(TimeSpan timeout,CancellationToken token)
    {
        using var startup=CancellationTokenSource.CreateLinkedTokenSource(token);startup.CancelAfter(timeout);
        try
        {
            for(;;)
            {
                try{await StatusAsync(startup.Token);return;}
                catch(DomainException)when(!_process.HasExited){await Task.Delay(100,startup.Token);}
            }
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new DomainException("Yerel oyun bileşeni başlatılamadı.",502);}
    }
    public async Task SetPausedAsync(bool paused, CancellationToken token)
    {
        await _commands.WaitAsync(token);
        try
        {
            var status = await QueryAsync(token);
            if (!status.StartsWith("GET_STATUS PLAYING ", StringComparison.Ordinal) && !status.StartsWith("GET_STATUS PAUSED ", StringComparison.Ordinal)) throw new DomainException("Yerel emülatör oyun oturumunu doğrulayamadı.", 502);
            if (status.StartsWith("GET_STATUS PAUSED ", StringComparison.Ordinal) == paused) return;
            await SendAsync("PAUSE_TOGGLE", token);
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if ((await QueryAsync(token)).StartsWith(paused ? "GET_STATUS PAUSED " : "GET_STATUS PLAYING ", StringComparison.Ordinal)) return;
                await Task.Delay(25, token);
            }
            throw new DomainException("Yerel oyunun duraklatma durumu doğrulanamadı.", 502);
        }
        finally { _commands.Release(); }
    }
    public Task SaveStateAsync(CancellationToken token) => CommandAsync("SAVE_STATE", token);
    public async Task FlushSaveFilesAsync(CancellationToken token)
    {
        await _commands.WaitAsync(token);
        try
        {
            while(_socket.Available>0)await _socket.ReceiveAsync(token);await SendAsync("SAVE_FILES",token);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(3));
            var reply=await _socket.ReceiveAsync(budget.Token);if(reply.Buffer.Length>16||Encoding.ASCII.GetString(reply.Buffer).Trim('\0','\r','\n') is not("OK" or "NO"))throw new DomainException("Çekirdeğin kayıt dosyası yanıtı geçersiz.",502);
        }
        finally{_commands.Release();}
    }
    public async Task LoadStateAsync(CancellationToken token)
    {
        // LOAD_STATE is a hotkey and can release pause after its queued work.
        // The released slot action queues the same load without that hotkey side effect.
        if(await ReplyAsync("LOAD_STATE_SLOT 0",token)!="LOAD_STATE_SLOT 0")throw new DomainException("Emülatör kayıt yükleme komutunu doğrulayamadı.",502);
    }
    public Task QuitAsync(CancellationToken token) => CommandAsync("QUIT", token);
    public Task EnablePacedHeadlessModeAsync(CancellationToken token) => CommandAsync("FAST_FORWARD", token);
    public async Task<(int Count,int Index)> DiskStatusAsync(CancellationToken token)
    {
        var text=await ReplyAsync("HOME_DISK_STATUS",token);var words=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(words.Length!=4||words[0]!="HOME_DISK_STATUS"||!int.TryParse(words[1],out var count)||!int.TryParse(words[2],out var index)||count is <0 or >128||index<0||index>count)throw new DomainException("Emülatör disk durumu geçersiz.",502);return(count,index);
    }
    public async Task SetDiskAsync(int index,CancellationToken token)
    {
        if(index is <0 or >127)throw new DomainException("Oyun disk seçimi geçersiz.");if(await ReplyAsync("HOME_DISK_SET "+index,token)!="OK")throw new DomainException("Oyun diski değiştirilemedi.",409);var actual=await DiskStatusAsync(token);if(actual.Index!=index)throw new DomainException("Oyun disk seçimi doğrulanamadı.",409);
    }
    public async Task<NativeCoreOption[]> CoreOptionsAsync(CancellationToken token)
    {
        var result=new List<NativeCoreOption>();var count=1;
        for(var index=0;index<count&&index<512;index++)
        {
            var reply=await ReplyAsync("HOME_OPTION_GET "+index,token);var parts=reply.Split(' ',3);if(parts.Length!=3||parts[0]!="HOME_OPTION"||!int.TryParse(parts[1],out count)||count is <0 or >512)throw new DomainException("Çekirdek ayar yanıtı geçersiz.",502);if(parts[2]=="-")break;
            var option=JsonSerializer.Deserialize<NativeCoreOption>(Convert.FromHexString(parts[2]),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("Çekirdek ayar yanıtı geçersiz.",502);if(option.Index!=index||option.Values.Length>128||!option.Values.Contains(option.Value))throw new DomainException("Çekirdek ayar yanıtı geçersiz.",502);result.Add(option);
        }
        return result.ToArray();
    }
    public async Task<double> FrameMillisecondsAsync(CancellationToken token)
    {var text=await ReplyAsync("HOME_TIMING",token);if(!text.StartsWith("HOME_TIMING ",StringComparison.Ordinal)||!double.TryParse(text[12..],NumberStyles.Float,CultureInfo.InvariantCulture,out var fps)||fps is <20 or >240)throw new DomainException("Çekirdek kare hızı yanıtı geçersiz.",502);return 1000/fps+1;}
    public async Task<bool> SupportsStateAsync(CancellationToken token)
    {var text=await ReplyAsync("HOME_STATE_SIZE",token);if(!text.StartsWith("HOME_STATE_SIZE ",StringComparison.Ordinal)||!long.TryParse(text[16..],out var size)||size<0)throw new DomainException("Çekirdek kayıt yeteneği yanıtı geçersiz.",502);return size>0;}
    public async Task<NativeControllerPort[]> ControllersAsync(CancellationToken token)
    {var ports=new List<NativeControllerPort>();for(var i=0;i<4;i++){var port=JsonSerializer.Deserialize<NativeControllerPort>(await ReplyAsync("HOME_DEVICE_GET "+i,token),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new DomainException("Kumanda bilgisi geçersiz.",502);if(port.Port!=i||port.Items.Length>64)throw new DomainException("Kumanda bilgisi geçersiz.",502);ports.Add(port);}return ports.ToArray();}
    public async Task SetControllerAsync(int port,int device,CancellationToken token)
    {if(port is <0 or >3||device is <0 or >65535)throw new DomainException("Kumanda seçimi geçersiz.");if(await ReplyAsync($"HOME_DEVICE_SET {port} {device}",token)!="OK")throw new DomainException("Kumanda seçimi desteklenmiyor.",409);}
    public async Task SetCoreOptionAsync(int index,int value,CancellationToken token)
    {if(index is <0 or >511||value is <0 or >127)throw new DomainException("Çekirdek ayarı geçersiz.");if(await ReplyAsync($"HOME_OPTION_SET {index} {value}",token)!="OK")throw new DomainException("Çekirdek ayarı değiştirilemedi.",409);}
    private async Task<string> ReplyAsync(string command,CancellationToken token)
    {
        await _commands.WaitAsync(token);try{while(_socket.Available>0)await _socket.ReceiveAsync(token);await SendAsync(command,token);using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(_queryBudget);var packet=await _socket.ReceiveAsync(budget.Token);if(packet.Buffer.Length>32768)throw new DomainException("Yerel oyun komut yanıtı çok büyük.",502);return Encoding.UTF8.GetString(packet.Buffer).Trim('\r','\n','\0');}finally{_commands.Release();}
    }
    private async Task CommandAsync(string command, CancellationToken token)
    { await _commands.WaitAsync(token); try { await SendAsync(command, token); } finally { _commands.Release(); } }
    private async Task<string> QueryAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(_queryBudget);
        try
        {
            // A connected UDP socket can surface startup ICMP refusal on the
            // next send or availability read, not just on ReceiveAsync.
            while (_socket.Available > 0) await _socket.ReceiveAsync(timeout.Token);
            await SendAsync("GET_STATUS", timeout.Token);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var packet = await _socket.ReceiveAsync(timeout.Token);
                if (packet.Buffer.Length > 8192) throw new DomainException("Yerel oyun komut yanıtı çok büyük.", 502);
                var response = Encoding.UTF8.GetString(packet.Buffer).TrimEnd('\r', '\n', '\0');
                if (response.StartsWith("GET_STATUS ", StringComparison.Ordinal)) return response;
            }
            throw new DomainException("Yerel oyun komut yanıtı geçersiz.", 502);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException(_process.HasExited ? "Yerel oyun işlemi sona erdi." : "Emülatörün yerel komut kanalı yanıt vermedi.", 502); }
        catch (SocketException) { throw new DomainException(_process.HasExited ? "Yerel oyun işlemi sona erdi." : "Emülatörün yerel komut kanalı yanıt vermedi.", 502); }
    }
    private async Task SendAsync(string command, CancellationToken token)
    {
        if (_process.HasExited) throw new DomainException("Yerel oyun işlemi sona erdi.", 409);
        await _socket.SendAsync(Encoding.ASCII.GetBytes(command + "\n"), token);
    }
    public ValueTask DisposeAsync() { _socket.Dispose(); _commands.Dispose(); return ValueTask.CompletedTask; }
}
