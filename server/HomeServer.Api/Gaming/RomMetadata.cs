using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;

public sealed record RomMetadataDocument(int Format,int Revision,string RomSha256,RomMetadataCandidate Game,string MatchMethod,string FetchedAt,string Provider="Hasheous");
public sealed record RomMetadataChoices(string RomSha256,RomMetadataCandidate[] Items);
public sealed record RomMetadataSelect(long Id,int Revision);
public sealed record RomMetadataSearch(string Query);
public sealed record RomMetadataAttempt(int Format,string RomSha256,int Pipeline,int Generation,string Outcome,int Failures,string NextAttemptAt);
public sealed class RomMetadata(HomePaths paths,MediaStore store,EventHub events,RomArtwork artwork,HasheousMetadataClient client,GameLibraryPreferences? preferences=null):IProductWorkHandler
{
    public const int PipelineVersion=3;
    public string[] Kinds=>["rom-metadata"];
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);private readonly object _gate=new();
    private string Root(GameRom rom)=>Path.Combine(paths.State,"games","metadata",rom.UserId,rom.Id);
    private GameLibraryOptions Options(string user)=>preferences?.Read(user)??new();
    private RomMetadataAttempt? Attempt(GameRom rom)
    {var path=Path.Combine(Root(rom),"automation.json");if(!File.Exists(path))return null;path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,path));if(new FileInfo(path).Length>8192)throw new DomainException("Oyun eşleşme kaydı geçersiz.",409);var data=JsonSerializer.Deserialize<RomMetadataAttempt>(File.ReadAllText(path),Json);return data?.Format==1&&data.RomSha256==rom.Sha256?data:null;}
    public bool ShouldEnrich(GameRom rom,Account actor,GameLibraryOptions options,DateTimeOffset now)
    {
        if(!options.AutomaticMetadata||rom.UserId!=actor.Id)return false;
        if(store.GetProductJobs(actor,"rom-metadata",rom.Id).Any(j=>j.Status is "queued" or "running"))return false;
        var attempt=Attempt(rom);if(attempt is null||attempt.Pipeline!=PipelineVersion||attempt.Generation!=options.RefreshGeneration)return true;
        return attempt.Outcome!="complete"&&DateTimeOffset.TryParse(attempt.NextAttemptAt,out var next)&&now>=next;
    }
    public object LibraryStatus(Account actor)
    {
        var roms=store.GetGameRoms(actor);var library=store.GetGameStates(actor.Id).Where(g=>g.InLibrary).Select(g=>g.GameId).ToHashSet(StringComparer.Ordinal);var total=0;var complete=0;var attention=0;var failures=0;var waiting=0;var options=Options(actor.Id);var now=DateTimeOffset.UtcNow;
        foreach(var rom in roms.Where(r=>library.Contains(r.Id))){total++;try{var attempt=Attempt(rom);if(attempt is null||attempt.Pipeline!=PipelineVersion||attempt.Generation!=options.RefreshGeneration||attempt.Outcome!="complete"&&DateTimeOffset.TryParse(attempt.NextAttemptAt,out var next)&&now>=next)waiting++;if(Read(paths,rom) is not null&&artwork.HasCover(rom)){complete++;continue;}if(attempt?.Outcome=="failed")failures++;else if(attempt?.Outcome=="unmatched")attention++;}catch(Exception error)when(error is DomainException or IOException or JsonException){failures++;}}
        var active=store.GetProductJobs(actor,"rom-metadata").Count(j=>j.Status is "queued" or "running");return new{options,total,complete,active,attention,failures,waiting,remaining=Math.Max(0,total-complete),provider="Hasheous + libretro",keyRequired=false};
    }
    public static RomMetadataDocument? Read(HomePaths paths,GameRom rom)
    {
        var root=Path.Combine(paths.State,"games","metadata",rom.UserId,rom.Id);if(!File.Exists(Path.Combine(root,"metadata.json")))return null;var path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,Path.Combine(root,"metadata.json")));if(new FileInfo(path).Length>64*1024)throw new DomainException("Oyun bilgisi kaydı geçersiz.",409);
        try{var data=JsonSerializer.Deserialize<RomMetadataDocument>(File.ReadAllText(path),Json);return data is{Format:1,Revision:>0}&&data.RomSha256==rom.Sha256?data:null;}catch(JsonException){throw new DomainException("Oyun bilgisi kaydı geçersiz.",409);}
    }
    private RomMetadataChoices Choices(GameRom rom)
    {if(!File.Exists(Path.Combine(Root(rom),"choices.json")))return new(rom.Sha256,[]);var path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,Path.Combine(Root(rom),"choices.json")));if(new FileInfo(path).Length>256*1024)throw new DomainException("Oyun eşleşme kaydı geçersiz.",409);var data=JsonSerializer.Deserialize<RomMetadataChoices>(File.ReadAllText(path),Json);return data?.RomSha256==rom.Sha256?data:new(rom.Sha256,[]);}
    public object Status(GameRom rom,Account actor)
    {var job=store.GetProductJobs(actor,"rom-metadata",rom.Id).FirstOrDefault();return new{data=Read(paths,rom),candidates=Choices(rom).Items,status=job?.Status,message=job?.Message};}
    private void StoreChoices(GameRom rom,RomMetadataCandidate[] items)
    {lock(_gate){var list=items.Take(20).Select(c=>c with{Description=c.Description?.Length>2000?c.Description[..2000]:c.Description}).ToList();while(list.Count>0&&JsonSerializer.SerializeToUtf8Bytes(new RomMetadataChoices(rom.Sha256,list.ToArray()),Json).Length>256*1024)list.RemoveAt(list.Count-1);var root=AppFiles.DirectoryIn(paths.State,"games","metadata",rom.UserId,rom.Id);PackageTrust.AtomicJson(Path.Combine(root,"choices.json"),new RomMetadataChoices(rom.Sha256,list.ToArray()));}}
    public async Task<RomMetadataCandidate[]> SearchAsync(GameRom rom,string query,Account actor,string login,CancellationToken token)
    {store.RequireRomImportActor(actor,login);var items=await client.SearchAsync(rom.System,query,token);store.RequireRomImportActor(actor,login);StoreChoices(rom,items);return items;}
    public ProductJob Queue(GameRom rom,Account actor,bool refresh=false,bool automatic=false)
    {lock(_gate){if(rom.UserId!=actor.Id)throw new DomainException("ROM bu profile ait değil.",404);var active=store.GetProductJobs(actor,"rom-metadata",rom.Id).FirstOrDefault(j=>j.Status is "queued" or "running");return active??store.QueueProductJob("rom-metadata",rom.Id,new{rom.Sha256,revision=Read(paths,rom)?.Revision??0,request=refresh?Guid.NewGuid().ToString("N"):"import",automatic,pipeline=PipelineVersion,generation=Options(actor.Id).RefreshGeneration},actor);}}
    private RomMetadataDocument Save(GameRom rom,RomMetadataCandidate game,string method,int expected)
    {
        lock(_gate)
        {
            var current=Read(paths,rom);if((current?.Revision??0)!=expected)throw new DomainException("Oyun eşleşmesi değişti; yeniden yükle.",409);if(!HasheousMetadataClient.PlatformMatches(rom.System,game.Platform)||string.IsNullOrWhiteSpace(game.Name))throw new DomainException("Oyun bilgisi seçilen konsolla eşleşmiyor.",409);
            var root=AppFiles.DirectoryIn(paths.State,"games","metadata",rom.UserId,rom.Id);var data=new RomMetadataDocument(1,expected+1,rom.Sha256,game,method,DateTimeOffset.UtcNow.ToString("O"));PackageTrust.AtomicJson(Path.Combine(root,"metadata.json"),data);events.Publish("games.changed");return data;
        }
    }
    public async Task<RomMetadataDocument> SelectAsync(GameRom rom,RomMetadataSelect selection,Account actor,string login,CancellationToken token)
    {
        store.RequireRomImportActor(actor,login);var choice=Choices(rom).Items.FirstOrDefault(c=>c.Id==selection.Id)??throw new DomainException("Önce oyun eşleşmelerini ara.",409);var details=await client.DetailsAsync(rom.System,choice,token);store.RequireRomImportActor(actor,login);var result=Save(rom,details,"manual",selection.Revision);await TryCoverAsync(rom,details,token,()=>{store.RequireRomImportActor(actor,login);RequireRevision(rom,result.Revision);});return result;
    }
    private void RequireRevision(GameRom rom,int revision){if(Read(paths,rom)?.Revision!=revision)throw new DomainException("Oyun eşleşmesi değişti; yeniden yükle.",409);}
    private async Task TryCoverAsync(GameRom rom,RomMetadataCandidate details,CancellationToken token,Action? verifyAuthority=null)
    {
        if(artwork.HasCover(rom))return;try{var covers=await artwork.SearchAsync(rom,details.Name,token);var cover=RomMatching.Cover(covers,details.Name,rom.FileName,Options(rom.UserId).CoverRegion,details.Regions);if(cover is not null)await artwork.SetAutomaticCoverAsync(rom,cover.Name,token,verifyAuthority);}catch(DomainException){/* Keep the confirmed identity if artwork is unavailable; automation retries missing covers later. */}
    }
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        var actor=store.RequireProductJob(job);var rom=store.GetGameRom(job.ScopeId??"",actor)??throw new DomainException("ROM bu profile ait değil.",404);
        try
        {
            RequireJob(job);await RunCoreAsync(job,token);
            if(token.IsCancellationRequested)return;var completed=store.GetProductJob(job.Id);if(completed?.Status!="completed")return;
            var match=Read(paths,rom);WriteAttempt(rom,job,match is not null&&artwork.HasCover(rom)?"complete":match is null?"unmatched":"artwork",0,DateTimeOffset.UtcNow.AddDays(7));
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested&&store.GetProductJob(job.Id)?.Status=="running")
        {
            var failures=Math.Min((Attempt(rom)?.Failures??0)+1,10);WriteAttempt(rom,job,"failed",failures,DateTimeOffset.UtcNow.AddMinutes(Math.Min(360,Math.Pow(2,failures)*2)));
            throw new DomainException("Oyun bilgisi kaynağı geçici bir hata döndürdü. Otomatik olarak yeniden denenecek.",502);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)when(error is DomainException or HttpRequestException or IOException or JsonException)
        {
            var failures=Math.Min((Attempt(rom)?.Failures??0)+1,10);var next=DateTimeOffset.UtcNow.AddMinutes(Math.Min(360,Math.Pow(2,failures)*2));WriteAttempt(rom,job,"failed",failures,next);throw;
        }
    }
    private void WriteAttempt(GameRom rom,ProductJob job,string outcome,int failures,DateTimeOffset next)
    {lock(_gate){var root=AppFiles.DirectoryIn(paths.State,"games","metadata",rom.UserId,rom.Id);var generation=job.Input.TryGetProperty("generation",out var input)&&input.TryGetInt32(out var value)?value:0;PackageTrust.AtomicJson(Path.Combine(root,"automation.json"),new RomMetadataAttempt(1,rom.Sha256,PipelineVersion,generation,outcome,failures,next.ToString("O")));}}
    private Account RequireJob(ProductJob job)
    {var actor=store.RequireProductJob(job);GameInstaller.RequireInstallAccount(actor);if(IsAutomatic(job)&&!Options(actor.Id).AutomaticMetadata)throw new OperationCanceledException();return actor;}
    public static bool IsAutomatic(ProductJob job)=>job.Input.TryGetProperty("automatic",out var auto)?auto.ValueKind==JsonValueKind.True:job.Input.TryGetProperty("request",out var request)&&request.ValueKind==JsonValueKind.String&&request.GetString()=="import";
    private async Task RunCoreAsync(ProductJob job,CancellationToken token)
    {
        var actor=RequireJob(job);var rom=store.GetGameRom(job.ScopeId??"",actor)??throw new DomainException("ROM bu profile ait değil.",404);var revision=job.Input.GetProperty("revision").GetInt32();
        var previous=Read(paths,rom);
        if((previous?.Revision??0)!=revision){store.FinishProductJob(job,new{preserved=true},message:"Yeni oyun eşleşmesi korundu.");return;}
        if(previous is not null&&artwork.HasCover(rom)&&job.Input.GetProperty("request").GetString()=="import"){store.FinishProductJob(job,new{preserved=true},message:"Mevcut oyun bilgisi korundu.");return;}
        if(previous is not null)
        {
            var details=await client.DetailsAsync(rom.System,previous.Game,token);RequireJob(job);var saved=Save(rom,details,previous.MatchMethod,revision);await TryCoverAsync(rom,details,token,()=>{RequireJob(job);RequireRevision(rom,saved.Revision);});store.FinishProductJob(job,new{matched=true,cover=artwork.HasCover(rom)},message:"Oyun bilgisi alındı.");return;
        }
        var fingerprint=await FingerprintAsync(rom,token);using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(60));
        var matches=await client.LookupAsync(rom,fingerprint,budget.Token);RequireJob(job);StoreChoices(rom,matches);var selected=RomMatching.Automatic(matches,rom.Title,false);var method="hash";
        if(matches.Length==0)
        {
            var query=RomMatching.SearchTitle(rom.Title);if(query.Length>=3){var candidates=await client.SearchAsync(rom.System,query,budget.Token);RequireJob(job);StoreChoices(rom,candidates);selected=RomMatching.Automatic(candidates,rom.Title,Options(actor.Id).MatchByName);method="name";}
        }
        if(selected is not null)
        {
            var details=await client.DetailsAsync(rom.System,selected,budget.Token);RequireJob(job);
            try{var saved=Save(rom,details,method,revision);await TryCoverAsync(rom,details,budget.Token,()=>{RequireJob(job);RequireRevision(rom,saved.Revision);});}catch(DomainException error)when(error.StatusCode==409){store.FinishProductJob(job,new{preserved=true},message:"Yeni oyun eşleşmesi korundu.");return;}
        }
        store.FinishProductJob(job,new{matched=Read(paths,rom) is not null,cover=artwork.HasCover(rom)},message:Read(paths,rom) is not null?(artwork.HasCover(rom)?"Oyun bilgisi ve kapak alındı.":"Oyun bilgisi alındı; kapak için seçim gerekebilir."):"Oyun bilgisi eşleştirmesi için bir sonuç seç.");events.Publish("games.changed");
    }
    public async Task<RomFingerprint> FingerprintAsync(GameRom rom,CancellationToken token)
    {
        string file;long size;string expected;var root=Path.Combine(paths.State,"games","roms",rom.System,rom.Sha256);
        if(NativeSystems.Extension(rom.FileName) is "cue" or "m3u" or "gdi")
        {
            var manifestPath=MediaPaths.ResolveFile(root,"disc.json");if(new FileInfo(manifestPath).Length>64*1024)throw new DomainException("Disk paketi manifesti geçersiz.",409);var bytes=await File.ReadAllBytesAsync(manifestPath,token);if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=rom.Sha256)throw new DomainException("Disk paketi bütünlüğü değişti.",409);
            var manifest=JsonSerializer.Deserialize<NativeDiscManifest>(bytes,Json)??throw new DomainException("Disk paketi manifesti geçersiz.",409);if(manifest.System!=rom.System||manifest.Entry!=rom.FileName)throw new DomainException("Disk paketi manifesti geçersiz.",409);await NativeRomLibrary.ValidateDiscReferencesAsync(manifest,root,token);
            NativeDiscFile? binary=null;
            var cue=manifest.Files.FirstOrDefault(f=>f.Name.Equals(manifest.Entry,StringComparison.OrdinalIgnoreCase)&&NativeSystems.Extension(f.Name)=="cue")??manifest.Files.FirstOrDefault(f=>NativeSystems.Extension(f.Name)=="cue");
            if(cue is not null)
            {
                var cuePath=MediaPaths.ResolveFile(root,cue.Name);VerifiedFilePublication.Require(cuePath,cue.Bytes,cue.Sha256);string? currentFile=null;
                foreach(var line in (await File.ReadAllTextAsync(cuePath,token)).Split('\n'))
                {
                    var match=System.Text.RegularExpressions.Regex.Match(line,"^\\s*FILE\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+BINARY\\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success)currentFile=match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value;
                    if(currentFile is not null&&System.Text.RegularExpressions.Regex.IsMatch(line,@"^\s*TRACK\s+\d+\s+MODE[12]/\d+\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){binary=manifest.Files.FirstOrDefault(f=>f.Name.Equals(currentFile,StringComparison.OrdinalIgnoreCase));break;}
                }
            }
            else binary=manifest.Files.Where(f=>NativeSystems.Extension(f.Name) is "iso" or "img" or "chd" or "pbp").OrderByDescending(f=>f.Bytes).FirstOrDefault();
            if(binary is null)throw new DomainException("Bu disk paketi için oyun özeti çıkarılamıyor.",409);file=binary.Name;size=binary.Bytes;expected=binary.Sha256;
        }else{file=NativeSystems.ContentName(rom);size=rom.SizeBytes;expected=rom.Sha256;}
        await using var input=File.OpenRead(MediaPaths.ResolveFile(root,file));if(input.Length!=size)throw new DomainException("ROM kopyası bütünlük kontrolünden geçemedi.",409);
        using var sha256=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var md5=IncrementalHash.CreateHash(HashAlgorithmName.MD5);using var sha1=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);var buffer=new byte[65536];int read;while((read=await input.ReadAsync(buffer,token))>0){sha256.AppendData(buffer,0,read);md5.AppendData(buffer,0,read);sha1.AppendData(buffer,0,read);}var strong=Convert.ToHexStringLower(sha256.GetHashAndReset());if(strong!=expected)throw new DomainException("ROM kopyası bütünlük kontrolünden geçemedi.",409);return new(file,size,strong,Convert.ToHexStringLower(md5.GetHashAndReset()),Convert.ToHexStringLower(sha1.GetHashAndReset()));
    }
}
