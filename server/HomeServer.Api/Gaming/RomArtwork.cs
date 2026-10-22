using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;
public sealed record RomCover(string Name,string Title,string SourceUrl);
public sealed record RomPresentation(string Title,string Category,string? File=null,long Bytes=0,string? Sha256=null,string? SourceUrl=null,string Attribution="User supplied",bool TitleEdited=false,int Revision=0);
public sealed record RomCoverCommand(string Name);
public sealed record RomPresentationCommand(string Title,string Category);
public sealed class RomArtwork(HomePaths paths,MediaStore store,EventHub events,HttpMessageHandler? handler=null):IProductWorkHandler,IDisposable
{
    public string[] Kinds=>["rom-artwork"];
    private readonly HttpClient _http=new(handler??new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};
    private readonly SemaphoreSlim _indexGate=new(1,1);
    private readonly Dictionary<string,(string[] Items,DateTimeOffset At)> _indexes=new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly object _presentationGate=new();
    private string Root(GameRom rom)=>Path.Combine(paths.State,"games","artwork",rom.UserId,rom.Id);
    public RomPresentation Presentation(GameRom rom)=>ReadPresentation(paths,rom);
    public static RomPresentation ReadPresentation(HomePaths paths,GameRom rom)
    {
        var root=Path.Combine(paths.State,"games","artwork",rom.UserId,rom.Id);var path=Path.Combine(root,"presentation.json");if(!File.Exists(path))return new(rom.Title,"");
        path=MediaPaths.ResolveFile(root,"presentation.json");if(new FileInfo(path).Length>8192)return new(rom.Title,"");
        try{var value=JsonSerializer.Deserialize<RomPresentation>(File.ReadAllText(path),Json)??new(rom.Title,"");return value with{Title=string.IsNullOrWhiteSpace(value.Title)?rom.Title:value.Title,Category=value.Category??""};}catch(JsonException){return new(rom.Title,"");}
    }
    public bool HasCover(GameRom rom)=>CoverExists(paths,rom);
    public static bool CoverExists(HomePaths paths,GameRom rom)=>ReadPresentation(paths,rom).File is{} file&&Regex.IsMatch(file,"^[a-f0-9]{64}\\.png$")&&File.Exists(Path.Combine(paths.State,"games","artwork",rom.UserId,rom.Id,file));
    private static string Normalize(string name)
    {
        var text=new string(name.Normalize(NormalizationForm.FormD).Where(character=>CharUnicodeInfo.GetUnicodeCategory(character)!=UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(Regex.Replace(text.ToLowerInvariant(),"\\([^)]*\\)|\\[[^]]*\\]",""),"[^a-z0-9]","");
    }
    public static string CleanTitle(string name)=>Regex.Replace(name,"\\([^)]*\\)|\\[[^]]*\\]","").Trim();
    public static bool SameTitle(string left,string right)=>Normalize(left)==Normalize(right);
    public static string[] ParseDirectory(string html)
    {
        var names=new HashSet<string>(StringComparer.Ordinal);foreach(Match match in Regex.Matches(html,"href\\s*=\\s*(?:\"([^\"]+)\"|'([^']+)')",RegexOptions.IgnoreCase))
        {string name;try{name=Uri.UnescapeDataString(System.Net.WebUtility.HtmlDecode(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value));}catch(UriFormatException){continue;}if(name.Length is <5 or >240||name.IndexOfAny(['/', '\\', ':', '\r', '\n','\0'])>=0||!name.EndsWith(".png",StringComparison.OrdinalIgnoreCase))continue;names.Add(name);if(names.Count>=20000)break;}return names.ToArray();
    }
    public async Task<RomCover[]> SearchAsync(GameRom rom,string? query,CancellationToken token)
    {
        var database=NativeSystems.Require(rom.System).ArtworkDatabase;if(database is null)return [];string[] index;
        await _indexGate.WaitAsync(token);
        try
        {
            if(!_indexes.TryGetValue(database,out var cached)||DateTimeOffset.UtcNow-cached.At>TimeSpan.FromDays(1))
            {
                var data=await FetchAsync(new("https://thumbnails.libretro.com/"+Uri.EscapeDataString(database)+"/Named_Boxarts/"),8*1024*1024,token);
                index=ParseDirectory(System.Text.Encoding.UTF8.GetString(data));
                if(_indexes.Count>=8)_indexes.Remove(_indexes.OrderBy(pair=>pair.Value.At).First().Key);_indexes[database]=(index,DateTimeOffset.UtcNow);
            }
            else index=cached.Items;
        }
        finally{_indexGate.Release();}
        var key=Normalize(query??Presentation(rom).Title);if(key.Length<3)return [];
        return index.Where(name=>Normalize(Path.GetFileNameWithoutExtension(name)).Contains(key,StringComparison.Ordinal)).OrderBy(name=>Normalize(Path.GetFileNameWithoutExtension(name))==key?0:1).ThenBy(name=>name,StringComparer.Ordinal).Take(20).Select(name=>new RomCover(name,Path.GetFileNameWithoutExtension(name),CoverUri(database,name).AbsoluteUri)).ToArray();
    }
    private static Uri CoverUri(string database,string name)
    {
        if(name.Length is <5 or >240||!name.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||name.IndexOfAny(['/', '\\', ':', '\r', '\n'])>=0)throw new DomainException("Kapak seçimi geçersiz.",400);
        return new("https://thumbnails.libretro.com/"+Uri.EscapeDataString(database)+"/Named_Boxarts/"+Uri.EscapeDataString(name));
    }
    private async Task<byte[]> FetchAsync(Uri uri,int limit,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("Home-ROM-Artwork/1.0");
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);if(!response.IsSuccessStatusCode)throw new DomainException("Oyun kapak kaynağına erişilemiyor.",502);
        if(response.Content.Headers.ContentLength>limit)throw new DomainException("Kapak kaynağı yanıtı çok büyük.",413);
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await input.ReadAsync(buffer,token))>0){if(output.Length+read>limit)throw new DomainException("Kapak kaynağı yanıtı çok büyük.",413);output.Write(buffer,0,read);}return output.ToArray();
    }
    public async Task SetCoverAsync(GameRom rom,string name,Account actor,CancellationToken token)
    {
        if(store.GetGameRom(rom.Id,actor) is null)throw new DomainException("ROM bu profile ait değil.",404);
        var database=NativeSystems.Require(rom.System).ArtworkDatabase??throw new DomainException("Bu sistem için kapak kaynağı eşleştirilmedi.",409);var uri=CoverUri(database,name);await SaveImageAsync(rom,await FetchAsync(uri,4*1024*1024,token),uri.AbsoluteUri,"libretro-thumbnails; artwork copyright belongs to the game's publishers/developers",token);
    }
    public async Task SetAutomaticCoverAsync(GameRom rom,string name,CancellationToken token,Action? verifyAuthority=null)
    {var database=NativeSystems.Require(rom.System).ArtworkDatabase??throw new DomainException("Bu sistem için kapak kaynağı eşleştirilmedi.",409);var uri=CoverUri(database,name);var bytes=await FetchAsync(uri,4*1024*1024,token);verifyAuthority?.Invoke();await SaveImageAsync(rom,bytes,uri.AbsoluteUri,"libretro-thumbnails; artwork copyright belongs to the game's publishers/developers",token,true,verifyAuthority);}
    public async Task SaveImageAsync(GameRom rom,byte[] bytes,string? source,string attribution,CancellationToken token,bool automatic=false,Action? verifyAuthority=null)
    {
        if(bytes.Length is <24 or >4*1024*1024||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) is <1 or >8192||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) is <1 or >8192)throw new DomainException("En fazla 4 MiB geçerli PNG kapak seç.",400);
        var root=AppFiles.DirectoryIn(paths.State,"games","artwork",rom.UserId,rom.Id);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));var file=hash+".png";var target=Path.Combine(root,file);
        if(!File.Exists(target)){var temporary=Path.Combine(root,Guid.NewGuid().ToString("N")+".partial");try{await File.WriteAllBytesAsync(temporary,bytes,token);token.ThrowIfCancellationRequested();try{File.Move(temporary,target,false);}catch(IOException)when(File.Exists(target)){VerifiedFilePublication.Require(target,bytes.Length,hash);}}finally{if(File.Exists(temporary))File.Delete(temporary);}}
        VerifiedFilePublication.Require(target,bytes.Length,hash);lock(_presentationGate){token.ThrowIfCancellationRequested();verifyAuthority?.Invoke();if(automatic&&HasCover(rom))return;var previous=Presentation(rom);PackageTrust.AtomicJson(Path.Combine(root,"presentation.json"),previous with{File=file,Bytes=bytes.Length,Sha256=hash,SourceUrl=source,Attribution=attribution,Revision=previous.Revision+1});}events.Publish("games.changed");
    }
    public FileStream Open(GameRom rom)
    {
        var presentation=Presentation(rom);if(presentation.File is null||!Regex.IsMatch(presentation.File,"^[a-f0-9]{64}\\.png$"))throw new DomainException("Oyun kapağı bulunamadı.",404);
        var path=MediaPaths.ResolveFile(Root(rom),presentation.File);VerifiedFilePublication.Require(path,presentation.Bytes,presentation.Sha256!);return File.OpenRead(path);
    }
    public RomPresentation SetPresentation(GameRom rom,RomPresentationCommand input)
    {
        if(string.IsNullOrWhiteSpace(input.Title)||input.Category is null||input.Title.Length>160||input.Category.Length>60||input.Title.Any(char.IsControl)||input.Category.Any(char.IsControl))throw new DomainException("Oyun adı veya kategori geçersiz.",400);
        lock(_presentationGate){var root=AppFiles.DirectoryIn(paths.State,"games","artwork",rom.UserId,rom.Id);var previous=Presentation(rom);var value=previous with{Title=input.Title.Trim(),Category=input.Category.Trim(),TitleEdited=true,Revision=previous.Revision+1};PackageTrust.AtomicJson(Path.Combine(root,"presentation.json"),value);events.Publish("games.changed");return value;}
    }
    public ProductJob Queue(GameRom rom,Account actor)=>store.QueueProductJob("rom-artwork",rom.Id,new{rom.Sha256},actor);
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        var actor=store.RequireProductJob(job);var rom=store.GetGameRom(job.ScopeId??"",actor)??throw new DomainException("ROM bu profile ait değil.",404);
        if(HasCover(rom)){store.FinishProductJob(job,new{found=true});return;}
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(30));
        var matches=await SearchAsync(rom,null,budget.Token);var exact=matches.Where(match=>Normalize(match.Title)==Normalize(rom.Title)).ToArray();
        if(exact.Length==1){store.RequireProductJob(job);await SetAutomaticCoverAsync(rom,exact[0].Name,budget.Token,()=>store.RequireProductJob(job));}
        store.FinishProductJob(job,new{found=HasCover(rom)},message:exact.Length==1?"Oyun kapağı alındı.":"Kapak eşleştirmesi için oyun ayrıntısından kapak seç.");events.Publish("games.changed");
    }
    public void Dispose(){_http.Dispose();_indexGate.Dispose();}
}
