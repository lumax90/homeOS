using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;

public sealed record NativeGameDisplay(string? Fit=null,bool? Smooth=null,int? Volume=null,string? Quality=null);
public sealed record NativeSettingsDocument(int Format,int Revision,string BinarySha256,Dictionary<string,string> Values,NativeGameDisplay? Display=null,NativeControllerChange[]? Controllers=null);
public sealed record NativeSettingsSchema(int Format,string BinarySha256,NativeCoreOption[] Options,NativeControllerPort[] Controllers);
public sealed record NativeSettingView(string Key,string Label,string Info,string[] Values,string Value,string InheritedValue,string Source,string Group,bool Common,bool Managed);
public sealed record NativeGameSettingsView(string GameId,string CoreId,string CoreName,string BinarySha256,int Revision,int CoreRevision,bool SchemaReady,NativeSettingView[] Options,NativeControllerPort[] ControllerTypes,NativeControllerChange[] CoreControllers,NativeControllerChange[] GameControllers,NativeGameDisplay CoreDisplay,NativeGameDisplay GameDisplay,bool RestartRequired=false,string ServerQuality="auto");
public sealed record NativeSettingsChange(string Scope,int Revision,Dictionary<string,string?>? Values=null,NativeGameDisplay? Display=null,NativeControllerChange[]? Controllers=null,bool Reset=false);

/// <summary>Version-bound profile/core defaults and sparse private game overrides. Never changes a ROM/save identity.</summary>
public sealed class NativeGameSettings(HomePaths paths)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly object _gate=new();
    private static string Segment(string value){if(!NativeSystems.SafeSegment(value))throw new DomainException("Oyun ayarı kapsamı geçersiz.",400);return value;}
    private string Root(string user,string core)=>Path.Combine(paths.State,"games","core-options",Segment(user),Segment(core));
    private string FilePath(string user,string core,string hash,string? game=null)
    {if(hash.Length!=64||hash.Any(c=>!char.IsAsciiHexDigit(c)))throw new DomainException("Çekirdek ayarı kimliği geçersiz.",409);return game is null?Path.Combine(Root(user,core),"defaults",hash+".json"):Path.Combine(Root(user,core),"games",hash,Segment(game)+".json");}
    private T? Read<T>(string path,int limit)
    {if(!File.Exists(path))return default;path=MediaPaths.ResolveFile(paths.State,Path.GetRelativePath(paths.State,path));if(new FileInfo(path).Length>limit)throw new DomainException("Oyun ayarı dosyası çok büyük.",409);try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json);}catch(JsonException){throw new DomainException("Oyun ayarı dosyası geçersiz.",409);}}
    private void EnsureDirectory(string path)=>AppFiles.DirectoryIn(paths.State,Path.GetRelativePath(paths.State,Path.GetDirectoryName(path)!).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
    private NativeSettingsDocument Core(string user,string core,string hash)
    {
        var modern=Read<NativeSettingsDocument>(FilePath(user,core,hash),256*1024);if(modern is not null)return Validate(modern,hash);
        var legacy=Read<SavedNativeCoreOptions>(Path.Combine(Root(user,core),"values.json"),128*1024);
        return legacy?.BinarySha256==hash?Validate(new(1,legacy.Revision,hash,legacy.Values,Controllers:Read<NativeControllerChange[]>(Path.Combine(Root(user,core),"controllers.json"),4096)),hash):new(1,0,hash,new(),Controllers:Read<NativeControllerChange[]>(Path.Combine(Root(user,core),"controllers.json"),4096));
    }
    private NativeSettingsDocument Game(string user,string core,string hash,string game)=>Validate(Read<NativeSettingsDocument>(FilePath(user,core,hash,game),256*1024)??new(1,0,hash,new()),hash);
    private static NativeSettingsDocument Validate(NativeSettingsDocument data,string hash)
    {
        if(data.Format!=1||data.Revision<0||data.BinarySha256!=hash||data.Values is null||data.Values.Count>512)throw new DomainException("Oyun ayarı dosyası geçersiz.",409);
        foreach(var pair in data.Values)NativeCoreOptions.ValidateChoice(pair.Key,pair.Value);ValidateDisplay(data.Display);
        if(data.Controllers is{} ports&&(ports.Length>4||ports.Select(p=>p.Port).Distinct().Count()!=ports.Length||ports.Any(p=>p.Port is <0 or >3||p.Device is <0 or >65535)))throw new DomainException("Kumanda ayarı geçersiz.",400);return data;
    }
    private static void ValidateDisplay(NativeGameDisplay? value)
    {if(value is not null&&(value.Fit is not(null or "contain" or "integer" or "fill")||value.Volume is <0 or >100||value.Quality is not(null or "auto" or "quality" or "low")))throw new DomainException("Oyun görüntü ayarı geçersiz.",400);}
    private string SchemaPath(string user,string core,string hash)=>Path.Combine(Root(user,core),"schemas",hash+".json");
    public void Observe(string user,string core,string hash,NativeCoreOption[] options,NativeControllerPort[] controllers)
    {
        lock(_gate)
        {
            _=FilePath(user,core,hash);if(Read<NativeSettingsSchema>(SchemaPath(user,core,hash),2*1024*1024) is{} known&&known.BinarySha256==hash)return;
            if(options.Length>512||options.Any(o=>o.Key.Length is <1 or >128||o.Label.Length>512||o.Info.Length>4096||o.Values.Length>128||!o.Values.Contains(o.Value))||controllers.Length>4)throw new DomainException("Çekirdek ayar tanımı geçersiz.",502);
            var schema=new NativeSettingsSchema(1,hash,options,controllers);var data=JsonSerializer.SerializeToUtf8Bytes(schema,Json);if(data.Length>2*1024*1024)throw new DomainException("Çekirdek ayar tanımı çok büyük.",502);
            EnsureDirectory(SchemaPath(user,core,hash));PackageTrust.AtomicJson(SchemaPath(user,core,hash),schema);
        }
    }
    public NativeGameSettingsView View(GameRom rom,string core,string name,string hash,bool restart=false)
    {
        lock(_gate)
        {
            var baseline=Core(rom.UserId,core,hash);var game=Game(rom.UserId,core,hash,rom.Id);var schema=Read<NativeSettingsSchema>(SchemaPath(rom.UserId,core,hash),2*1024*1024);
            if(schema is not null&&(schema.Format!=1||schema.BinarySha256!=hash||schema.Options.Length>512))throw new DomainException("Çekirdek ayar tanımı geçersiz.",409);
            var options=(schema?.Options??[]).Select(option=>
            {
                var inherited=baseline.Values.GetValueOrDefault(option.Key,option.Value);var value=game.Values.GetValueOrDefault(option.Key,inherited);var group=Group(option.Key);return new NativeSettingView(option.Key,option.Label,option.Info,option.Values,value,inherited,game.Values.ContainsKey(option.Key)?"game":baseline.Values.ContainsKey(option.Key)?"core":"default",group,Common(core,option.Key),option.Managed||NativeCoreOptions.IsManaged(option.Key));
            }).ToArray();
            return new(rom.Id,core,name,hash,game.Revision,baseline.Revision,schema is not null,options,schema?.Controllers??[],baseline.Controllers??[],game.Controllers??[],baseline.Display??new(),game.Display??new(),restart);
        }
    }
    public NativeGameSettingsView Save(GameRom rom,string core,string name,string hash,NativeSettingsChange change)
    {
        lock(_gate)
        {
            if(change.Scope is not("game" or "core")||change.Reset&&change.Scope!="game")throw new DomainException("Oyun ayarı kapsamı geçersiz.",400);
            var view=View(rom,core,name,hash);var previous=change.Scope=="game"?Game(rom.UserId,core,hash,rom.Id):Core(rom.UserId,core,hash);if(previous.Revision!=change.Revision)throw new DomainException("Oyun ayarları değişti; yeniden yükle.",409);
            var values=new Dictionary<string,string>(previous.Values,StringComparer.Ordinal);
            if(change.Values?.Count>512)throw new DomainException("Oyun ayarı dosyası çok büyük.",400);
            foreach(var pair in change.Values??new Dictionary<string,string?>())
            {
                var option=view.Options.FirstOrDefault(o=>o.Key==pair.Key)??throw new DomainException("Çekirdek ayarı bulunamadı.",404);if(option.Managed)throw new DomainException("Bu çekirdek ayarını Home yönetir.",409);
                if(pair.Value is null){if(change.Scope!="game")throw new DomainException("Emülatör varsayılanı için bir değer seç.",400);values.Remove(pair.Key);}
                else{if(!option.Values.Contains(pair.Value))throw new DomainException("Çekirdek ayarı desteklenmiyor.",400);NativeCoreOptions.ValidateChoice(pair.Key,pair.Value);values[pair.Key]=pair.Value;}
            }
            var controllers=change.Controllers??previous.Controllers;
            if(change.Controllers is not null)foreach(var port in change.Controllers){var definition=view.ControllerTypes.FirstOrDefault(p=>p.Port==port.Port);if(definition is null||!definition.Items.Any(item=>item.Id==port.Device))throw new DomainException("Kumanda seçimi desteklenmiyor.",400);}
            var saved=Validate(new(1,previous.Revision+1,hash,change.Reset?new():values,change.Reset?null:change.Display??previous.Display,change.Reset?null:controllers),hash);
            var path=FilePath(rom.UserId,core,hash,change.Scope=="game"?rom.Id:null);EnsureDirectory(path);PackageTrust.AtomicJson(path,saved);return View(rom,core,name,hash);
        }
    }
    public string Configuration(string user,string core,string hash,string game)
    {
        lock(_gate){var values=new Dictionary<string,string>(Core(user,core,hash).Values);foreach(var pair in Game(user,core,hash,game).Values)values[pair.Key]=pair.Value;return string.Join('\n',values.Where(p=>!NativeCoreOptions.IsManaged(p.Key)).Select(p=>p.Key+" = "+RetroArchHostConfiguration.Quote(p.Value)))+"\n";}
    }
    public string ControllerConfiguration(string user,string core,string hash,string game)
    {lock(_gate){var ports=(Core(user,core,hash).Controllers??[]).ToDictionary(p=>p.Port,p=>p.Device);foreach(var p in Game(user,core,hash,game).Controllers??[])ports[p.Port]=p.Device;return string.Join('\n',ports.Select(p=>$"input_libretro_device_p{p.Key+1} = \"{p.Value}\""))+"\n";}}
    public string? Quality(string user,string core,string hash,string game)
    {lock(_gate)return Game(user,core,hash,game).Display?.Quality??Core(user,core,hash).Display?.Quality;}
    private static string Group(string key)=>new[]{"palette","color","dither","resol","enhance","video","screen","overscan","layout","aspect","filtering"}.Any(k=>key.Contains(k,StringComparison.OrdinalIgnoreCase))?"video":new[]{"audio","sound","snd","interpol","low_pass"}.Any(k=>key.Contains(k,StringComparison.OrdinalIgnoreCase))?"audio":new[]{"input","controller","button","turbo","analog","solar"}.Any(k=>key.Contains(k,StringComparison.OrdinalIgnoreCase))?"controls":"compatibility";
    private static bool Common(string core,string key)=>core switch
    {
        "pcsx_rearmed"=>new[]{"pcsx_rearmed_neon_enhancement_enable","pcsx_rearmed_dithering","pcsx_rearmed_region","pcsx_rearmed_neon_interlace_enable_v2","pcsx_rearmed_neon_enhancement_tex_adj_v2"}.Contains(key),
        "mgba"=>new[]{"mgba_gb_colors","mgba_color_correction","mgba_audio_low_pass_filter","mgba_solar_sensor_level","mgba_gb_model"}.Contains(key),
        "fceumm"=>new[]{"fceumm_palette","fceumm_overscan","fceumm_region","fceumm_sndquality"}.Contains(key),
        "snes9x"=>new[]{"snes9x_region","snes9x_overscan","snes9x_hires_blend","snes9x_gfx_hires","snes9x_audio_interpolation"}.Contains(key),
        "genesis_plus_gx"=>new[]{"genesis_plus_gx_region_detect","genesis_plus_gx_aspect_ratio","genesis_plus_gx_overscan","genesis_plus_gx_audio_filter"}.Contains(key),
        _=>false
    };
}
