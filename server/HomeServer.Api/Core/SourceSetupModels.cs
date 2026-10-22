namespace HomeServer.Core;

public sealed record SourceProvider(string Id,string Name,string Description,string Protocol,string Access,string[] Kinds,string? DefaultEndpoint,string? AdapterId,string Homepage,string HelpUrl,bool RequiresApiKey,bool Installed=false);
public sealed record SourceSetupInput(string CommandId,string ProviderId,string? Name=null,string? Endpoint=null,string? ApiKey=null,int Priority=25,bool Enabled=true,SourceNetworkSettings? Network=null,string? ExistingIndexerId=null,int? ExpectedRevision=null);
public sealed record SourceSetupConnect(string Id,int Revision);
public sealed record SourceSetupPreview(string Id,string ProviderId,string Name,string Endpoint,string Status,int Revision,string ExpiresAt,string[] SearchTypes,int CategoryCount,string? Error,string? IndexerId,bool NeedsUsenetServer);
public sealed record SourceSetup(string Id,string UserId,string LoginId,int UserVersion,string ProviderId,string DefinitionHash,string RequestHash,string? IndexerId,string? ExistingIndexerId,int? ExpectedRevision,bool Enabled,string Status,int Revision,string? Lease,string? Error,string ExpiresAt);
public sealed record SourceEditSettings(SourceProvider Provider,IndexerInfo Indexer,SourceNetworkSettings Network,bool ApiKeyConfigured,bool ProxyPasswordConfigured,bool CookieConfigured,bool HeadersConfigured);
