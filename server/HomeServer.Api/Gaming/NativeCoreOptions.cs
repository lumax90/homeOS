using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Gaming;
public sealed record NativeCoreOption(int Index,string Key,string Label,string Info,string Value,string[] Values,bool Managed=false,bool PendingRestart=false);
public sealed record NativeCoreOptionChange(string Key,string Value);
public sealed record SavedNativeCoreOptions(int Revision,string BinarySha256,Dictionary<string,string> Values);
public sealed record NativeControllerType(int Id,string Name);
public sealed record NativeControllerPort(int Port,int Device,NativeControllerType[] Items);
public sealed record NativeControllerChange(int Port,int Device);
public sealed class NativeCoreOptions(HomePaths paths)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static bool Managed(string key)=>key.Contains("save_path",StringComparison.OrdinalIgnoreCase)||key.Contains("save_location",StringComparison.OrdinalIgnoreCase)||key.Contains("libretro_save",StringComparison.OrdinalIgnoreCase)||key.Contains("gdbstub",StringComparison.OrdinalIgnoreCase)||key is "cemu_wua_output_dir" or "cemu_convert_to_wua";
    public static bool IsManaged(string key)=>Managed(key)||key.Contains("gdb_stub",StringComparison.OrdinalIgnoreCase);
    private string Root(string user,string core)=>Path.Combine(paths.State,"games","core-options",user,core);
    public string Configuration(string user,string core,string hash)
    {
        var path=Path.Combine(Root(user,core),"values.json");if(!File.Exists(path))return "";path=MediaPaths.ResolveFile(Root(user,core),"values.json");if(new FileInfo(path).Length>128*1024)throw new DomainException("Çekirdek ayar dosyası geçersiz.",409);
        var value=JsonSerializer.Deserialize<SavedNativeCoreOptions>(File.ReadAllText(path),Json)??throw new DomainException("Çekirdek ayar dosyası geçersiz.",409);if(value.BinarySha256!=hash)return "";
        ValidateValues(value.Values);
        return string.Join('\n',value.Values.Where(pair=>!Managed(pair.Key)).Select(pair=>pair.Key+" = "+RetroArchHostConfiguration.Quote(pair.Value)))+"\n";
    }
    public NativeCoreOption[] Decorate(NativeCoreOption[] options)=>options.Select(option=>option with{Managed=Managed(option.Key)}).ToArray();
    public static bool NeedsRestart(NativeCoreOption option)=>option.Label.Contains("restart",StringComparison.OrdinalIgnoreCase)||new[]{"resolution","enhancement","layout","renderer","graphics","screen","scale","render_3d"}.Any(part=>option.Key.Contains(part,StringComparison.OrdinalIgnoreCase));
    private static void ValidateValues(Dictionary<string,string>? values)
    {if(values is null||values.Count>512||values.Any(pair=>pair.Key.Length is <1 or >128||pair.Key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('_' or '-'))||pair.Value is null||pair.Value.Length>512||pair.Value.Any(c=>char.IsControl(c)||c=='"')))throw new DomainException("Çekirdek ayar dosyası geçersiz.",409);}
    public static void ValidateChoice(string key,string value)=>ValidateValues(new(){{key,value}});
    public string ControllerConfiguration(string user,string core)
    {
        var path=Path.Combine(Root(user,core),"controllers.json");if(!File.Exists(path))return "";path=MediaPaths.ResolveFile(Root(user,core),"controllers.json");if(new FileInfo(path).Length>4096)throw new DomainException("Kumanda ayar dosyası geçersiz.",409);var ports=JsonSerializer.Deserialize<NativeControllerChange[]>(File.ReadAllText(path),Json)??[];if(ports.Length>4||ports.Any(port=>port.Port is <0 or >3||port.Device is <0 or >65535))throw new DomainException("Kumanda ayar dosyası geçersiz.",409);return string.Join('\n',ports.Select(port=>$"input_libretro_device_p{port.Port+1} = \"{port.Device}\""))+"\n";
    }
    public void SaveControllers(string user,string core,NativeControllerPort[] ports)
    {var root=AppFiles.DirectoryIn(paths.State,"games","core-options",user,core);PackageTrust.AtomicJson(Path.Combine(root,"controllers.json"),ports.Select(port=>new NativeControllerChange(port.Port,port.Device)).ToArray());}
    public void Save(string user,string core,string hash,IEnumerable<NativeCoreOption> options)
    {
        var root=AppFiles.DirectoryIn(paths.State,"games","core-options",user,core);var path=Path.Combine(root,"values.json");var values=new Dictionary<string,string>(StringComparer.Ordinal);if(File.Exists(path)){path=MediaPaths.ResolveFile(root,"values.json");if(new FileInfo(path).Length>128*1024)throw new DomainException("Çekirdek ayar dosyası geçersiz.",409);var previous=JsonSerializer.Deserialize<SavedNativeCoreOptions>(File.ReadAllText(path),Json);if(previous?.BinarySha256==hash){ValidateValues(previous.Values);values=previous.Values;}}foreach(var option in options.Where(option=>!Managed(option.Key)))values[option.Key]=option.Value;ValidateValues(values);PackageTrust.AtomicJson(path,new SavedNativeCoreOptions(1,hash,values));
    }
}
