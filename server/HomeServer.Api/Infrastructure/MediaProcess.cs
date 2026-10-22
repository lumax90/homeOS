using System.Diagnostics;
using System.Text;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public static class MediaProcess
{
    public const string MediaFormats = "mov,matroska,webm,avi,mp3,flac,ogg,wav,aiff,mpegts,mpeg";

    public static Process Start(string executable, IEnumerable<string> arguments, bool captureOutput = true, string? workingDirectory = null, bool redirectInput = false,IReadOnlyDictionary<string,string>? environment=null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = captureOutput ? Encoding.UTF8 : null, StandardErrorEncoding = Encoding.UTF8,
            RedirectStandardInput = redirectInput, StandardInputEncoding = redirectInput ? new UTF8Encoding(false) : null
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        if(environment is not null)foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        try { process.Start(); WindowsMediaJob.Bind(process); return process; }
        catch { try { Kill(process); } catch (InvalidOperationException) { } process.Dispose(); throw; }
    }

    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan budget, CancellationToken token, string? workingDirectory = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(budget);
        using var process = Start(executable, arguments, workingDirectory: workingDirectory);
        var output = ReadOutputAsync(process.StandardOutput, timeout);
        var errors = ReadErrorTailAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var result = await output;
            var error = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            return result;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await output; } catch (OperationCanceledException) { } catch (DomainException) { }
            await errors;
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationTokenSource cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellation.Token)) > 0)
        {
            if (result.Length + count > 8 * 1024 * 1024)
            {
                cancellation.Cancel();
                throw new DomainException("Medya analizi izin verilen çıktı boyutunu aşıyor.");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    public static async Task<string> ReadErrorTailAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 4096) tail.Remove(0, tail.Length - 4096);
        }
        return tail.ToString();
    }

    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
