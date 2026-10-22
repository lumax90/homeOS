using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public interface INativeRuntimeVerifier { void Verify(NativeRuntimeProfile profile); }
public sealed class NativeRuntimeVerifier : INativeRuntimeVerifier
{
    public void Verify(NativeRuntimeProfile profile) => profile.Verify();
}
public sealed record NativeRuntimeCandidate(int Format, string FrontendVersion, string FrontendCommit, string InputFix,
    string MediaMtxVersion, NativeRuntimeProfile Profile, NativeRuntimeFile Proof)
{
    public static NativeRuntimeCandidate? Read(HomePaths paths)
    {
        const string relative = "games/native-runtime-candidate.json";
        if (!File.Exists(Path.Combine(paths.State, relative))) return null;
        var path = MediaPaths.ResolveFile(paths.State, relative);
        if (new FileInfo(path).Length > 128 * 1024) throw new DomainException("Emülasyon hazırlık kaydı çok büyük.", 409);
        NativeRuntimeCandidate candidate;
        try { candidate = JsonSerializer.Deserialize<NativeRuntimeCandidate>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException(); }
        catch (JsonException) { throw new DomainException("Emülasyon hazırlık kaydı geçersiz.", 409); }
        if (candidate.Format != 1 || candidate.FrontendVersion != "1.22.2" || candidate.FrontendCommit != "69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576" || candidate.InputFix != "cfb1204314dd6b061a7b1cda77221735939bf5ba" || candidate.MediaMtxVersion != "1.21.2" || candidate.Profile is null || candidate.Proof is null)
            throw new DomainException("Emülasyon hazırlık sürümü desteklenmiyor.", 409);
        NativeRuntimeProfile.ValidateDefinition(candidate.Profile);
        if (!candidate.Profile.InputReady || candidate.Proof.Path is null || !Path.IsPathFullyQualified(candidate.Proof.Path) || candidate.Proof.Bytes is < 1 or > 128 * 1024 || candidate.Proof.Sha256 is not { Length: 64 } || candidate.Proof.Sha256.Any(c => !char.IsAsciiHexDigit(c))) throw new DomainException("Emülasyonun native giriş kanıtı eksik.", 409);
        VerifiedFilePublication.Require(candidate.Proof.Path, candidate.Proof.Bytes, candidate.Proof.Sha256);
        RequireProof(candidate.Profile, File.ReadAllText(candidate.Proof.Path));
        return candidate;
    }
    public static void RequireProof(NativeRuntimeProfile profile, string content)
    {
        try
        {
            using var proof = JsonDocument.Parse(content); var root = proof.RootElement; var stream = root.GetProperty("streaming");
            if (root.GetProperty("error").ValueKind != JsonValueKind.Null || root.GetProperty("frontendSha256").GetString() != profile.RetroArch.Sha256 || root.GetProperty("coreSha256").GetString() != profile.Core.Sha256 ||
                !root.GetProperty("patchedPrototype").GetBoolean() || root.GetProperty("upstreamInputFix").GetString() != "cfb1204314dd6b061a7b1cda77221735939bf5ba" ||
                !stream.GetProperty("simultaneousHeldInputVerified").GetBoolean() || stream.GetProperty("held").GetProperty("consecutiveHeldFrames").GetUInt32() < 10 ||
                !stream.GetProperty("nativeSaveRestoreVerified").GetBoolean() || !stream.GetProperty("windowless").GetBoolean() || !stream.GetProperty("privateInputGuard").GetBoolean() ||
                stream.GetProperty("whepStatus").GetInt32() != 201 || !stream.GetProperty("negotiatedH264Opus").GetBoolean()) throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new DomainException("Emülasyonun native giriş kanıtı eksik.", 409); }
    }
}
public sealed record NativeSetupCommand(bool Enabled, bool AcceptedPrototype = false, string? ExpectedVersion = null);
public sealed record NativeSetupStatus(bool Available, bool Enabled, bool Prepared, bool CanConfigure, bool ClientLocal,
    string? Version, string? Reason, string[] Components, bool LocalOnly = false, bool OwnerOnly = true, bool PreparedRuntimeOnly = true,string? Platform=null);

/// <summary>Manage installed server software from any authenticated owner device.</summary>
public sealed class NativeRuntimeSetup(HomePaths paths,NativeGameManager games,INativeRuntimeVerifier verifier,EventHub events,NativeGpuRuntime? gpu=null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public NativeSetupStatus Status(Account actor, bool local)
    {
        if (!actor.IsAdmin) return new(false, false, false, false, local, null, "Sunucu emülasyonu sahip hesabını gerektirir.", []);
        NativeRuntimeProfile? profile = null; NativeRuntimeCandidate? candidate = null; string? reason = null; var available = false; var prepared = false;
        try
        {
            profile = NativeRuntimeProfile.Read(paths);
            if(profile?.Bundled==true)
            {
                verifier.Verify(profile with{Enabled=true});
                return new(profile.Enabled,profile.Enabled,true,true,local,profile.Version,null,["RetroArch 1.22.2 + Home host adapter","Headless Linux x64","mGBA / FCEUmm / Snes9x / Genesis Plus GX / PCSX ReARMed","MediaMTX 1.21.2"],PreparedRuntimeOnly:false,Platform:profile.Platform);
            }
            if (profile?.Enabled == true) { verifier.Verify(profile); available = true; }
            candidate = NativeRuntimeCandidate.Read(paths);
            if (candidate is not null) { verifier.Verify(candidate.Profile with { Enabled = true }); prepared = true; }
            if (!available && !prepared) reason = "Bu sunucuda doğrulanmış emülasyon paketi henüz hazırlanmadı.";
        }
        catch (DomainException error) { reason = error.Message; }
        catch (IOException) { reason = "Emülasyon dosyalarına erişilemiyor; ROM ve kayıtlar korundu."; }
        catch (UnauthorizedAccessException) { reason = "Emülasyon dosyalarına erişilemiyor; ROM ve kayıtlar korundu."; }
        return new(available, profile?.Enabled == true, prepared, prepared || profile is not null, local, profile?.Version ?? candidate?.Profile.Version, reason,
            candidate is null&&profile is null?[]:gpu?.Prepared==true?["RetroArch 1.22.2 + Home host adapter","OpenGL / Direct3D 11 / Vulkan","mGBA 0.10.5","MediaMTX 1.21.2","GPL-3.0 / MPL-2.0 / MIT"]:["RetroArch 1.22.2 + upstream input fix cfb12043","mGBA 0.10.5","MediaMTX 1.21.2","GPL-3.0 / MPL-2.0 / MIT"]);
    }
    public async Task<NativeSetupStatus> ConfigureAsync(NativeSetupCommand input, Account actor, bool local, CancellationToken token)
    {
        if (!actor.IsAdmin) throw new DomainException("Sunucu emülasyonu sahip hesabını gerektirir.", 403);
        await games.ConfigureIdleRuntimeAsync(async budget =>
        {
            var current = NativeRuntimeProfile.Read(paths);
            if(current?.Bundled==true)
            {
                if(input.ExpectedVersion!=current.Version)throw new DomainException("Emülasyon hazırlığı değişti. Durumu yenileyip tekrar dene.",409);
                if(input.Enabled)verifier.Verify(current with{Enabled=true});budget.ThrowIfCancellationRequested();
                Operations.PackageTrust.AtomicJson(Path.Combine(AppFiles.DirectoryIn(paths.State,"games"),"server-emulation.json"),new NativeBundleSettings(Enabled:input.Enabled));events.Publish("games.changed");return;
            }
            var candidate = input.Enabled ? NativeRuntimeCandidate.Read(paths) ?? throw new DomainException("Bu sunucuda doğrulanmış emülasyon paketi henüz hazırlanmadı.", 409) : null;
            var version = current?.Version ?? candidate?.Profile.Version;
            if (input.ExpectedVersion != version) throw new DomainException("Emülasyon hazırlığı değişti. Durumu yenileyip tekrar dene.", 409);
            if (candidate is not null)
            {
                verifier.Verify(candidate.Profile with { Enabled = true });
                if (current is not null && JsonSerializer.Serialize(current with { Enabled = false }, Json) != JsonSerializer.Serialize(candidate.Profile with { Enabled = false }, Json))
                    throw new DomainException("Mevcut emülasyon profili farklı; dosya ve kayıtlar korundu.", 409);
            }
            if (current is null && !input.Enabled) return;
            var root = AppFiles.DirectoryIn(paths.State, "games"); var target = Path.Combine(root, "native-runtime-profile.json");
            byte[]? before = null; JsonObject content;
            if (current is null) content = JsonSerializer.SerializeToNode(candidate!.Profile, Json)!.AsObject();
            else
            {
                before = await File.ReadAllBytesAsync(MediaPaths.ResolveFile(paths.State, "games/native-runtime-profile.json"), budget);
                content = JsonNode.Parse(before)!.AsObject();
            }
            var keys = content.Select(pair => pair.Key).Where(key => key.Equals("enabled", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keys.Length > 1) throw new DomainException("Mevcut emülasyon profili farklı; dosya ve kayıtlar korundu.", 409);
            content[keys.FirstOrDefault() ?? "enabled"] = input.Enabled;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(content, Json); if (bytes.Length > 128 * 1024) throw new DomainException("Emülasyon hazırlık kaydı çok büyük.", 409);
            var temporary = Path.Combine(root, ".native-setup-" + Guid.NewGuid().ToString("N") + ".partial");
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous)) { await output.WriteAsync(bytes, budget); await output.FlushAsync(budget); output.Flush(true); }
                budget.ThrowIfCancellationRequested();
                if (before is not null)
                {
                    var latest = await File.ReadAllBytesAsync(target, budget);
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(before), SHA256.HashData(latest))) throw new DomainException("Emülasyon hazırlığı değişti. Durumu yenileyip tekrar dene.", 409);
                }
                File.Move(temporary, target, before is not null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            events.Publish("games.changed");
        }, token);
        return Status(actor, local);
    }
}
