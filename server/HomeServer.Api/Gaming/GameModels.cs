namespace HomeServer.Gaming;

public sealed record GameAsset(string Path, long Size, string Sha256);
public sealed record GameManifest(string Id, string Title, string Publisher, string SourceUrl, string SourceRepository,
    string SourceCommit, string Version, string Engine, string License, string Kind, string Aspect,
    string[] Genres, string[] SavePaths, GameAsset[] Files, string Runtime = "godot");
public sealed record GameUserState(string GameId, bool InLibrary, bool Favorite, string? LastPlayedAt, long PlayedSeconds,
    string? SaveVersion, int SaveRevision, bool HasSave);
public sealed record GameEntry(string Id, string Title, string Publisher, string SourceUrl, string SourceRepository,
    string SourceCommit, string Version, string Engine, string License, string Kind, string Aspect,
    string[] Genres, long SizeBytes, GameUserState State, bool Installed = true, bool InStore = false,
    string Runtime = "godot", string? InstallJobId = null, string? InstallStatus = null, int InstallProgress = 0, string? InstallMessage = null,
    bool Playable=true,string? System=null,string? BlockReason=null,string? RomSha256=null,bool HasArtwork=false,string? Category=null,string? ImportedAt=null,string? ArtworkStatus=null,string? CoreId=null,string? CoreName=null,string? SystemLabel=null,RomMetadataDocument? Metadata=null,string? UninstallStatus=null,int UninstallProgress=0,string? UninstallMessage=null);
public sealed record GamePreference(bool InLibrary, bool Favorite);
public sealed record GameStart(string ClientId, string Ticket);
public sealed record GamePulse(bool Running, int? SaveRevision = null, Dictionary<string, string>? Files = null);
public sealed record GameCheckpoint(int Revision, Dictionary<string, string> Files, string? Version);
public sealed record GameSession(string Id, string UserId, string LoginId, string GameId, string GameVersion,
    string LastSeenAt, string ExpiresAt, bool Running, string? ClosedAt);
public sealed record GameLaunch(string Id, string Url, string Version, string ExpiresAt, GameCheckpoint Checkpoint);
public sealed record GameSourceFile(string Path, long Size, string GitBlobSha1);
public sealed record GameStoreDefinition(string Id, string Title, string Publisher, string Repository, string Commit,
    string License, string LicensePath, string SourceUrl, string Adapter, string Input, string Version,
    string ArchiveUrl, string ArchiveRoot, string[] Genres, string[] Scripts, GameSourceFile[] SourceFiles,
    string ArtworkSha256, long ExpandedBytes);
public sealed record GameStoreDocument(int Format, string ReviewedAt, GameStoreDefinition[] Games);
