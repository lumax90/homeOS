using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>Configuration adapter for upstream RetroArch's own recording and command interfaces.</summary>
public static class RetroArchHostConfiguration
{
    public static string Create(string workspace, int? remotePadPort = null, int? commandPort = null)
    {
        var system = AppFiles.DirectoryIn(workspace, "system"); var saves = AppFiles.DirectoryIn(workspace, "saves");
        var cores = AppFiles.DirectoryIn(workspace, "cores"); var info = AppFiles.DirectoryIn(workspace, "info");
        if (remotePadPort is { } port && port is < 1024 or > 65535) throw new DomainException("Yerel oyun giriş portu geçersiz.");
        if (commandPort is { } cmd && (cmd is < 1024 or > 65535 || cmd == remotePadPort)) throw new DomainException("Yerel oyun komut portu geçersiz.");
        return """
            video_driver = "null"
            audio_driver = "null"
            audio_enable = "false"
            input_driver = "null"
            input_joypad_driver = "null"
            input_autodetect_enable = "false"
            input_overlay_enable = "false"
            camera_driver = "null"
            camera_allow = "false"
            video_vsync = "false"
            audio_sync = "false"
            fastforward_ratio = "1.0"
            video_shader_enable = "false"
            video_gpu_record = "false"
            pause_nonactive = "false"
             config_save_on_exit = "false"
             global_core_options = "true"
             auto_overrides_enable = "false"
             auto_remaps_enable = "false"
             auto_shaders_enable = "false"
            quit_press_twice = "false"
             stdin_cmd_enable = "false"
            microphone_enable = "false"
            midi_driver = "null"
            input_player1_joypad_index = "15"
            history_list_enable = "false"
            sort_savefiles_enable = "false"
            sort_savestates_enable = "false"
            menu_show_start_screen = "false"
            savestate_file_compression = "false"
            savestate_auto_index = "false"
            state_slot = "0"
            savestate_auto_save = "false"
            savestate_auto_load = "false"
            record_driver = "ffmpeg"
            core_info_cache_enable = "false"
            """ + $"\nsystem_directory = {Quote(system)}\nsavefile_directory = {Quote(saves)}\nsavestate_directory = {Quote(saves)}\nlibretro_directory = {Quote(cores)}\nlibretro_info_path = {Quote(info)}\ncore_options_path = {Quote(Path.Combine(workspace, "core-options.cfg"))}\nrgui_config_directory = {Quote(workspace)}\nplaylist_directory = {Quote(workspace)}\n" +
            (remotePadPort is null ? "network_remote_enable = \"false\"\n" : $"network_remote_enable = \"true\"\nnetwork_remote_enable_user_p1 = \"true\"\nnetwork_remote_base_port = \"{remotePadPort}\"\n") +
            (commandPort is null ? "network_cmd_enable = \"false\"\n" : $"network_cmd_enable = \"true\"\nnetwork_cmd_port = \"{commandPort}\"\n");
    }
    public const string Recording = """
        vcodec = "libx264"
        acodec = "libopus"
        format = "mpegts"
        pix_fmt = "yuv420p"
        sample_rate = "48000"
        audio_enable = "true"
        scale_factor = "1"
        threads = "1"
        frame_drop_ratio = "1"
        video_preset = "ultrafast"
        video_tune = "zerolatency"
        video_profile = "baseline"
        video_g = "30"
        video_bf = "0"
        audio_application = "lowdelay"
        audio_frame_duration = "2.5"
        audio_bit_rate = "64000"
        video_bit_rate = "600000"
        """;
    public const string CoreOptions = "mgba_use_bios = \"OFF\"\nmgba_skip_bios = \"ON\"\n";
    // Read by the reviewed Home host adapter and passed directly to upstream FFmpeg's muxer.
    public const string LowDelayMux="\nhome_low_delay = \"true\"\nformat_flush_packets = \"1\"\nformat_max_delay = \"0\"\nformat_max_interleave_delta = \"1\"\nformat_pes_payload_size = \"0\"\n";
    public const string ExtraControllerPorts="\ninput_max_users = \"4\"\nnetwork_remote_enable_user_p2 = \"true\"\nnetwork_remote_enable_user_p3 = \"true\"\nnetwork_remote_enable_user_p4 = \"true\"\n";
    public static string CreateSelkies(string workspace,int remotePadPort,int commandPort,string? renderer,int width,int height)=>
        CreatePrivateHost(workspace,remotePadPort,commandPort,renderer??"glcore",width,height)
            .Replace("video_driver = \"glcore\"",renderer is null?"video_driver = \"sdl2\"":"video_driver = \"glcore\"",StringComparison.Ordinal)
            .Replace("audio_driver = \"null\"","audio_driver = \"pulse\"",StringComparison.Ordinal)
            .Replace("audio_enable = \"false\"","audio_enable = \"true\"",StringComparison.Ordinal)
            .Replace("audio_sync = \"false\"","audio_sync = \"true\"",StringComparison.Ordinal)
            .Replace("video_gpu_record = \"true\"","video_gpu_record = \"false\"",StringComparison.Ordinal)
            .Replace("video_fullscreen = \"false\"","video_fullscreen = \"true\"",StringComparison.Ordinal)
        +$"\naudio_latency = \"32\"\naudio_out_rate = \"48000\"\nvideo_fullscreen_x = \"{width}\"\nvideo_fullscreen_y = \"{height}\"\nvideo_font_enable = \"false\"\nvideo_smooth = \"false\"\nsuspend_screensaver_enable = \"false\"\n";
    public static string CreatePrivateHost(string workspace,int remotePadPort,int commandPort,string? renderer,int width=640,int height=480)
    {
        // A software core stays windowless; an actual hardware-context request can still switch drivers upstream.
        var text=CreateGpu(workspace,remotePadPort,commandPort,renderer??"glcore",width,height);
        if(renderer is null)text=text.Replace("video_driver = \"glcore\"","video_driver = \"null\"",StringComparison.Ordinal);
        return text+ExtraControllerPorts;
    }
    public static string CreateGpu(string workspace,int? remotePadPort=null,int? commandPort=null,string renderer="glcore",int width=400,int height=480)
    {
        if(renderer is not("gl" or "glcore" or "vulkan" or "d3d11")||width is <160 or >1920||height is <144 or >1080)throw new DomainException("GPU görüntü ayarı geçersiz.");
        return Create(workspace,remotePadPort,commandPort)
        .Replace("video_driver = \"null\"","video_driver = "+Quote(renderer),StringComparison.Ordinal)
        .Replace("video_gpu_record = \"false\"","video_gpu_record = \"true\"",StringComparison.Ordinal)+
        $"\nvideo_fullscreen = \"false\"\nvideo_window_save_positions = \"false\"\nvideo_windowed_fullscreen = \"false\"\nvideo_window_width = \"{width}\"\nvideo_window_height = \"{height}\"\nvideo_scale = \"1\"\nvideo_force_aspect = \"true\"\nvideo_threaded = \"false\"\nvideo_gpu_screenshot = \"false\"\n"+(renderer=="vulkan"?"video_max_swapchain_images = \"2\"\n":"");
    }
    public static string LocalMpegTs(int port)
    {
        if (port is < 1024 or > 65535) throw new DomainException("Yerel oyun yayın portu geçersiz.");
        return $"udp://127.0.0.1:{port}?pkt_size=1316";
    }
    public static string Quote(string value)
    {
        if (value.Any(c => char.IsControl(c) || c == '"')) throw new DomainException("Yerel oyun ayarı geçersiz.");
        return "\"" + value.Replace('\\', '/') + "\"";
    }
}
