using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.IO.Pipes;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace HomeServer.Gaming;

/// <summary>Windows process/desktop adapter. Rendering/recording stay inside upstream RetroArch.</summary>
public sealed class NativeGpuProcess:IDisposable
{
    public Process Process {get;}
    public string DesktopName {get;}
    public string ErrorLog {get;}
    public string OutputLog {get;}
    private readonly DesktopHandle _desktop;
    private readonly StreamReader _outputReader,_errorReader;
    private readonly Task<string> _outputTail,_errorTail;
    private NativeGpuProcess(Process process,DesktopHandle desktop,string name,string output,string error,AnonymousPipeServerStream stdout,AnonymousPipeServerStream stderr)
    {Process=process;_desktop=desktop;DesktopName=name;OutputLog=output;ErrorLog=error;_outputReader=new(stdout,Encoding.UTF8);_errorReader=new(stderr,Encoding.UTF8);_outputTail=Task.Factory.StartNew(()=>ReadTail(_outputReader),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);_errorTail=Task.Factory.StartNew(()=>ReadTail(_errorReader),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
    public static NativeGpuProcess Start(string executable,IEnumerable<string> arguments,string workspace)
    {
        if(!OperatingSystem.IsWindows())throw new DomainException("GPU oyun yayını şu an Windows sunucuda kullanılabilir.",409);
        AppFiles.RejectLink(workspace);var name="Home.NativeGpu."+Guid.NewGuid().ToString("N");
        using var identity=WindowsIdentity.GetCurrent();if(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new DomainException("Oyun çalıştırıcısı yönetici yetkisiyle çalıştırılamaz.",409);
        var sid=identity.User?.Value??throw new DomainException("GPU masaüstü için kullanıcı kimliği gerekli.",409);
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;"+sid+")",1,out var descriptor,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
        DesktopHandle desktop;
        try{var security=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor};desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x01ff,ref security);}
        finally{LocalFree(descriptor);}
        if(desktop.IsInvalid){desktop.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
        var stdout=Path.Combine(workspace,"gpu.stdout.log");var stderr=Path.Combine(workspace,"gpu.stderr.log");
        Process? process=null;AnonymousPipeServerStream? output=null,error=null;
        try
        {
            var inheritance=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Inherit=true};
            output=new(PipeDirection.In,HandleInheritability.Inheritable);error=new(PipeDirection.In,HandleInheritability.Inheritable);using var input=CreateFile("NUL",0x80000000,3,ref inheritance,3,0x80,IntPtr.Zero);
            if(input.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup=new StartupInfoEx{Info=new StartupInfo{Size=Marshal.SizeOf<StartupInfoEx>(),Desktop=name,Flags=0x100,Input=input.DangerousGetHandle(),Output=output.ClientSafePipeHandle.DangerousGetHandle(),Error=error.ClientSafePipeHandle.DangerousGetHandle()}};
            var command=new StringBuilder(string.Join(' ',new[]{executable}.Concat(arguments).Select(QuoteArgument)));
            // Never switches desktops, requests elevation, captures a display, or launches on the user's default desktop.
            using var inherited=new InheritedHandles(input.DangerousGetHandle(),output.ClientSafePipeHandle.DangerousGetHandle(),error.ClientSafePipeHandle.DangerousGetHandle());startup.Attributes=inherited.Attributes;
            if(!CreateProcess(executable,command,IntPtr.Zero,IntPtr.Zero,true,0x08080004,IntPtr.Zero,workspace,ref startup,out var created))throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                process=Process.GetProcessById((int)created.ProcessId);WindowsMediaJob.Bind(process);
                if(ResumeThread(created.Thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch{TerminateProcess(created.Process,1);throw;}
            finally{CloseHandle(created.Thread);CloseHandle(created.Process);}
            output.DisposeLocalCopyOfClientHandle();error.DisposeLocalCopyOfClientHandle();return new(process,desktop,name,stdout,stderr,output,error);
        }
        catch{if(process is not null){MediaProcess.Kill(process);process.WaitForExit();process.Dispose();}output?.Dispose();error?.Dispose();desktop.Dispose();throw;}
    }
    public static string QuoteArgument(string value)
    {
        if(value.Any(char.IsControl))throw new DomainException("GPU oyun komut satırı geçersiz.");
        var output=new StringBuilder("\"");var slashes=0;
        foreach(var character in value){if(character=='\\'){slashes++;continue;}if(character=='"'){output.Append('\\',slashes*2+1).Append('"');slashes=0;continue;}output.Append('\\',slashes).Append(character);slashes=0;}
        return output.Append('\\',slashes*2).Append('"').ToString();
    }
    public async Task<string> ReadLogAfterExitAsync(string path)
    {
        var tail=path==ErrorLog?_errorTail:path==OutputLog?_outputTail:throw new ArgumentException("Unknown owned native log.",nameof(path));await Process.WaitForExitAsync(CancellationToken.None);var result=await tail;WriteLog(path,result);return result;
    }
    public (int PrivateWindows,int DefaultWindows) WindowBoundary()
    {
        var isolated=0;var visible=0;EnumWindow callback=(window,_)=>{GetWindowThreadProcessId(window,out var id);if(id==Process.Id)isolated++;return true;};
        Marshal.SetLastPInvokeError(0);if(!EnumDesktopWindows(_desktop.DangerousGetHandle(),callback,IntPtr.Zero)&&Marshal.GetLastWin32Error()!=0)throw new Win32Exception(Marshal.GetLastWin32Error());
        EnumWindow normal=(window,_)=>{GetWindowThreadProcessId(window,out var id);if(id==Process.Id)visible++;return true;};EnumWindows(normal,IntPtr.Zero);return(isolated,visible);
    }
    public void Dispose(){try{if(!Process.HasExited){MediaProcess.Kill(Process);Process.WaitForExit();}Task.WhenAll(_outputTail,_errorTail).GetAwaiter().GetResult();WriteLog(OutputLog,_outputTail.Result);WriteLog(ErrorLog,_errorTail.Result);}finally{_outputReader.Dispose();_errorReader.Dispose();Process.Dispose();_desktop.Dispose();}}
    private static void WriteLog(string path,string text){try{File.WriteAllText(path,text);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
    private static string ReadTail(StreamReader reader)
    {var output=new StringBuilder();var buffer=new char[2048];int length;while((length=reader.Read(buffer,0,buffer.Length))>0){output.Append(buffer,0,length);if(output.Length>8192)output.Remove(0,output.Length-8192);}return output.ToString();}
    [StructLayout(LayoutKind.Sequential)]private struct SecurityAttributes{public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct StartupInfo{public int Size;public string? Reserved,Desktop,Title;public int X,Y,XSize,YSize,XCount,YCount,Fill,Flags;public short Show,ReservedBytes;public IntPtr ReservedPointer,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct StartupInfoEx{public StartupInfo Info;public IntPtr Attributes;}
    [StructLayout(LayoutKind.Sequential)]private struct ProcessInfo{public IntPtr Process,Thread;public uint ProcessId,ThreadId;}
    private sealed class DesktopHandle:SafeHandleZeroOrMinusOneIsInvalid{public DesktopHandle():base(true){}protected override bool ReleaseHandle()=>CloseDesktop(handle);}
    private sealed class InheritedHandles:IDisposable
    {
        public IntPtr Attributes {get;private set;}private IntPtr _values;private bool _initialized;
        public InheritedHandles(params IntPtr[] handles)
        {
            try{nuint bytes=0;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref bytes);Attributes=Marshal.AllocHGlobal(checked((int)bytes));if(!InitializeProcThreadAttributeList(Attributes,1,0,ref bytes))throw new Win32Exception(Marshal.GetLastWin32Error());_initialized=true;_values=Marshal.AllocHGlobal(handles.Length*IntPtr.Size);Marshal.Copy(handles,0,_values,handles.Length);if(!UpdateProcThreadAttribute(Attributes,0,(nuint)0x20002,_values,(nuint)(handles.Length*IntPtr.Size),IntPtr.Zero,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());}
            catch{Dispose();throw;}
        }
        public void Dispose(){if(Attributes!=IntPtr.Zero){if(_initialized)DeleteProcThreadAttributeList(Attributes);Marshal.FreeHGlobal(Attributes);Attributes=IntPtr.Zero;}if(_values!=IntPtr.Zero){Marshal.FreeHGlobal(_values);_values=IntPtr.Zero;}}
    }
    [DllImport("user32.dll",EntryPoint="CreateDesktopW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern DesktopHandle CreateDesktop(string desktop,IntPtr device,IntPtr mode,uint flags,uint access,ref SecurityAttributes security);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseDesktop(IntPtr desktop);
    private delegate bool EnumWindow(IntPtr window,IntPtr argument);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnumDesktopWindows(IntPtr desktop,EnumWindow callback,IntPtr argument);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnumWindows(EnumWindow callback,IntPtr argument);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("advapi32.dll",EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint bytes);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string name,uint access,uint sharing,ref SecurityAttributes security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool InitializeProcThreadAttributeList(IntPtr attributes,int count,int flags,ref nuint bytes);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool UpdateProcThreadAttribute(IntPtr attributes,uint flags,nuint attribute,IntPtr value,nuint bytes,IntPtr previous,IntPtr returnedBytes);
    [DllImport("kernel32.dll")]private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool TerminateProcess(IntPtr process,uint exitCode);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(IntPtr handle);
}
