using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Sources;

public sealed class SourceSetupService(MediaStore store,SourceCatalog catalog,IndexerService indexers,SourceTransportService network,EventHub events,IHostApplicationLifetime application,ILogger<SourceSetupService> logger):BackgroundService
{
    private readonly SemaphoreSlim _tests=new(2,2);
    public SourceEditSettings Edit(string id)
    {
        store.RequirePublishedIndexer(id);var source=indexers.List().Single(value=>value.Id==id);var transport=network.Read(id);var provider=catalog.Find(source.ProviderId??"custom-"+source.Protocol);
        if(provider.AdapterId!=transport.AdapterId)provider=transport.AdapterId is{}adapter?catalog.List().FirstOrDefault(value=>value.AdapterId==adapter)??catalog.Find("custom-"+source.Protocol):catalog.Find("custom-"+source.Protocol);
        var settings=transport.Settings;
        return new(provider,source,settings with{ProxyPassword=null,Cookie=null,Headers=null},!string.IsNullOrEmpty(indexers.ReadKey(id)),!string.IsNullOrEmpty(settings.ProxyPassword),!string.IsNullOrEmpty(settings.Cookie),settings.Headers?.Count>0);
    }
    private static bool SameOrigin(string left,string right)
    {
        var a=new Uri(left);var b=new Uri(right);return a.Scheme==b.Scheme&&a.IdnHost==b.IdnHost&&a.Port==b.Port;
    }
    public async Task<SourceSetupPreview> TestAsync(SourceSetupInput input,string userId,string loginId,CancellationToken token)
    {
        _=AppFiles.Id(input.CommandId);var provider=catalog.Find(input.ProviderId);
        IndexerSecret? previous=null;SourceNetworkSettings? oldNetwork=null;
        if(input.ExistingIndexerId is{}id){store.RequirePublishedIndexer(id);previous=store.GetIndexer(id);if(previous.Info.Revision!=input.ExpectedRevision)throw new DomainException("Kaynak değişti; düzenlemeyi yeniden aç.",409);oldNetwork=network.Read(id).Settings;}
        var endpoint=(input.Endpoint??previous?.Info.Endpoint??provider.DefaultEndpoint)?.Trim()??throw new DomainException("Kaynağın bağlantı adresini gir.");
        var name=string.IsNullOrWhiteSpace(input.Name)?previous?.Info.Name??provider.Name:input.Name.Trim();
        var acceptsKey=provider.AdapterId is null||SourceAdapterRegistry.Find(provider.AdapterId,store).Authentication!="none";
        if(!acceptsKey&&!string.IsNullOrWhiteSpace(input.ApiKey))throw new DomainException("Bu kaynak API anahtarı istemiyor; anahtar alanını boş bırak.");
        var apiKey=acceptsKey?input.ApiKey?.Trim():"";var settings=input.Network??oldNetwork??new();
        if(previous is not null)
        {
            var oldKey=acceptsKey?indexers.ReadKey(previous.Info.Id):"";var same=Uri.TryCreate(endpoint,UriKind.Absolute,out _)&&SameOrigin(previous.Info.Endpoint,endpoint);
            if(!same&&(apiKey is null&&oldKey.Length>0||input.Network?.Cookie is null&&!string.IsNullOrEmpty(oldNetwork!.Cookie)||input.Network?.Headers is null&&oldNetwork!.Headers?.Count>0))throw new DomainException("Adres değişti; bu adresin API anahtarı/cookie/kimlik başlıklarını yeniden gir.");
            apiKey??=oldKey;
            if(input.Network is not null)settings=settings with{ProxyPassword=settings.ProxyPassword??oldNetwork!.ProxyPassword,Cookie=settings.Cookie??oldNetwork!.Cookie,Headers=settings.Headers??oldNetwork!.Headers};
        }
        if(provider.RequiresApiKey&&string.IsNullOrWhiteSpace(apiKey))throw new DomainException(provider.Name+" hesabındaki API anahtarını gir.");
        if(apiKey?.Any(char.IsControl)==true||name.Any(char.IsControl))throw new DomainException("Kaynak adı veya API anahtarı geçersiz.");
        var connection=IndexerService.ValidateInput(new(name,endpoint,provider.Protocol,apiKey,input.Priority,previous?.Info.Categories??"",previous?.Info.AllowedHosts??"",input.Enabled));
        var protectedNetwork=network.ProtectSettings(settings);var definition=catalog.Fingerprint(provider);
        var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{connection,settings,input.ProviderId,input.ExistingIndexerId,input.ExpectedRevision})));
        if(!await _tests.WaitAsync(0,token))throw new DomainException("İki kaynak testi zaten çalışıyor; biraz sonra yeniden dene.",429);
        try
        {
            var work=store.BeginSourceSetup(input,connection,indexers.ProtectKey(apiKey??""),provider.AdapterId,protectedNetwork,definition,hash,userId,loginId);
            if(work.Status is "ready" or "committed")return View(work);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token,application.ApplicationStopping);budget.CancelAfter(TimeSpan.FromSeconds(45));
            using var watchStop=new CancellationTokenSource();var watch=WatchAsync();
            try
            {
                _=store.RequireSourceSetupWork(work);await indexers.TestSetupAsync(work.IndexerId!,budget.Token);budget.Token.ThrowIfCancellationRequested();
                return View(store.CompleteSourceSetup(work));
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){store.FailSourceSetup(work,"Bağlantı testi iptal edildi.");throw;}
            catch(Exception ex)
            {
                var message=ex is DomainException?ex.Message:ex is OperationCanceledException?"Bağlantı testi iptal edildi veya zaman aşımına uğradı.":"Kaynak yanıtı doğrulanamadı; adres ve bağlantı bilgilerini kontrol et.";
                store.FailSourceSetup(work,message);logger.LogWarning("Kaynak kurulumu {Id}: {Type}",work.Id,ex.GetType().Name);
                return View(store.OwnSourceSetup(work.Id,userId,loginId));
            }
            finally{watchStop.Cancel();try{await watch;}catch(OperationCanceledException){}}
            async Task WatchAsync()
            {
                while(!watchStop.IsCancellationRequested)
                {
                    await Task.Delay(500,watchStop.Token);try{_=store.RequireSourceSetupWork(work);}catch{budget.Cancel();return;}
                }
            }
        }
        finally{_tests.Release();}
    }
    public SourceSetupPreview Status(string id,string userId,string loginId)=>View(store.OwnSourceSetup(id,userId,loginId));
    public IndexerInfo Connect(SourceSetupConnect input,string userId,string loginId)
    {
        var setup=store.OwnSourceSetup(input.Id,userId,loginId);var connected=store.ConnectSourceSetup(input,catalog.Fingerprint(catalog.Find(setup.ProviderId)),userId,loginId);
        events.Publish("sources.changed");return indexers.List().FirstOrDefault(source=>source.Id==connected.IndexerId)??throw new DomainException("Bağlanan kaynak bulunamadı.",404);
    }
    private SourceSetupPreview View(SourceSetup setup)
    {
        var source=setup.IndexerId is null?null:store.GetIndexer(setup.IndexerId).Info;
        var status=setup.Status!="committed"&&DateTimeOffset.Parse(setup.ExpiresAt)<=DateTimeOffset.UtcNow?"expired":setup.Status;
        return new(setup.Id,setup.ProviderId,source?.Name??"Kaynak testi",source?.Endpoint??"",status,setup.Revision,setup.ExpiresAt,source?.Capabilities?.Searches.Keys.ToArray()??[],source?.Capabilities?.Categories.Count??0,setup.Error,status=="committed"?setup.IndexerId:null,source?.Protocol=="newznab"&&!store.GetUsenetServers().Any(value=>value.Server.Enabled));
    }
    public override Task StartAsync(CancellationToken token){store.RecoverSourceSetups();return base.StartAsync(token);}
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{store.PruneSourceSetups();var ids=store.SourceConnectionIds();indexers.PruneRuntime(ids);network.PruneRuntime(ids);}catch(Exception ex){logger.LogWarning("Geçici kaynak testleri temizlenemedi: {Type}",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException){break;}
        }
    }
}
