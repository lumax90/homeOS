using System.Diagnostics;

namespace HomeServer.Operations;

public sealed class RestartCoordinator
{
    public bool Requested { get; set; }
    public string? ReplacementDirectory { get; set; }
    public string? ReplacementEntryPoint { get; set; }
    public string? ReplacementDataPath { get; set; }
    public bool Supported => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true"
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID"))
        && ((Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "").Equals("HomeServer.Api", StringComparison.OrdinalIgnoreCase)
            || (Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase)
               && Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]).Equals("HomeServer.Api", StringComparison.OrdinalIgnoreCase));
    public void LaunchReplacement()
    {
        if (!Requested || !Supported) return;
        if (ReplacementDirectory is not null && ReplacementEntryPoint is not null) { StartReplacement(ReplacementDirectory, ReplacementEntryPoint, ReplacementDataPath); return; }
        var executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
        AddArguments(start); Process.Start(start);
    }
    public static void StartReplacement(string directory, string entryPoint, string? dataPath = null)
    {
        var start = new ProcessStartInfo(Path.Combine(directory, entryPoint)) { UseShellExecute = false, WorkingDirectory = directory };
        AddArguments(start);
        if (dataPath is not null) { start.ArgumentList.Add("--Home:DataPath"); start.ArgumentList.Add(Path.GetFullPath(dataPath)); }
        Process.Start(start);
    }
    private static void AddArguments(ProcessStartInfo start)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == "--Home:WaitForExitPid") { index++; continue; }
            start.ArgumentList.Add(arguments[index]);
        }
        start.ArgumentList.Add("--Home:WaitForExitPid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
    }
}
