namespace HomeServer.Infrastructure;

public static class ExecutablePath
{
    public static string? Find(string? configured, string name)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var filename = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var local = Path.Combine(AppContext.BaseDirectory, filename);
        if (File.Exists(local)) return local;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var file = Path.Combine(directory.Trim('"'), filename); if (File.Exists(file)) return Path.GetFullPath(file);
        }
        return null;
    }
}
