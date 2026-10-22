namespace HomeServer.Core;
public sealed record SourceAdapterManifest(int SchemaVersion,string Id,string Name,string Version,string License,string Homepage,string Format,string Protocol,string SearchPath,string QueryParameter="q",string OffsetParameter="offset",string LimitParameter="limit",string ItemsPath="",Dictionary<string,string>? Fields=null,Dictionary<string,string>? Parameters=null,string? DownloadTemplate=null,string Authentication="none",string CredentialName="apikey",int PageSize=50);
public sealed record SourceNetworkSettings(string? ProxyUrl=null,string? ProxyUsername=null,string? ProxyPassword=null,string? Cookie=null,Dictionary<string,string>? Headers=null,int DailyQueries=1000,int DailyGrabs=100);
public sealed record SourceTransportInput(string? AdapterId,SourceNetworkSettings Settings,int Revision=0,bool ClearProxyPassword=false,bool ClearCookie=false,bool ClearHeaders=false);
public sealed record SourceTransport(string? AdapterId,SourceNetworkSettings Settings,int Revision);
public sealed record AdapterReleasePage(IReadOnlyList<RawRelease> Items,bool More,string? NextCursor=null);
