using HomeServer.Core;

namespace HomeServer.Gaming;
public static class NativeCoreConfiguration
{
    public static string Options(string id)
    {
        if(id is "citra" or "citra2018" or "citra_canary" or "azahar")return ("citra_use_libretro_save_path = \"LibRetro Default\"\ncitra_use_virtual_sd = \"enabled\"\ncitra_use_gdbstub = \"disabled\"\ncitra_layout_option = \"Default Top-Bottom Screen\"\ncitra_resolution_factor = \"1x (Native)\"\ncitra_analog_function = \"C-Stick and Touchscreen Pointer\"\ncitra_render_touchscreen = \"enabled\"\ncitra_is_new_3ds = \"New 3DS\"\n").Replace("citra_",id=="citra2018"?"citra2018_":"citra_",StringComparison.Ordinal);
        return "";
    }
    public static string RequiredOptions(string id)
    {if(id is "citra" or "citra2018" or "citra_canary" or "azahar")return "citra_use_libretro_save_path = \"LibRetro Default\"\ncitra_use_virtual_sd = \"enabled\"\ncitra_use_gdbstub = \"disabled\"\n".Replace("citra_",id=="citra2018"?"citra2018_":"citra_",StringComparison.Ordinal);return "";}
    public static void PrepareSaveDirectories(string core,string root)
    {
        if(core is "citra" or "citra2018" or "citra_canary")AppFiles.DirectoryIn(root,"Citra");
        if(core=="azahar")AppFiles.DirectoryIn(root,"Azahar");
        if(core=="ppsspp")AppFiles.DirectoryIn(root,"PSP");
    }
}
