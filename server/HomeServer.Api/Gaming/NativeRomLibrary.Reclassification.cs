using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record GameSystemCorrection(string System,bool Confirmed=false);
public sealed partial class NativeRomLibrary
{
    // Called with the content and idle-runtime locks already held. Original collection bytes never move.
    public async Task<GameRom> CorrectPspSystemAsync(GameRom rom,Account actor,string login,CancellationToken token)
    {
        if(rom.UserId!=actor.Id||rom.System!="ps1"||NativeSystems.Extension(rom.FileName)!="iso")throw new DomainException("Bu düzeltme yalnız PS1 olarak eklenmiş PSP ISO dosyaları içindir.",409);
        store.RequireRomImportActor(actor,login);
        store.RequireRomPublication(rom.System,rom.Sha256);store.RequireRomPublication("psp",rom.Sha256);
        var source=MediaPaths.ResolveFile(paths.State,$"games/roms/{rom.System}/{rom.Sha256}/{NativeSystems.ContentName(rom)}");
        await using(var verified=OpenVerified(rom)){}
        if(await IdentifyIsoAsync(source,token)!="psp")throw new DomainException("Diskte PSP oyun yapısı doğrulanamadı; sistem ve dosyalar değiştirilmedi.",409);
        if(new DriveInfo(Path.GetPathRoot(paths.State)!).AvailableFreeSpace<rom.SizeBytes+128*1024*1024)throw new DomainException("Doğrulanmış PSP kopyası için yeterli boş alan yok.",409);
        var target=AppFiles.DirectoryIn(paths.State,"games","roms","psp",rom.Sha256);var operation=Guid.NewGuid().ToString("N");
        await VerifiedFilePublication.StageAsync(source,target,NativeSystems.ContentName(rom),operation,rom.SizeBytes,rom.Sha256,token);
        VerifiedFilePublication.Promote(target,NativeSystems.ContentName(rom),operation,rom.SizeBytes,rom.Sha256);
        return store.CorrectRomSystem(rom,"psp",actor,login); // Stable game id and all prior save partitions remain intact.
    }
}
