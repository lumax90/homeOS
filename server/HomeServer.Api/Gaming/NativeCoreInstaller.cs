using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;
public sealed record NativeCoreInstallCommand(bool AcceptedLicense=false,bool AcceptedUpstreamBuild=false);
/// <summary>Explicit official software acquisition, never ROM/BIOS acquisition or an arbitrary native-code uploader.</summary>
public sealed class NativeCoreInstaller(HomePaths paths,MediaStore store,RetroArchCoreCatalog catalog,NativeCoreRegistry registry,NativeGameManager games,EventHub events,NativeGpuRuntime gpu):IProductWorkHandler,IDisposable
{
    public string[] Kinds=>["native-core-install"];
    private readonly HttpClient _http=new(new SocketsHttpHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(5)};
    public ProductJob Queue(string id,NativeCoreInstallCommand input,Account actor,bool local)
    {
        if(!actor.IsAdmin)throw new DomainException("Çekirdek kurulumu sahip hesabını gerektirir.",403);
        var core=catalog.Require(id);if(id=="mgba")throw new DomainException("Mevcut mGBA sürümü korunur; yeniden kurulmaz.",409);
        var host=NativeRuntimeProfile.Read(paths);var linux=host?.Bundled==true;var block=RetroArchCoreCatalog.AdapterBlock(core,host?.HeadlessGraphics==true||gpu.Prepared,linux);if(block is not null)throw new DomainException(block,409);
        if(!linux&&!core.WindowsBuild)throw new DomainException("Bu çekirdeğin Windows paketi resmi katalogda yok.",409);
        if(!input.AcceptedLicense||!input.AcceptedUpstreamBuild)throw new DomainException("Çekirdek lisansı ve upstream derlemesi için açık onay gerekli.",400);
        return store.QueueProductJob("native-core-install",id,new{catalogCommit=RetroArchCoreCatalog.Commit,core.InfoSha256,platform=linux?"linux-x64":"win-x64",acceptedLicense=true,acceptedUpstreamBuild=true},actor);
    }
    public async Task RunAsync(ProductJob job,CancellationToken token)
    {
        store.RequireProductJob(job,true);var host=NativeRuntimeProfile.Read(paths);var linux=host?.Bundled==true;var platform=linux?"linux-x64":"win-x64";var core=catalog.Require(job.ScopeId??"");if(job.Input.TryGetProperty("platform",out var requested)&&requested.GetString()!=platform||job.Input.GetProperty("catalogCommit").GetString()!=RetroArchCoreCatalog.Commit||job.Input.GetProperty("infoSha256").GetString()!=core.InfoSha256||RetroArchCoreCatalog.AdapterBlock(core,host?.HeadlessGraphics==true||gpu.Prepared,linux) is not null||core.Id=="mgba")throw new DomainException("Çekirdek kurulumu tanımı değişti.",409);
        if(registry.Installed(core.Id) is{} old){VerifiedFilePublication.Require(old.Binary.Path,old.Binary.Bytes,old.Binary.Sha256);VerifiedFilePublication.Require(old.Info.Path,old.Info.Bytes,old.Info.Sha256);store.FinishProductJob(job,new{core.Id,sha256=old.Binary.Sha256});return;}
        var root=AppFiles.DirectoryIn(paths.State,"games","core-staging",job.Id);var archive=Path.Combine(root,"upstream.zip");
        try
        {
            if(File.Exists(archive)){AppFiles.RejectLink(archive);File.Delete(archive);} // Only this job's interrupted staging file.
            var extension=linux?".so":".dll";var url=$"https://buildbot.libretro.com/nightly/{(linux?"linux":"windows")}/x86_64/latest/{core.Id}_libretro{extension}.zip";
            store.ProductProgress(job,5,"Resmi emülasyon çekirdeği alınıyor.");using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>256*1024*1024)throw new DomainException("Çekirdek paketi boyut sınırını aşıyor.",413);
            await using(var input=await response.Content.ReadAsStreamAsync(token))await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
            {var buffer=new byte[65536];long total=0;int read;while((read=await input.ReadAsync(buffer,token))>0){total+=read;if(total>256*1024*1024)throw new DomainException("Çekirdek paketi boyut sınırını aşıyor.",413);await output.WriteAsync(buffer.AsMemory(0,read),token);}await output.FlushAsync(token);output.Flush(true);}
            store.RequireProductJob(job,true);var archiveHash=await VerifiedFilePublication.HashAsync(archive,token);var dll=core.Id+"_libretro"+extension;var binaryPath=Path.Combine(root,dll);
            using(var zip=ZipFile.OpenRead(archive))
            {
                if(zip.Entries.Count!=1||zip.Entries[0].FullName!=dll||zip.Entries[0].Length is <512 or >256*1024*1024||(zip.Entries[0].ExternalAttributes>>16&0xf000)==0xa000)throw new DomainException("Çekirdek paketi beklenen tek DLL biçiminde değil.",422);
                if(File.Exists(binaryPath)){AppFiles.RejectLink(binaryPath);File.Delete(binaryPath);}
                await using var input=zip.Entries[0].Open();await using var output=new FileStream(binaryPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);var buffer=new byte[65536];long total=0;int read;while((read=await input.ReadAsync(buffer,token))>0){total+=read;if(total>zip.Entries[0].Length)throw new DomainException("Çekirdek paketi boyut sınırını aşıyor.",413);await output.WriteAsync(buffer.AsMemory(0,read),token);}if(total!=zip.Entries[0].Length)throw new DomainException("Çekirdek aktarımı tamamlanmadı.",422);await output.FlushAsync(token);output.Flush(true);
            }
            if(linux)RequireLinuxLibrary(binaryPath);else RequireWindowsDll(binaryPath);var bytes=new FileInfo(binaryPath).Length;var hash=await VerifiedFilePublication.HashAsync(binaryPath,token);var infoName=core.Id+"_libretro.info";var infoPath=Path.Combine(root,infoName);await File.WriteAllTextAsync(infoPath,core.Info,new UTF8Encoding(false),token);
            var destination=AppFiles.DirectoryIn(paths.State,"games",linux?"native-cores-linux-x64":"native-cores",core.Id,hash);store.ProductProgress(job,90,"Çekirdek dosyaları doğrulanıyor.");
            await VerifiedFilePublication.StageAsync(binaryPath,destination,dll,job.Id,bytes,hash,token);VerifiedFilePublication.Promote(destination,dll,job.Id,bytes,hash);
            await VerifiedFilePublication.StageAsync(infoPath,destination,infoName,job.Id,new FileInfo(infoPath).Length,core.InfoSha256,token);VerifiedFilePublication.Promote(destination,infoName,job.Id,new FileInfo(infoPath).Length,core.InfoSha256);
            var receipt=new InstalledNativeCore(1,core.Id,RetroArchCoreCatalog.Commit,"upstream-nightly",DateTimeOffset.UtcNow.ToString("O"),url,archiveHash,new(Path.Combine(destination,dll),bytes,hash),new(Path.Combine(destination,infoName),new FileInfo(infoPath).Length,core.InfoSha256));
            await games.ConfigureIdleRuntimeAsync(_=>{store.RequireProductJob(job,true);if(registry.Installed(core.Id) is not null)throw new DomainException("Çekirdek kurulum durumu değişti.",409);PackageTrust.AtomicJson(Path.Combine(Path.GetDirectoryName(destination)!,"installed.json"),receipt);events.Publish("games.changed");return Task.CompletedTask;},token);
            store.FinishProductJob(job,new{core.Id,sha256=hash,channel="upstream-nightly"});
        }
        finally{if(Directory.Exists(root))AppFiles.DeleteOwnedDirectory(root);}
    }
    public static void RequireWindowsDll(string path)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);if(stream.Length<512||reader.ReadUInt16()!=0x5a4d)throw new DomainException("Çekirdek DLL biçimi geçersiz.",422);stream.Position=0x3c;var offset=reader.ReadUInt32();if(offset>stream.Length-24)throw new DomainException("Çekirdek DLL biçimi geçersiz.",422);stream.Position=offset;
        if(reader.ReadUInt32()!=0x00004550||reader.ReadUInt16()!=0x8664)throw new DomainException("Çekirdek Windows x64 DLL olmalı.",422);stream.Position=offset+22;if((reader.ReadUInt16()&0x2000)==0)throw new DomainException("Çekirdek Windows x64 DLL olmalı.",422);
    }
    public static void RequireLinuxLibrary(string path)
    {
        using var file=System.IO.File.OpenRead(path);Span<byte> header=stackalloc byte[64];if(file.Length<512||file.Read(header)!=header.Length||!header[..4].SequenceEqual(new byte[]{0x7f,69,76,70})||header[4]!=2||header[5]!=1||BinaryPrimitives.ReadUInt16LittleEndian(header[16..])!=3||BinaryPrimitives.ReadUInt16LittleEndian(header[18..])!=62)throw new DomainException("Çekirdek Linux x64 ELF paylaşımlı kitaplığı olmalı.",422);
    }
    public void Dispose()=>_http.Dispose();
}
