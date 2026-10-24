using System.Diagnostics;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public static class OwnedNativeProcess
{
    private sealed record Owner(int Pid,long Started,string Executable);
    public static async Task RecordAsync(string directory,Process process,CancellationToken token,string? executable=null)
    {
        AppFiles.RejectLink(directory);
        var path = Path.Combine(directory, "native-process.json");
        if (File.Exists(path)) throw new DomainException("Yerel işlem günlüğü zaten mevcut.", 409);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new Owner(process.Id,process.StartTime.ToUniversalTime().Ticks,Path.GetFullPath(executable??process.StartInfo.FileName))),token);
    }
    public static async Task RecoverAsync(string directory,CancellationToken token)
    {
        var path=Path.Combine(directory,"native-process.json");if(!File.Exists(path))return;AppFiles.RejectLink(path);
        Owner? owner;try{owner=JsonSerializer.Deserialize<Owner>(await File.ReadAllTextAsync(path,token));}catch(JsonException){throw new DomainException("Yerel işlem günlüğü okunamadı.",409);}
        if(owner is not null)try{using var process=Process.GetProcessById(owner.Pid);if(process.StartTime.ToUniversalTime().Ticks==owner.Started&&string.Equals(process.MainModule?.FileName,owner.Executable,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)){MediaProcess.Kill(process);await process.WaitForExitAsync(token);}}
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        File.Delete(path);
    }
    public static async Task RunAsync(string directory,string executable,IEnumerable<string> arguments,TimeSpan limit,CancellationToken token,string? workingDirectory=null)
    {
        await RecoverAsync(directory,token);using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(limit);
        using var process=MediaProcess.Start(executable,arguments,workingDirectory:workingDirectory);var output=MediaProcess.ReadErrorTailAsync(process.StandardOutput);var errors=MediaProcess.ReadErrorTailAsync(process.StandardError);var path=Path.Combine(directory,"native-process.json");
        try
        {
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new Owner(process.Id,process.StartTime.ToUniversalTime().Ticks,Path.GetFullPath(executable))),token);
            await process.WaitForExitAsync(budget.Token);await output;await errors;if(process.ExitCode!=0)throw new DomainException("Yerel medya aracı işlemi tamamlayamadı; araç/model/codec ayarlarını kontrol et.",422);
        }
        finally{MediaProcess.Kill(process);await process.WaitForExitAsync(CancellationToken.None);await output;await errors;if(File.Exists(path))File.Delete(path);}
    }
}
