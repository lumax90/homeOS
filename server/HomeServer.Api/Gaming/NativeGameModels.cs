namespace HomeServer.Gaming;

public sealed record GameRom(string Id, string UserId, string System, string FileName, string Title, string Sha256,
    long SizeBytes, string ImportedAt);
public sealed record NativePackageCandidate(string Id, string Version, string License, string SourceCommit,
    string SourceUrl, string? ArchiveUrl, string? ArchiveFileName, string? ArchiveSha256);
public sealed record NativePackageInventory(NativePackageCandidate Candidate, string Status);
public sealed record NativeGamingStatus(string HostPlatform, string[] ImportSystems, long MaximumRomBytes,
    bool ExecutionAvailable,bool StreamingImplemented,NativePackageInventory[] Components,string[] Blockers);
