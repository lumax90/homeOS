using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Music;

public sealed record MusicEnrichmentProgress(int Generation=0,int Offset=0,int Total=0,int Checked=0,int Matched=0,int Review=0,int Failed=0,string? UpdatedAt=null,string? NextPass=null);
public sealed class MusicEnrichmentWorker(HomePaths paths,MediaStore store,MusicExperiencePreferences preferences,MusicOnlineMetadata metadata,CoverArtCache covers,EventHub events,ILogger<MusicEnrichmentWorker> logger):BackgroundService
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private int _account;
    private string FilePath(string user)=>Path.Combine(paths.State,"music","enrichment",user+".json");
    public MusicEnrichmentProgress Status(string user)
    {
        if(!Gaming.NativeSystems.SafeSegment(user))throw new DomainException("Müzik bilgisi kapsamı geçersiz.");var path=FilePath(user);if(!File.Exists(path))return new();path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,path));if(new FileInfo(path).Length>8192)return new();
        try{return JsonSerializer.Deserialize<MusicEnrichmentProgress>(File.ReadAllText(path),Json)??new();}catch(JsonException){return new();}
    }
    public async Task<int> FillAsync(CancellationToken token)
    {
        var actors=store.GetAccounts().Where(MusicAccess.Online).ToArray();if(actors.Length==0)return 0;
        for(var attempt=0;attempt<actors.Length;attempt++)
        {
            _account%=actors.Length;var actor=actors[_account++];var options=preferences.Read(actor.Id);if(!options.OnlineMetadata&&!options.OnlineCovers)continue;
            var progress=Status(actor.Id);if(progress.Generation!=options.MetadataGeneration)progress=new(Generation:options.MetadataGeneration);
            if(DateTimeOffset.TryParse(progress.NextPass,out var next)&&next>DateTimeOffset.UtcNow)continue;
            if(progress.NextPass is not null)progress=new(Generation:options.MetadataGeneration);
            var page=store.GetMusicAlbums(actor,progress.Offset);var count=0;
            foreach(var album in page.Items.Take(4))
            {
                token.ThrowIfCancellationRequested();var latest=preferences.Read(actor.Id);if(!latest.OnlineMetadata&&!latest.OnlineCovers||latest.MetadataGeneration!=options.MetadataGeneration)break;
                var info=await metadata.ResolveAsync(album,actor,token);var failed=info?.Status=="retry";
                if(latest.OnlineCovers&&album.ArtworkMediaId is null&&(album.ReleaseMbid??album.ReleaseGroupMbid??info?.GroupId) is{} key)
                {
                    try{if(preferences.Read(actor.Id).OnlineCovers&&store.GetAccount(actor.Id) is{} current&&MusicAccess.Online(current))await covers.GetAsync(album.ReleaseMbid is null?"release-group":"release",key,token);}
                    catch(DomainException){failed=true;}
                }
                count++;progress=progress with{Offset=progress.Offset+1,Total=page.Total,Checked=progress.Checked+1,Matched=progress.Matched+(info?.Album is null?0:1),Review=progress.Review+(info?.Status=="unmatched"?1:0),Failed=progress.Failed+(failed?1:0),UpdatedAt=DateTimeOffset.UtcNow.ToString("O")};
                Save(actor.Id,progress);
            }
            if(page.Items.Count==0||progress.Offset>=page.Total)
            {
                // Receipts retain per-album backoff across sweeps/restarts. The next sweep discovers newly scanned/imported albums.
                progress=progress with{Offset=0,Total=page.Total,NextPass=DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")};Save(actor.Id,progress);
            }
            if(count>0)events.Publish("music.enrichment.changed");return count;
        }
        return 0;
    }
    private void Save(string user,MusicEnrichmentProgress progress){AppFiles.DirectoryIn(paths.State,"music","enrichment");PackageTrust.AtomicJson(FilePath(user),progress);}
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(12));
        while(await timer.WaitForNextTickAsync(token))try{await FillAsync(token);}catch(Exception error)when(error is IOException or DomainException or JsonException or UnauthorizedAccessException){logger.LogWarning("Müzik bilgisi taraması sürdürülemedi: {Type}",error.GetType().Name);}
    }
}
