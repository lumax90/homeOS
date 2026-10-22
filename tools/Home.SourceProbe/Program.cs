using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Sources;
using Microsoft.AspNetCore.WebUtilities;

if(args.Length<1)throw new ArgumentException("Usage: Home.SourceProbe <evidence.json> [provider-id ...] — public metadata/caps reads only.");
var selected=args.Skip(1).ToHashSet(StringComparer.Ordinal);
if(selected.Any(id=>!SourceCatalog.Builtins.Any(provider=>provider.Id==id)))throw new ArgumentException("Unknown provider id.");
var results=new ConcurrentBag<ProviderRead>();
using var http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate,ConnectTimeout=TimeSpan.FromSeconds(10)}){Timeout=TimeSpan.FromSeconds(25)};
http.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/"+BuildInfo.Version);
await Parallel.ForEachAsync(SourceCatalog.Builtins.Where(provider=>selected.Count==0||selected.Contains(provider.Id)),new ParallelOptions{MaxDegreeOfParallelism=2},async(provider,token)=>
{
    using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(30));token=budget.Token;
    var address=provider.DefaultEndpoint!;int? status=null;
    try
    {
        SourceAdapterManifest? manifest=null;
        if(provider.AdapterId is{}adapter)
        {
            manifest=SourceAdapterRegistry.Builtins.Single(value=>value.Id==adapter);var parameters=SourceAdapterRegistry.ProbeParameters(manifest);
            if(adapter=="yts")parameters["imdbid"]="0063350";
            if(adapter=="internet-archive")parameters["q"]="Big Buck Bunny";
            var source=new IndexerSecret(new("readonly",provider.Name,address,provider.Protocol,false,false,25,"","",null,null,null,null,null),"");
            address=SourceAdapterRegistry.Request(manifest,source,parameters,"").Url;
        }
        else address=QueryHelpers.AddQueryString(address,"t","caps");
        var uri=new Uri(address);
        if(uri.Scheme!="https"||uri.AbsolutePath.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase)||uri.AbsolutePath.EndsWith(".nzb",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Only HTTPS metadata/caps endpoints are allowed.");
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);status=(int)response.StatusCode;response.EnsureSuccessStatusCode();
        await using var body=await response.Content.ReadAsStreamAsync(token);var bytes=await MetadataProvider.ReadLimitedAsync(body,8*1024*1024,token);
        var page=manifest is null?null:SourceAdapterRegistry.Parse(manifest,bytes,new Uri(provider.DefaultEndpoint!),0);
        var caps=manifest is null?ZnabParser.Capabilities(bytes):SourceAdapterRegistry.Capabilities(manifest);
        if(caps.Searches.Count==0)throw new InvalidOperationException("No search capabilities were reported.");
        results.Add(new(provider.Id,address,status,bytes.Length,page?.Items.Count,caps.Searches.Keys.ToArray(),caps.Categories.Count,true,provider.RequiresApiKey,null));
    }
    catch(Exception ex){results.Add(new(provider.Id,address,status,null,null,[],0,false,provider.RequiresApiKey,ex is DomainException?ex.Message:ex.GetType().Name));}
});
var rows=results.OrderBy(value=>value.Id).ToArray();var path=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{recordedAt=DateTimeOffset.UtcNow,version=BuildInfo.Version,schema=MediaStore.LatestSchema,metadataOnly=true,mediaTransfersInitiated=0,passed=rows.Count(value=>value.Passed),failed=rows.Count(value=>!value.Passed),providers=rows},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
foreach(var row in rows)Console.WriteLine($"{row.Id}: {(row.Passed?"PASS":"FAIL")} · HTTP {row.Status} · {row.Error??(row.CredentialsPending?"caps verified; account search remains user-owned":$"{row.Releases} normalized releases")}");
Environment.ExitCode=rows.All(value=>value.Passed)?0:1;

internal sealed record ProviderRead(string Id,string Address,int? Status,int? Bytes,int? Releases,string[] Searches,int Categories,bool Passed,bool CredentialsPending,string? Error);
