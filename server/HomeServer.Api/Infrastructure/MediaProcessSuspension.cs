using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HomeServer.Infrastructure;

/// <summary>Backpressure for an owned encoder. Stopping or killing the child still works while suspended.</summary>
public sealed class MediaProcessSuspension(Process process)
{
    public bool Paused { get; private set; }
    public void Set(bool paused)
    {
        if (Paused == paused || process.HasExited) return;
        if (OperatingSystem.IsWindows())
        {
            var status = paused ? NtSuspendProcess(process.Handle) : NtResumeProcess(process.Handle);
            if (status < 0 && !process.HasExited) throw new IOException("The media process could not change its production state.");
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var signal = OperatingSystem.IsMacOS() ? paused ? 17 : 19 : paused ? 19 : 18;
            if (Signal(process.Id, signal) != 0 && !process.HasExited) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        else throw new PlatformNotSupportedException("Bounded media production requires a supported server operating system.");
        Paused = paused;
    }
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int Signal(int process, int signal);
}
