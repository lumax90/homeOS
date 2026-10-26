namespace HomeServer.Core;

public sealed record CatalogPerson(string Provider,int Id,string Name,string? Image,string? Department,string? Biography,string Url);
public sealed record CatalogCredit(CatalogPerson Person,string Role,string Department);
public sealed record CatalogTrailer(string Key,string Name,string Site,string Type,bool Official,string? Language);
public sealed record CatalogImage(string Url,string PreviewUrl,int Width,int Height);
public sealed record CatalogMedia(CatalogTrailer[] Trailers,CatalogImage[] Images,string? TrailerError=null,string? ImageError=null);
public sealed record PersonWork(ProviderContent Metadata,string[] Roles);
public sealed record CatalogFeedInput(string Provider,string Kind,string ExternalId,string CollectionId,bool Enabled=false,bool RequestNew=false,string RoleFilter="all",string ProfileId="balanced",int ExpectedRevision=0);
public sealed record CatalogFeed(string Id,string UserId,string Provider,string Kind,string ExternalId,string Name,string CollectionId,bool Enabled,bool RequestNew,string RoleFilter,string ProfileId,int Revision,string NextRun,string? LastRun,string? LastError);
public sealed record CatalogFeedPage(string Name,ProviderContent[] Items,int Page,bool More);
public sealed record CatalogFeedItem(string Provider,string Kind,int ProviderId,ProviderContent Metadata,string? ContentId,bool Excluded);
public sealed record FeedException(string Provider,string Kind,int ProviderId,bool Excluded,int Revision);
