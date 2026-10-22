using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

/// <summary>Bounded user upload, not a ROM provider/downloader or executable importer.</summary>
public sealed partial class NativeRomLibrary(HomePaths paths, MediaStore store) : IDisposable
{
    public const long MaximumBytes = 32 * 1024 * 1024;
    private readonly SemaphoreSlim _upload = new(1, 1);
    public static void ValidateName(string value)
    {
        NativeSystems.ValidateFileName(value);
        if (value.Length is < 5 or > 124 || value != value.Trim() || value.Contains('/') || value.Contains('\\')
            || value.Any(c => char.IsControl(c) || c is ':' or '"' or '<' or '>' or '|' or '*' or '?')
            || !value.EndsWith(".gba", StringComparison.OrdinalIgnoreCase) || value.StartsWith('.') || value.Contains("..", StringComparison.Ordinal))
            throw new DomainException("İlk ROM desteği yalnız tek bir .gba dosyası içindir. ZIP, BIOS veya çalıştırılabilir dosya eklenemez.");
        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(value))) throw new DomainException("ROM dosyası adı geçersiz.");
    }
    public async Task<GameRom> ImportAsync(Stream input, string fileName, long? expectedLength, Account actor, string loginId, CancellationToken token)
        =>await ImportAsync(input,fileName,expectedLength,actor,loginId,token,"gba");
    public async Task<GameRom> ImportAsync(Stream input,string fileName,long? expectedLength,Account actor,string loginId,CancellationToken token,string system)
    {
        if(system=="gba")ValidateName(fileName);else NativeSystems.ValidateName(system,fileName);store.RequireRomImportActor(actor,loginId);
        var definition=NativeSystems.Require(system);var minimum=system=="gba"?192:16;
        if(NativeSystems.Extension(fileName) is "cue" or "m3u" or "gdi")throw new DomainException("Bu oyun için disk dosyalarını birlikte seç.",400);
        if(expectedLength<minimum||expectedLength>definition.MaximumBytes)throw new DomainException("ROM dosyası seçilen sistemin boyut sınırını aşıyor.",413);
        // Do not accumulate a queue of upload bodies or unbounded managed staging directories.
        if (!await _upload.WaitAsync(0, token)) throw new DomainException("Başka bir ROM ekleniyor. Biraz bekleyip yeniden dene.", 409);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(system=="gba"?TimeSpan.FromMinutes(2):TimeSpan.FromHours(2)); token = budget.Token;
        string? stage = null, publicationPartial = null;
        try
        {
            var operation = Guid.NewGuid().ToString("N");
            stage = AppFiles.DirectoryIn(paths.State, "games", "rom-staging", operation);
            var pending = Path.Combine(stage, "game.partial"); long total = 0;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[64 * 1024]; int bytes;
                while ((bytes = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += bytes;
                    if(total>definition.MaximumBytes)throw new DomainException("ROM dosyası seçilen sistemin boyut sınırını aşıyor.",413);
                    digest.AppendData(buffer.AsSpan(0, bytes)); await output.WriteAsync(buffer.AsMemory(0, bytes), token);
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            if(total<minimum||expectedLength is{} length&&length!=total)throw new DomainException("ROM aktarımı tamamlanmadı; kütüphaneye eklenmedi.",422);
            var header = new byte[(int)Math.Min(total,192)];
            await using (var file = File.OpenRead(pending)) await file.ReadExactlyAsync(header, token);
            // Narrow standard GBA cartridge-header gate, not a game-compatibility or authenticity verdict.
            // Unfixed/ELF/multiboot builds and BIOS import are deliberately outside this first slice.
            if(system=="gba")
            {
                var checksum=0x19;for(var index=0xa0;index<=0xbc;index++)checksum+=header[index];
                if(header[3]!=0xea||header[0xb2]!=0x96||(byte)(-checksum)!=header[0xbd])throw new DomainException("Dosya standart GBA kartuş başlığı kontrolünü geçemedi. Geçerli bir .gba ROM seç.",422);
            }
            if(system!="gba")ValidateHeader(system,fileName,header,total);
            // Integrity is not an emulator/game compatibility verdict. No fake metadata or embedded game/BIOS data.
            var hash = Convert.ToHexStringLower(digest.GetHashAndReset());
            var root=AppFiles.DirectoryIn(paths.State,"games","roms",system,hash);var content="game."+NativeSystems.Extension(fileName);
            publicationPartial = Path.Combine(root, ".home-publish-" + operation + ".partial");
            await VerifiedFilePublication.StageAsync(pending,root,content,operation,total,hash,token);
            token.ThrowIfCancellationRequested(); store.RequireRomImportActor(actor, loginId);
            VerifiedFilePublication.Promote(root,content,operation,total,hash);
            // A database failure leaves verified content, never a falsely cataloged ROM; identical retry is safe.
            return store.RegisterGameRom(fileName,hash,total,actor,loginId,system);
        }
        finally
        {
            try
            {
                if (publicationPartial is not null && File.Exists(publicationPartial)) { AppFiles.RejectLink(publicationPartial); File.Delete(publicationPartial); }
                if (stage is not null) AppFiles.DeleteOwnedDirectory(stage);
            }
            finally { _upload.Release(); }
        }
    }
    public FileStream OpenVerified(GameRom rom)
    {
        NativeSystems.ValidateName(rom.System,rom.FileName);
        if(rom.Sha256.Length!=64||rom.Sha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("ROM dosyası sınırları geçersiz.");
        var path=MediaPaths.ResolveFile(paths.State,$"games/roms/{rom.System}/{rom.Sha256}/{NativeSystems.ContentName(rom)}");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        try
        {
            if (stream.Length != rom.SizeBytes || Convert.ToHexStringLower(SHA256.HashData(stream)) != rom.Sha256) throw new DomainException("ROM kopyası bütünlük kontrolünden geçemedi; mevcut dosya korundu.", 409);
            stream.Position = 0; return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    public void Dispose() => _upload.Dispose();
    public static void ValidateHeader(string system,string file,byte[] header,long bytes)
    {
        if(header.Length<16||header[0]=='M'&&header[1]=='Z'||header.AsSpan(0,4).SequenceEqual(new byte[]{0x7f,(byte)'E',(byte)'L',(byte)'F'}))throw new DomainException("ROM dosyası biçimi geçersiz.",422);
        var extension=NativeSystems.Extension(file);
        if(system=="nes"&&extension=="nes"&&!header.AsSpan(0,4).SequenceEqual(new byte[]{(byte)'N',(byte)'E',(byte)'S',0x1a}))throw new DomainException("NES kartuş başlığı geçersiz.",422);
        if(extension=="chd"&&!header.AsSpan(0,8).SequenceEqual("MComprHD"u8))throw new DomainException("CHD disk başlığı geçersiz.",422);
        if(extension=="zip"&&!header.AsSpan(0,4).SequenceEqual(new byte[]{0x50,0x4b,0x03,0x04}))throw new DomainException("Arcade arşiv başlığı geçersiz.",422);
        if(system is "gb" or "gbc"&&bytes<0x150)throw new DomainException("Game Boy kartuş başlığı eksik.",422);
    }
}
