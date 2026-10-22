using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HomeServer.Infrastructure;

public static class StableFileIdentity
{
    public static string? Read(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandleEx(handle, 18, out var id, (uint)Marshal.SizeOf<FileId>()) || !GetFileInformationByHandle(handle, out var info) || id.Low == 0 && id.High == 0) return null;
            return $"win:{id.Volume:x16}:{id.High:x16}{id.Low:x16}:{info.CreatedHigh:x8}{info.CreatedLow:x8}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileId { public ulong Volume, Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoByHandle
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileId information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoByHandle information);
}
