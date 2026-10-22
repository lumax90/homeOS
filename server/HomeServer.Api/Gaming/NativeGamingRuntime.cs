using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

/// <summary>Reviewed candidate inventory; verified archives are NOT a configured or safe streaming runtime.</summary>
public sealed class NativeGamingRuntime(HomePaths paths)
{
    public static readonly NativePackageCandidate[] Candidates =
    [
        new("retroarch", "1.22.2", "GPL-3.0", "69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576", "https://github.com/libretro/RetroArch/tree/v1.22.2", null, null, null),
        new("sunshine", "2026.914.233613", "GPL-3.0", "63d35f702ee9e362e43263742981836ec0710384", "https://github.com/LizardByte/Sunshine/tree/v2026.914.233613",
            "https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-lite.zip", "Sunshine-Windows-AMD64-lite.zip", "233008e46f4c0e501a586cbfd6c4fd4a4c0d414a0b5fc7f13c070eb92ec3824b"),
        new("moonlight-web", "2.10.0", "GPL-3.0", "cd9d03cbf9a42b394f7b72a733a2f39cb5f0edd8", "https://github.com/MrCreativ3001/moonlight-web-stream/tree/v2.10.0",
            "https://github.com/MrCreativ3001/moonlight-web-stream/releases/download/v2.10.0/moonlight-web-x86_64-pc-windows-gnu.zip", "moonlight-web-x86_64-pc-windows-gnu.zip", "1dc3019952c610fbd7deb76dc84e3c4c6f26458ebb44823ea1f02ad883a36da9")
    ];
    public NativeGamingStatus Status()
    {
        var components = Candidates.Select(candidate => new NativePackageInventory(candidate, ArchiveStatus(candidate))).ToArray();
        return new(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other", ["gba"], NativeRomLibrary.MaximumBytes,
            false, false, components, ["native-execution-not-implemented", "capture-isolation-not-configured", "virtual-gamepad-not-configured", "browser-bridge-not-integrated"]);
    }
    private string ArchiveStatus(NativePackageCandidate candidate)
    {
        if (candidate.ArchiveFileName is null || candidate.ArchiveSha256 is null) return "binary-not-reviewed";
        var relative = $"games/native-archives/{candidate.Id}/{candidate.ArchiveFileName}";
        if (!File.Exists(Path.Combine(paths.State, relative.Replace('/', Path.DirectorySeparatorChar)))) return "not-staged";
        try
        {
            var path = MediaPaths.ResolveFile(paths.State, relative);
            if (new FileInfo(path).Length > 64 * 1024 * 1024) return "invalid-archive";
            return StateFolders.Hash(path) == candidate.ArchiveSha256 ? "archive-verified" : "invalid-archive";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DomainException) { return "invalid-archive"; }
    }
}
