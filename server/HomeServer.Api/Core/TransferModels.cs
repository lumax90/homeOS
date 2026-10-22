namespace HomeServer.Core;

public sealed record TransferPolicy(int Priority = 25, int DownloadKiB = 0, int UploadKiB = 0, double? SeedRatio = null,
    int? SeedMinutes = null, string SeedStop = "either", string[]? Files = null, Dictionary<string, int>? FilePriorities = null);
public sealed record TransferDefaults(int DownloadKiB = 0, int UploadKiB = 512, int MaximumConnections = 120);
public sealed record TransferFile(string Path, long Length, bool Selected, int Priority = 3);
public sealed record CustomFormat(string Name, int Score = 0, string? Pattern = null, string[]? Codecs = null, string[]? Sources = null,
    string[]? Protocols = null, string? Hdr = null, bool Required = false, bool Negate = false);
public sealed record SourceSeedRules(double MinimumRatio = 0, int MinimumMinutes = 0, double DownloadFactor = 1, double UploadFactor = 1, string SeedType = "either");
