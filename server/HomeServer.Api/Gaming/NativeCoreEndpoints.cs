using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;
public static class NativeCoreEndpoints
{
    public static void MapNativeCores(this RouteGroupBuilder api)
    {
        api.MapGet("/games/native/cores",(RetroArchCoreCatalog catalog,NativeCoreRegistry registry,HomePaths paths,MediaStore store,CurrentAccount actor,NativeGpuRuntime gpu)=>
        {
            NativeRuntimeProfile? host=null;try{host=NativeRuntimeProfile.Read(paths);}catch(DomainException){}
            var jobs=store.GetProductJobs(actor.User,"native-core-install").GroupBy(job=>job.ScopeId??"").ToDictionary(group=>group.Key,group=>group.First());var graphics=host?.HeadlessGraphics==true||gpu.Prepared;
            return Results.Ok(new{commit=RetroArchCoreCatalog.Commit,catalog.Data.ReviewedAt,catalog.Data.Source,items=catalog.Data.Entries.Select(core=>
            {
                InstalledNativeCore? installed=null;string? problem=null;try{installed=registry.Installed(core.Id);}catch(Exception error)when(error is DomainException or IOException or System.Text.Json.JsonException){problem="Çekirdek dosyaları kullanılamıyor; ROM ve kayıtlar korundu.";}
                var block=RetroArchCoreCatalog.AdapterBlock(core,graphics,host?.Bundled==true);var job=jobs.GetValueOrDefault(core.Id);
                return new{core.Id,core.Name,core.DisplayName,core.SystemName,core.Databases,core.Extensions,core.License,core.Categories,core.Hardware,core.HardwareApi,core.SaveState,core.DiskControl,core.Experimental,core.Description,basicSystems=NativeBasicSelection.CoreSystems(core.Id),firmware=NativeFirmwareLibrary.Requirements(core),core.Notes,core.WindowsBuild,resourceRoots=NativeFirmwareLibrary.UploadRoots(core),adapterReady=block is null,reason=problem??block,installed=core.Id=="mgba"?host is not null:installed is not null,version=core.Id=="mgba"?"0.10.5":installed?.Binary.Sha256,channel=core.Id=="mgba"?"released":installed?.Channel??"upstream-nightly",status=job?.Status,message=job?.Message,progress=job?.Progress??0,jobId=job?.Id};
            })});
        });
        api.MapGet("/games/native/systems",(NativeCoreRegistry registry,HomePaths paths,CurrentAccount actor)=>
        {NativeRuntimeProfile? host=null;try{host=NativeRuntimeProfile.Read(paths);}catch(DomainException){}return Results.Ok(NativeBasicSelection.Order(NativeSystems.All).Select(system=>new{system.Id,system.Name,system.Core,system.Extensions,system.MaximumBytes,system.ArtworkDatabase,system.Digital,basic=NativeBasicSelection.Includes(system.Id),reason=registry.BlockReason(new("",""+actor.User.Id,system.Id,"game."+system.Extensions[0],"","",16,""),host)}));});
        api.MapPost("/games/native/cores/{id}/install",(string id,NativeCoreInstallCommand input,NativeCoreInstaller installer,CurrentAccount actor,HttpContext context,EventHub events)=>{GameEndpoints.RequireCommandActor(actor,context);var job=installer.Queue(id,input,actor.User,NativeGameEndpoints.Local(context));events.Publish("games.changed");return Results.Ok(job);}).RequireRateLimiting("watch-command");
        api.MapPost("/games/native/cores/{id}/install/cancel",(string id,MediaStore store,CurrentAccount actor,HttpContext context,EventHub events)=>{GameEndpoints.RequireCommandActor(actor,context);var job=store.GetProductJobs(actor.User,"native-core-install",id).FirstOrDefault()??throw new DomainException("İş bulunamadı.",404);store.CancelProductJob(job.Id,job.Revision,actor.User);events.Publish("games.changed");return Results.NoContent();}).RequireRateLimiting("watch-command");
        api.MapGet("/games/{id}/core",(string id,NativeCoreRegistry registry,RetroArchCoreCatalog catalog,MediaStore store,CurrentAccount actor,NativeGpuRuntime gpu,HomePaths paths)=>
        {
            var rom=store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404);var host=NativeRuntimeProfile.Read(paths);var graphics=host?.HeadlessGraphics==true||gpu.Prepared;
            var jobs=store.GetProductJobs(actor.User,"native-core-install").GroupBy(job=>job.ScopeId??"").ToDictionary(group=>group.Key,group=>group.First());
            return Results.Ok(new{selection=registry.Selection(rom),items=catalog.Data.Entries.Where(core=>registry.Compatible(rom.System,core)).Select(core=>new{core.Id,core.Name,core.License,installed=core.Id=="mgba"?host is not null:registry.Installed(core.Id) is not null,adapterReady=RetroArchCoreCatalog.AdapterBlock(core,graphics,host?.Bundled==true) is null,status=jobs.GetValueOrDefault(core.Id)?.Status,message=jobs.GetValueOrDefault(core.Id)?.Message})});
        });
        api.MapPut("/games/{id}/core",async(string id,NativeCoreSelection input,NativeCoreRegistry registry,NativeGameManager games,MediaStore store,CurrentAccount actor,HttpContext context,EventHub events,CancellationToken token)=>{GameEndpoints.RequireCommandActor(actor,context);if(!actor.User.IsAdmin)throw new DomainException("Çekirdek seçimi sahip hesabını gerektirir.",403);NativeCoreSelection? result=null;await games.ConfigureIdleRuntimeAsync(_=>{result=registry.Select(store.GetGameRom(id,actor.User)??throw new DomainException("ROM bulunamadı.",404),input);events.Publish("games.changed");return Task.CompletedTask;},token);return Results.Ok(result);}).RequireRateLimiting("watch-command");
        api.MapGet("/games/native/cores/{id}/firmware",(string id,RetroArchCoreCatalog catalog,NativeFirmwareLibrary firmware,CurrentAccount actor)=>{var core=catalog.Require(id);return Results.Ok(new{required=NativeFirmwareLibrary.Requirements(core),roots=NativeFirmwareLibrary.UploadRoots(core),files=firmware.Files(actor.User.Id,id)});});
        api.MapPost("/games/native/cores/{id}/firmware",async(string id,RetroArchCoreCatalog catalog,NativeFirmwareLibrary firmware,NativeGameManager games,MediaStore store,CurrentAccount actor,HttpContext context,EventHub events,CancellationToken token)=>
        {
            GameEndpoints.RequireCommandActor(actor,context);if(!actor.User.IsAdmin)throw new DomainException("BIOS eklemek sahip hesabını gerektirir.",403);
            var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is{IsReadOnly:false})limit.MaxRequestBodySize=64L*1024*1024*1024+2*1024*1024;
            var encoded=context.Request.Headers["X-Home-Firmware-Name"].ToString();if(encoded.Length is <1 or >1024)throw new DomainException("BIOS dosyası adı geçersiz.",400);var name=Uri.UnescapeDataString(encoded);NativeFirmwareFile? result=null;
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromHours(2));var core=catalog.Require(id);
            if(Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(context.Request.ContentType,out var type)&&type.MediaType.Equals("multipart/form-data",StringComparison.OrdinalIgnoreCase))
            {var prefix=name=="-"?"":name;var count=0;await games.ConfigureIdleRuntimeAsync(async cancellation=>{count=await firmware.ImportPackAsync(core,context.Request.Body,Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(type.Boundary).Value??"",prefix,actor.User,store,actor.SessionId??"",cancellation);events.Publish("games.changed");},budget.Token);return Results.Ok(new{count});}
            if(context.Request.ContentType!="application/octet-stream")throw new DomainException("ROM dosyası ikili veri olarak gönderilmeli.",415);
            await games.ConfigureIdleRuntimeAsync(async cancellation=>{result=await firmware.ImportAsync(core,name,context.Request.Body,context.Request.ContentLength,actor.User,store,actor.SessionId??"",cancellation);events.Publish("games.changed");},budget.Token);return Results.Ok(result);
        }).RequireRateLimiting("watch-command");
    }
}
