using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

/// <summary>Bounded refill of the durable metadata lane. Rescans after restart; per-ROM receipts prevent retry storms.</summary>
public sealed class RomMetadataAutomation(MediaStore store,RomMetadata metadata,GameLibraryPreferences preferences,ILogger<RomMetadataAutomation> logger):BackgroundService
{
    private readonly Dictionary<string,string> _cursor=new(StringComparer.Ordinal);
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(12));
        do{try{Fill();}catch(Exception error)when(error is IOException or DomainException or System.Text.Json.JsonException){logger.LogWarning("Oyun bilgisi kuyruğu hazırlanamadı: {Type}",error.GetType().Name);}}while(await timer.WaitForNextTickAsync(token));
    }
    public int Fill()
    {
        var room=Math.Min(8,Math.Min(12-store.PendingProductJobCount("rom-metadata"),96-store.PendingProductJobCount()));if(room<=0)return 0;var added=0;
        foreach(var actor in store.GetAccounts().Where(a=>a.Enabled&&(a.IsAdmin||a.CanAcquire)))
        {
            var options=preferences.Read(actor.Id);if(!options.AutomaticMetadata)continue;
            var page=store.GameRomsForMetadata(actor,_cursor.GetValueOrDefault(actor.Id,""),64);
            if(page.Count==0){_cursor[actor.Id]="";continue;}
            foreach(var rom in page)
            {
                if(added>=room)return added;_cursor[actor.Id]=rom.Id;
                try{if(!metadata.ShouldEnrich(rom,actor,options,DateTimeOffset.UtcNow))continue;metadata.Queue(rom,actor,true,true);added++;}
                catch(DomainException error)when(error.StatusCode==429){return added;}
                catch(Exception error)when(error is DomainException or IOException or System.Text.Json.JsonException){logger.LogWarning("Oyun bilgisi girdisi atlandı {Game}: {Type}",rom.Id,error.GetType().Name);}
            }
            if(page.Count<64)_cursor[actor.Id]="";
        }
        return added;
    }
}
