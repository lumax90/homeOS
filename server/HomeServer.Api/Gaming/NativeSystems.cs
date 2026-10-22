using HomeServer.Core;

namespace HomeServer.Gaming;

public sealed record NativeSystem(string Id,string Name,string Core,string[] Extensions,long MaximumBytes,string? ArtworkDatabase=null,bool Digital=true);
public static class NativeSystems
{
    private const long MiB=1024*1024,GiB=1024*MiB;
    // Formats and adapter routing, not a claim that every core/device is already playable.
    private static readonly NativeSystem[] Primary=[
        new("gba","Game Boy Advance","mgba",["gba"],32*MiB,"Nintendo - Game Boy Advance"),
        new("gb","Game Boy","mgba",["gb","dmg","sgb"],8*MiB,"Nintendo - Game Boy"),
        new("gbc","Game Boy Color","mgba",["gbc","cgb"],8*MiB,"Nintendo - Game Boy Color"),
        new("nes","NES / Famicom","fceumm",["nes","unf","unif","fds"],64*MiB,"Nintendo - Nintendo Entertainment System"),
        new("snes","Super Nintendo","snes9x",["smc","sfc","swc","fig"],64*MiB,"Nintendo - Super Nintendo Entertainment System"),
        new("mastersystem","Master System","genesis_plus_gx",["sms","bms","bin"],16*MiB,"Sega - Master System - Mark III"),
        new("gamegear","Game Gear","genesis_plus_gx",["gg","bin"],16*MiB,"Sega - Game Gear"),
        new("megadrive","Mega Drive / Genesis","genesis_plus_gx",["md","gen","smd","mdx","bin"],64*MiB,"Sega - Mega Drive - Genesis"),
        new("segacd","Mega-CD / Sega CD","genesis_plus_gx",["chd","cue","iso","m3u"],8*GiB,"Sega - Mega-CD - Sega CD"),
        new("sega32x","Sega 32X","picodrive",["32x","bin","smd","md"],64*MiB,"Sega - 32X"),
        new("ps1","PlayStation","pcsx_rearmed",["chd","pbp","iso","img","cue","m3u"],16*GiB,"Sony - PlayStation"),
        new("ps2","PlayStation 2","pcsx2",["iso","chd","cso","zso","cue","m3u"],64*GiB,"Sony - PlayStation 2",false),
        new("psp","PlayStation Portable","ppsspp",["iso","cso","pbp","chd"],16*GiB,"Sony - PlayStation Portable",false),
        new("n64","Nintendo 64","mupen64plus_next",["n64","z64","v64"],512*MiB,"Nintendo - Nintendo 64",false),
        new("nds","Nintendo DS","desmume",["nds"],512*MiB,"Nintendo - Nintendo DS",false),
        new("3ds","Nintendo 3DS","citra",["3ds","cci","cxi","3dsx"],16*GiB,"Nintendo - Nintendo 3DS",false),
        new("gamecube","GameCube","dolphin",["gcm","iso","rvz","gcz","ciso","wia","m3u"],16*GiB,"Nintendo - GameCube",false),
        new("wii","Wii","dolphin",["iso","wbfs","rvz","gcz","ciso","wia"],64*GiB,"Nintendo - Wii",false),
        new("wiiu","Wii U","cemu",["wud","wux","wua"],64*GiB,"Nintendo - Wii U",false),
        new("xbox","Xbox","xemu",["iso","xiso"],64*GiB,"Microsoft - Xbox",false),
        new("saturn","Saturn","mednafen_saturn",["chd","cue","m3u"],16*GiB,"Sega - Saturn"),
        new("dreamcast","Dreamcast","flycast",["chd","cdi","gdi","m3u"],16*GiB,"Sega - Dreamcast",false),
        new("pcengine","PC Engine / TurboGrafx-16","mednafen_pce_fast",["pce"],16*MiB,"NEC - PC Engine - TurboGrafx 16"),
        new("pcenginecd","PC Engine CD","mednafen_pce_fast",["chd","cue","m3u"],8*GiB,"NEC - PC Engine CD - TurboGrafx-CD"),
        new("supergrafx","SuperGrafx","mednafen_supergrafx",["pce","sgx"],16*MiB,"NEC - PC Engine SuperGrafx"),
        new("atari2600","Atari 2600","stella",["a26","bin"],4*MiB,"Atari - 2600"),
        new("atari7800","Atari 7800","prosystem",["a78","bin"],4*MiB,"Atari - 7800"),
        new("lynx","Atari Lynx","handy",["lnx"],16*MiB,"Atari - Lynx"),
        new("jaguar","Atari Jaguar","virtualjaguar",["j64","jag","rom","abs"],64*MiB,"Atari - Jaguar"),
        new("ngp","Neo Geo Pocket / Color","mednafen_ngp",["ngp","ngc","ngpc","npc"],16*MiB,"SNK - Neo Geo Pocket Color"),
        new("wonderswan","WonderSwan / Color","mednafen_wswan",["ws","wsc"],16*MiB,"Bandai - WonderSwan Color"),
        new("virtualboy","Virtual Boy","mednafen_vb",["vb","vboy"],16*MiB,"Nintendo - Virtual Boy"),
        new("colecovision","ColecoVision","gearcoleco",["col","cv","rom","bin"],16*MiB,"Coleco - ColecoVision"),
        new("pokemini","Pokémon Mini","pokemini",["min"],16*MiB,"Nintendo - Pokemon Mini"),
        new("arcade","Arcade / Neo Geo","fbneo",["zip"],2*GiB,null,false)
    ];
    private static NativeSystem[] _all=Primary;
    public static NativeSystem[] All=>_all;
    private static readonly Dictionary<string,string> Aliases=new(){{"game_boy","gb"},{"game_boy_advance","gba"},{"super_nes","snes"},{"mega_drive","megadrive"},{"master_system","mastersystem"},{"nintendo_64","n64"},{"playstation","ps1"},{"playstation2","ps2"},{"playstation_portable","psp"},{"pc_engine","pcengine"},{"sega_saturn","saturn"},{"atari_2600","atari2600"},{"atari_7800","atari7800"},{"atari_lynx","lynx"},{"atari_jaguar","jaguar"},{"neo_geo_pocket","ngp"},{"virtual_boy","virtualboy"},{"pokemon_mini","pokemini"}};
    public static string CanonicalId(string id)=>Aliases.GetValueOrDefault(id,id);
    public static void RegisterOfficial(RetroArchCore[] cores)
    {
        var systems=Primary.ToList();
        foreach(var group in cores.Where(core=>core.Categories.Contains("Emulator")&&core.System.Length is >0 and <=40&&core.System.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')).GroupBy(core=>core.System))
        {
            if(Aliases.ContainsKey(group.Key)||systems.Any(system=>system.Id==group.Key))continue;
            var options=group.OrderByDescending(core=>core.WindowsBuild).ThenByDescending(core=>core.SaveState).ThenBy(core=>core.Experimental).ToArray();var core=options[0];
            var extensions=options.SelectMany(item=>item.Extensions).Select(value=>value.ToLowerInvariant()).Where(value=>value.Length is >0 and <16&&value.All(c=>char.IsAsciiLetterOrDigit(c))&&!new[]{"exe","dll","sys","bat","cmd","ps1","sh","lnk"}.Contains(value)).Distinct().ToArray();
            if(extensions.Length==0)continue;
            systems.Add(new(group.Key,string.IsNullOrWhiteSpace(core.SystemName)?core.DisplayName:core.SystemName,core.Id,extensions,64L*1024*1024*1024,core.Databases.FirstOrDefault(),false));
        }
        _all=systems.ToArray();
    }
    public static NativeSystem Require(string id)=>All.FirstOrDefault(system=>system.Id==id)??throw new DomainException("Oyun sistemi desteklenmiyor.",400);
    public static void ValidateFileName(string name)
    {
        if(name.Length is <3 or >124||name!=name.Trim()||name.StartsWith('.')||name.Contains("..",StringComparison.Ordinal)||name.EndsWith(' ')||name.Contains('/')||name.Contains('\\')||name.Any(c=>char.IsControl(c)||c is ':' or '"' or '<' or '>' or '|' or '*' or '?'))throw new DomainException("ROM dosyası adı geçersiz.");
        var stem=name.Split('.')[0];if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem,StringComparer.OrdinalIgnoreCase))throw new DomainException("ROM dosyası adı geçersiz.");
    }
    public static bool SafeSegment(string value)
    {
        if(value.Length is <1 or >124||value is "." or ".."||value.EndsWith('.')||value!=value.Trim()||value.Any(c=>char.IsControl(c)||c is ':' or '"' or '<' or '>' or '|' or '*' or '?' or '/' or '\\'))return false;
        var stem=value.Split('.')[0];return !new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem,StringComparer.OrdinalIgnoreCase);
    }
    public static string Extension(string name)=>Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
    public static void ValidateName(string system,string name)
    {ValidateFileName(name);if(!Require(system).Extensions.Contains(Extension(name)))throw new DomainException("Dosya biçimi seçilen oyun sistemiyle eşleşmiyor.",400);}
    public static string ContentName(GameRom rom)=>"game."+Extension(rom.FileName);
}
