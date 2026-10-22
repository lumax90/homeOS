using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Playback;

public sealed record EncodingCapabilities(bool Nvenc, bool ToneMapping, string Preference, string CheckedAt, string? Note);
public sealed class EncodingSupport(MediaTools tools, MediaStore store)
{
    public const string ToneMapFilter = "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EncodingCapabilities? _capabilities;
    private bool _hardwareFailed;
    public string Preference => store.GetSetting("playback.encoder") ?? "auto";
    public void SetPreference(string value)
    {
        if (value is not ("auto" or "software" or "nvenc")) throw new DomainException("Kodlayıcı tercihi geçersiz.");
        store.SetSetting("playback.encoder", value); _hardwareFailed = false;
    }
    public void HardwareFailed() => _hardwareFailed = true;
    public async Task<EncodingCapabilities> InspectAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_capabilities is not null) return _capabilities with { Preference = Preference, Nvenc = _capabilities.Nvenc && !_hardwareFailed };
            if (!tools.FfmpegAvailable) return new(false, false, Preference, DateTimeOffset.UtcNow.ToString("O"), "FFmpeg bulunamadı.");
            var nvenc = await TestAsync(["-f", "lavfi", "-i", "color=size=256x144:rate=12", "-frames:v", "12", "-c:v", "h264_nvenc", "-preset", "p4", "-f", "null", "-"], token);
            var tone = await TestAsync(["-f", "lavfi", "-i", "color=size=128x72:rate=1,format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc",
                "-frames:v", "2", "-vf", ToneMapFilter, "-f", "null", "-"], token);
            _capabilities = new(nvenc, tone, Preference, DateTimeOffset.UtcNow.ToString("O"), nvenc ? "NVENC gerçek kodlama denemesi geçti. Çözme ve filtreleme bu profilde CPU'dadır." : "NVENC başlatılamadı; yazılım kodlayıcısı kullanılabilir.");
            return _capabilities;
        }
        finally { _gate.Release(); }
    }
    public async Task<string> EncoderAsync(CancellationToken token)
    {
        if (Preference == "software" || _hardwareFailed) return "libx264";
        return (await InspectAsync(token)).Nvenc ? "h264_nvenc" : "libx264";
    }
    private async Task<bool> TestAsync(string[] arguments, CancellationToken token)
    {
        try { await MediaProcess.RunAsync(tools.FfmpegExecutable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "1", ..arguments], TimeSpan.FromSeconds(15), token); return true; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { return false; }
    }
}
