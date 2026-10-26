using HomeServer.Core;

namespace HomeServer.Playback;

public static class DolbyVisionBaseLayer
{
    // First verified base-copy path: P8.1 HEVC, HDR10-compatible single layer.
    // P5, P7/EL and other compatibility modes need their own output proofs.
    public static bool CanUseHdr10(MediaStreamInfo? video) => video is
    {
        Codec: "hevc", DolbyVision: true, ColorTransfer: "smpte2084", ColorPrimaries: "bt2020",
        DolbyVisionConfig: { Profile: 8, BaseLayerCompatibilityId: 1, BaseLayerPresent: true, EnhancementLayerPresent: false, RpuPresent: true }
    };
}
