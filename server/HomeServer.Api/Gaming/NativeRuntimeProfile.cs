using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.Principal;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record NativeRuntimeFile(string Path, long Bytes, string Sha256);
public sealed record NativeRuntimeProfile(int Format, bool Enabled, string CoreVersion, string CoreCommit,
    NativeRuntimeFile RetroArch, NativeRuntimeFile Core, NativeRuntimeFile CoreInfo, NativeRuntimeFile MediaMtx, NativeRuntimeFile[] Dependencies, bool ContinuousInputVerified = false,
    string Platform="win-x64",bool LoopbackOnly=false,NativeRuntimeFile? Proof=null,bool HeadlessGraphics=false)
{
    // Actual held-input probe failed for this reviewed Windows executable. A profile flag cannot promote it.
    public const string RejectedRemotePadSha256 = "81c11b6f24932bf7918f05eee8928035bff3887335fd2a081507c75e9d94d06a";
    public const string InputBlockReason = "Basılı tutulan oyun girdisi henüz doğrulanmadı; prototip oynatmaya açılmadı.";
    public bool InputReady => ContinuousInputVerified && !RejectedRemotePadSha256.Equals(RetroArch.Sha256, StringComparison.OrdinalIgnoreCase);
    public bool Bundled=>Format==2;
    public string CoreExtension=>Platform=="linux-x64"?".so":".dll";
    public string Version => "native-mgba-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RetroArch.Sha256 + Core.Sha256 + MediaMtx.Sha256)))[..32];
    public static NativeRuntimeProfile? Read(HomePaths paths)
    {
        if(paths.NativeRuntimePath is not null)return NativeRuntimeBundle.Profile(paths);
        var path = Path.Combine(paths.State, "games", "native-runtime-profile.json"); if (!File.Exists(path)) return null;
        path = MediaPaths.ResolveFile(paths.State, "games/native-runtime-profile.json");
        if (new FileInfo(path).Length > 128 * 1024) throw new DomainException("Yerel oyun çalışma profili çok büyük.", 409);
        NativeRuntimeProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<NativeRuntimeProfile>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new DomainException("Yerel oyun çalışma profili geçersiz.", 409);
        }
        catch (JsonException) { throw new DomainException("Yerel oyun çalışma profili geçersiz.", 409); }
        ValidateDefinition(profile);
        return profile;
    }
    public static void ValidateDefinition(NativeRuntimeProfile profile)
    {
        if (profile.Format is not(1 or 2)||profile.Format==1&&profile.Platform!="win-x64"||profile.Format==2&&(profile.Platform!="linux-x64"||!profile.LoopbackOnly||profile.Proof is null) || profile.CoreVersion != "0.10.5" || profile.CoreCommit != "26b7884bc25a5933960f3cdcd98bac1ae14d42e2" || profile.Dependencies is null || profile.Dependencies.Length > 128 || profile.RetroArch is null || profile.Core is null || profile.CoreInfo is null || profile.MediaMtx is null)
            throw new DomainException("Yerel oyun çalışma profili desteklenmiyor.", 409);
        if (profile.Dependencies.Concat([profile.RetroArch, profile.Core, profile.CoreInfo, profile.MediaMtx]).Any(file =>
            file is null || string.IsNullOrWhiteSpace(file.Path) || file.Bytes is < 1 or > 256 * 1024 * 1024 || file.Sha256 is not { Length: 64 } || file.Sha256.Any(c => !char.IsAsciiHexDigit(c))))
            throw new DomainException("Yerel oyun çalışma dosyası geçersiz.", 409);
    }
    public void Verify()
    {
        ValidateDefinition(this);
        if (!Enabled) throw new DomainException("Sunucu emülasyonunu etkinleştir.", 409);
        if (!InputReady) throw new DomainException(InputBlockReason, 409);
        if(Platform=="linux-x64")
        {
            if(!OperatingSystem.IsLinux()||System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new DomainException("Bu emülasyon paketi Linux x64 sunucu gerektirir.",409);
            if(GetEffectiveUserId()==0)throw new DomainException("Oyun çalıştırıcısı root yetkisiyle çalıştırılamaz.",409);
            if(Proof is null||Proof.Bytes is <1 or >128*1024)throw new DomainException("Sunucu emülasyonunun native doğrulaması eksik.",409);
            VerifiedFilePublication.Require(Proof.Path,Proof.Bytes,Proof.Sha256);NativeRuntimeBundle.RequireProof(this,File.ReadAllText(Proof.Path));
        }
        else
        {
            if (!OperatingSystem.IsWindows()) throw new DomainException("Bu emülasyon paketi Windows x64 sunucu gerektirir.", 409);
            using var identity=WindowsIdentity.GetCurrent();if(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new DomainException("Oyun çalıştırıcısı yönetici yetkisiyle çalıştırılamaz.",409);
        }
        foreach (var file in Dependencies.Concat([RetroArch, Core, CoreInfo, MediaMtx]))
        {
            if (file.Bytes is < 1 or > 256 * 1024 * 1024 || file.Sha256.Length != 64 || file.Sha256.Any(c => !char.IsAsciiHexDigit(c))) throw new DomainException("Yerel oyun çalışma dosyası geçersiz.", 409);
            VerifiedFilePublication.Require(file.Path, file.Bytes, file.Sha256);
        }
        if (!LoopbackOnly&&!NativeLocalInputGuard.InspectPorts(RetroArch.Path, [62401,62406])) throw new DomainException("Yerel oyun giriş ve komut bağlantıları dış erişime kapatılmalı.", 409);
    }
    [System.Runtime.InteropServices.DllImport("libc",EntryPoint="geteuid")]private static extern uint GetEffectiveUserId();
    public GameManifest Game(GameRom rom) => new(rom.Id, rom.Title, "", "", "", CoreCommit, Version, "RetroArch / mGBA 0.10.5", "user-supplied", "native", "16/9", [], ["native-save.json"], [], "native");
}
