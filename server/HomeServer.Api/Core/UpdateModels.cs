namespace HomeServer.Core;

public sealed record PackageFile(string Path, long Length, string Sha256, bool Executable = false);
public sealed record PackageManifest(int Format, string Version, string Runtime, int Schema, string EntryPoint, PackageFile[] Files);
public sealed record PreparedUpdate(string Id, string Version, string Runtime, string CreatedAt);
public sealed record ActiveRelease(string Directory, string Version, string ActivatedAt);
public sealed record ReleaseRollback(string Directory, string Version, string BackupId);
