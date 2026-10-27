using HomeServer.Core;

namespace HomeServer.Gaming;
public static class NativeCoreConfiguration
{
    public static bool PspProfileManaged(string key)=>key is "ppsspp_backend" or "ppsspp_software_rendering" or "ppsspp_internal_resolution" or "ppsspp_cache_iso" or "ppsspp_cheats" or "ppsspp_enable_wlan" or "ppsspp_enable_upnp" or "ppsspp_enable_builtin_pro_ad_hoc_server";
    public static bool DsProfileManaged(string key)=>key is "desmume_opengl_mode" or "desmume_internal_resolution" or "desmume_screens_layout" or "desmume_screens_gap" or "desmume_pointer_type";
    public static string Options(string id)
    {
        if(id=="desmume")return "desmume_cpu_mode = \"jit\"\ndesmume_internal_resolution = \"256x192\"\ndesmume_opengl_mode = \"disabled\"\ndesmume_screens_layout = \"top/bottom\"\ndesmume_screens_gap = \"0\"\ndesmume_pointer_mouse = \"enabled\"\ndesmume_pointer_type = \"touch\"\ndesmume_frameskip = \"0\"\n";
        if(id=="ppsspp")return "ppsspp_cpu_core = \"JIT\"\nppsspp_internal_resolution = \"480x272\"\nppsspp_frameskip = \"0\"\nppsspp_memstick_inserted = \"enabled\"\n";
        if(id is "citra" or "citra2018" or "citra_canary" or "azahar")return ("citra_use_libretro_save_path = \"LibRetro Default\"\ncitra_use_virtual_sd = \"enabled\"\ncitra_use_gdbstub = \"disabled\"\ncitra_layout_option = \"Default Top-Bottom Screen\"\ncitra_resolution_factor = \"1x (Native)\"\ncitra_analog_function = \"C-Stick and Touchscreen Pointer\"\ncitra_touch_touchscreen = \"enabled\"\ncitra_mouse_touchscreen = \"enabled\"\ncitra_render_touchscreen = \"enabled\"\ncitra_is_new_3ds = \"New 3DS\"\n").Replace("citra_",id=="citra2018"?"citra2018_":"citra_",StringComparison.Ordinal);
        return "";
    }
    public static string RequiredOptions(string id,bool pspSoftware=false,bool handheld=false)
    {if(id=="desmume"&&handheld)return "desmume_opengl_mode = \"disabled\"\ndesmume_internal_resolution = \"256x192\"\ndesmume_screens_layout = \"top/bottom\"\ndesmume_screens_gap = \"0\"\ndesmume_pointer_type = \"touch\"\n";if(id=="ppsspp"&&pspSoftware)return "ppsspp_backend = \"none\"\nppsspp_software_rendering = \"enabled\"\nppsspp_internal_resolution = \"480x272\"\nppsspp_cache_iso = \"disabled\"\nppsspp_cheats = \"disabled\"\nppsspp_enable_wlan = \"disabled\"\nppsspp_enable_upnp = \"disabled\"\nppsspp_enable_builtin_pro_ad_hoc_server = \"disabled\"\n";if(id is "citra" or "citra2018" or "citra_canary" or "azahar")return "citra_use_libretro_save_path = \"LibRetro Default\"\ncitra_use_virtual_sd = \"enabled\"\ncitra_use_gdbstub = \"disabled\"\n".Replace("citra_",id=="citra2018"?"citra2018_":"citra_",StringComparison.Ordinal);return "";}
    public static void PrepareSaveDirectories(string core,string root)
    {
        if(core is "citra" or "citra2018" or "citra_canary")AppFiles.DirectoryIn(root,"Citra");
        if(core=="azahar")AppFiles.DirectoryIn(root,"Azahar");
        if(core=="ppsspp")AppFiles.DirectoryIn(root,"PSP");
    }
}
