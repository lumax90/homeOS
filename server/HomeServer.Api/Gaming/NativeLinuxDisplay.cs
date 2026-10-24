using System.Diagnostics;
using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

/// <summary>One leased virtual X display for hardware-context cores. No physical monitor/login session.</summary>
public sealed class NativeLinuxDisplay: IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _output,_error;
    public IReadOnlyDictionary<string,string> Environment {get;}
    private NativeLinuxDisplay(Process process,Task<string> output,Task<string> error,int number,string auth,string runtime)
    {_process=process;_output=output;_error=error;Environment=new Dictionary<string,string>{{"DISPLAY",":"+number},{"XAUTHORITY",auth},{"XDG_RUNTIME_DIR",runtime},{"LIBGL_ALWAYS_SOFTWARE","1"}};}
    public static async Task<NativeLinuxDisplay> StartAsync(string directory,int width,int height,CancellationToken token)
    {
        if(!OperatingSystem.IsLinux()||!File.Exists("/usr/bin/Xvfb")||!File.Exists("/usr/bin/xauth"))throw new DomainException("Başsız OpenGL için Xvfb paketi gerekli.",409);
        var root=AppFiles.DirectoryIn(directory,"display");File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        for(var attempt=0;attempt<10;attempt++)
        {
            var number=RandomNumberGenerator.GetInt32(90,999);if(File.Exists($"/tmp/.X{number}-lock")||File.Exists($"/tmp/.X11-unix/X{number}"))continue;
            var auth=Path.Combine(root,"authority-"+number);await File.WriteAllBytesAsync(auth,[],token);File.SetUnixFileMode(auth,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            await MediaProcess.RunAsync("/usr/bin/xauth",["-f",auth,"add",":"+number,"MIT-MAGIC-COOKIE-1",Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))],TimeSpan.FromSeconds(5),token);
            var process=MediaProcess.Start("/usr/bin/Xvfb",[":"+number,"-screen","0",$"{width}x{height}x24","-s","0","-dpms","-nolisten","tcp","-auth",auth,"-noreset"],workingDirectory:root);
            var output=MediaProcess.ReadErrorTailAsync(process.StandardOutput);var error=MediaProcess.ReadErrorTailAsync(process.StandardError);
            try
            {
                for(var wait=0;wait<100;wait++){token.ThrowIfCancellationRequested();if(process.HasExited)break;if(File.Exists($"/tmp/.X11-unix/X{number}")){await OwnedNativeProcess.RecordAsync(root,process,token);return new(process,output,error,number,auth,root);}await Task.Delay(50,token);}
                throw new DomainException("Başsız OpenGL ekranı başlatılamadı.",502);
            }
            catch{MediaProcess.Kill(process);await process.WaitForExitAsync(CancellationToken.None);await output;await error;process.Dispose();throw;}
        }
        throw new DomainException("Başsız OpenGL ekranı için boş oturum bulunamadı.",409);
    }
    public async ValueTask DisposeAsync()
    {
        if(!_process.HasExited)
        {
            _=Signal(_process.Id,15); // Xvfb unlinks its own Unix socket/lock on SIGTERM.
            try{await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){MediaProcess.Kill(_process);}
        }
        await _process.WaitForExitAsync(CancellationToken.None);await _output;await _error;_process.Dispose();
    }
    [System.Runtime.InteropServices.DllImport("libc",EntryPoint="kill")]private static extern int Signal(int process,int signal);
}
