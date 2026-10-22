using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;
public sealed record NativeEmulationOptions(int Revision=0,string Quality="auto",bool AudioFilter=false,int SolarSensor=0);
public sealed class NativeEmulationSettings(HomePaths paths,NativeGameManager games)
{
    public static NativeEmulationOptions Read(HomePaths paths)
    {
        const string relative="games/emulation-options.json";if(!File.Exists(Path.Combine(paths.State,relative)))return new();
        var path=MediaPaths.ResolveFile(paths.State,relative);if(new FileInfo(path).Length>4096)throw new DomainException("Emülasyon ayar dosyası geçersiz.",409);
        try { var value=JsonSerializer.Deserialize<NativeEmulationOptions>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new JsonException();Validate(value);return value; }
        catch(JsonException){throw new DomainException("Emülasyon ayar dosyası geçersiz.",409);}
    }
    private static void Validate(NativeEmulationOptions value)
    { if(value.Revision<0||value.Quality is not("auto" or "quality" or "low")||value.SolarSensor is <0 or >10)throw new DomainException("Emülasyon ayarı desteklenmiyor.",400); }
    public async Task<NativeEmulationOptions> SaveAsync(NativeEmulationOptions value,Account actor,bool local,CancellationToken token)
    {
        if(!actor.IsAdmin)throw new DomainException("Emülasyon ayarları sahip hesabından değiştirilir.",403);Validate(value);
        NativeEmulationOptions? saved=null;
        await games.ConfigureIdleRuntimeAsync(_=>{var current=Read(paths);if(current.Revision!=value.Revision)throw new DomainException("Emülasyon ayarları değişti; yeniden yükle.",409);saved=value with{Revision=value.Revision+1};PackageTrust.AtomicJson(Path.Combine(AppFiles.DirectoryIn(paths.State,"games"),"emulation-options.json"),saved);return Task.CompletedTask;},token);
        return saved!;
    }
    public static string Recording(NativeEmulationOptions value,int width=240,int height=160)
    {var scale=Math.Clamp(width*height/(240d*160),1,10);var bitrate=(int)Math.Min(18000000,(value.Quality=="quality"?1800000:value.Quality=="low"?350000:600000)*scale);return RetroArchHostConfiguration.Recording.Replace("video_bit_rate = \"600000\"",$"video_bit_rate = \"{bitrate}\"",StringComparison.Ordinal);}
    public static string CoreOptions(NativeEmulationOptions value)=>RetroArchHostConfiguration.CoreOptions+$"mgba_audio_low_pass_filter = \"{(value.AudioFilter?"enabled":"disabled")}\"\nmgba_audio_low_pass_range = \"60\"\nmgba_solar_sensor_level = \"{value.SolarSensor}\"\n";
}
