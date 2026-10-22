using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Tests;

public sealed class NativeRuntimeAdapterTests
{
    [Fact]
    public void HeadlessProfileUsesManagedDirectoriesAndDoesNotExposeRemoteInterfacesByDefault()
    {
        using var home = new TestWorkspace(); var cfg = RetroArchHostConfiguration.Create(home.Root);
        Assert.Contains("video_driver = \"null\"", cfg); Assert.Contains("network_cmd_enable = \"false\"", cfg); Assert.Contains("network_remote_enable = \"false\"", cfg);
        Assert.Contains("input_player1_joypad_index = \"15\"", cfg); Assert.DoesNotContain("joypad_index = \"-1\"", cfg);
        Assert.Contains("microphone_enable = \"false\"", cfg); Assert.Contains("config_save_on_exit = \"false\"", cfg);
        Assert.Contains("input_autodetect_enable = \"false\"", cfg); Assert.Contains("input_overlay_enable = \"false\"", cfg);
        Assert.Contains("camera_driver = \"null\"", cfg); Assert.Contains("camera_allow = \"false\"", cfg);
        Assert.Contains("global_core_options = \"true\"",cfg);Assert.Contains("auto_overrides_enable = \"false\"",cfg);Assert.Contains("auto_remaps_enable = \"false\"",cfg);Assert.Contains("stdin_cmd_enable = \"false\"",cfg);
        Assert.Throws<DomainException>(() => RetroArchHostConfiguration.Create(home.Root, 80)); Assert.Throws<DomainException>(() => RetroArchHostConfiguration.Create(home.Root, 62401, 62401));
    }
    [Fact]
    public void RecordingProfileUsesUpstreamCodecPipelineAndOnlyLoopbackTransport()
    {
        Assert.Contains("vcodec = \"libx264\"", RetroArchHostConfiguration.Recording); Assert.Contains("acodec = \"libopus\"", RetroArchHostConfiguration.Recording); Assert.Contains("video_tune = \"zerolatency\"", RetroArchHostConfiguration.Recording);
        Assert.Contains("audio_application = \"lowdelay\"", RetroArchHostConfiguration.Recording); Assert.Contains("audio_frame_duration = \"2.5\"", RetroArchHostConfiguration.Recording);
        Assert.Contains("threads = \"1\"", RetroArchHostConfiguration.Recording);
        Assert.Equal("udp://127.0.0.1:62402?pkt_size=1316", RetroArchHostConfiguration.LocalMpegTs(62402)); Assert.Throws<DomainException>(() => RetroArchHostConfiguration.LocalMpegTs(65536));
        Assert.Throws<DomainException>(() => RetroArchHostConfiguration.Quote("unsafe\"\ncommand"));
    }
    [Fact]
    public void MediaServerProfileDisablesAllUnneededListenersAndUsesScopedCredentials()
    {
        using var cfg = JsonDocument.Parse(MediaMtxGameConfiguration.Create("fixture", new string('a', 64), 62402, 62403, 62404, 62405)); var root = cfg.RootElement;
        foreach (var name in new[] { "metrics", "pprof", "playback", "rtsp", "rtmp", "hls", "srt", "moq" }) Assert.False(root.GetProperty(name).GetBoolean());
        Assert.Equal("127.0.0.1:62403", root.GetProperty("webrtcAddress").GetString()); Assert.Equal("127.0.0.1:62405", root.GetProperty("webrtcLocalUDPAddress").GetString()); Assert.False(root.GetProperty("webrtcIPsFromInterfaces").GetBoolean());
        Assert.Equal("udp+mpegts://127.0.0.1:62402", root.GetProperty("paths").GetProperty("fixture").GetProperty("source").GetString()); Assert.Single(root.GetProperty("authInternalUsers").EnumerateArray());
        Assert.Throws<DomainException>(() => MediaMtxGameConfiguration.Create("../other", new string('a',64), 62402, 62403, 62404, 62405)); Assert.Throws<DomainException>(() => MediaMtxGameConfiguration.Create("fixture", new string('a',64), 62402, 62402, 62404, 62405));
    }
    [Fact]
    public void RetroPadAdapterMatchesTheReviewedWindowsAbiAndRejectsUnsupportedInputs()
    {
        var packet = RetroPadProtocol.Button(8, true); Assert.Equal(20, packet.Length); Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(packet)); Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12))); Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(16))); Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(RetroPadProtocol.Button(8, false).AsSpan(16)));
        var releases = RetroPadProtocol.ReleaseAll().ToArray(); Assert.Equal(10, releases.Length);
        Assert.Equal(10, releases.Select(packet => BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12))).Distinct().Count());
        Assert.Equal(RetroPadProtocol.GbaButtons, releases.Aggregate(0, (mask, packet) => mask | (1 << BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12)))));
        Assert.All(releases, packet => { Assert.Equal(20, packet.Length); Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4))); Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(16))); });
        Assert.Throws<DomainException>(() => RetroPadProtocol.Button(1, true)); Assert.Throws<DomainException>(() => RetroPadProtocol.Validate(ushort.MaxValue)); RetroPadProtocol.Validate(RetroPadProtocol.GbaButtons);
    }
    private const string ProgramPath = @"C:\isolated\retroarch.exe";
    [Fact]
    public void InputTransitionsAreBoundedReleaseFirstAndNeverRepeatUnchangedHeldButtons()
    {
        const ushort previous = (1 << 8) | (1 << 0); const ushort desired = (1 << 8) | (1 << 7) | (1 << 10);
        var packets = RetroPadProtocol.Changes(previous, desired).ToArray(); Assert.Equal(3, packets.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(packets[0].AsSpan(12))); Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packets[0].AsSpan(16)));
        Assert.Equal([7,10], packets.Skip(1).Select(packet => BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12))));
        Assert.All(packets.Skip(1), packet => Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(16))));
        Assert.Empty(RetroPadProtocol.Changes(desired, desired));
        Assert.Equal(10, RetroPadProtocol.Changes(RetroPadProtocol.GbaButtons, 0).Count());
        Assert.Throws<DomainException>(() => RetroPadProtocol.Changes(0, ushort.MaxValue).ToArray());
    }
    private static NativeInputFirewallSnapshot Policy(params NativeInputFirewallRule[] rules) => new([true,true,true],[0,0,0],false,rules);
    private static NativeInputFirewallRule Allow(string application = "", string ports = "*") => new(true,1,1,17,7,application,"",ports,"*","*");
    private static NativeInputFirewallRule Block(string application = ProgramPath, string remote = "0.0.0.0-126.255.255.255,128.0.0.0-255.255.255.255") => new(true,1,0,17,7,application,"","62401,62406","*",remote);
    [Fact]
    public void EffectiveFirewallPolicyIsFailClosedAndAnExactBlockOverridesGenericAllows()
    {
        Assert.True(NativeLocalInputGuard.IsPrivate(Policy(), ProgramPath, 62406)); Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Allow()), ProgramPath, 62406));
        Assert.True(NativeLocalInputGuard.IsPrivate(Policy(Allow(),Block()), ProgramPath, 62406)); Assert.True(NativeLocalInputGuard.IsPrivate(Policy(Allow(),Block()), ProgramPath, 62401));
        Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Allow(),Block(@"C:\other\retroarch.exe")), ProgramPath, 62406)); Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Allow(),Block(remote:"LocalSubnet")), ProgramPath, 62406));
        Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Allow(),Block(application:"")), ProgramPath, 62406)); Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Block()) with { EnabledProfiles = [true,false,true] }, ProgramPath, 62406));
        Assert.False(NativeLocalInputGuard.IsPrivate(Policy(Block()) with { HasExcludedInterfaces = true }, ProgramPath, 62406));
    }
    [Fact]
    public async Task NativeCommandAdapterSerializesNamedCommandsAndVerifiesPauseState()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var process = Process.GetCurrentProcess(); await using var control = new RetroArchControl(process, port); using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var commands = new List<string>(); var paused = false;
        var listener = Task.Run(async () =>
        {
            while (true)
            {
                var packet = await receiver.ReceiveAsync(budget.Token); var command = Encoding.ASCII.GetString(packet.Buffer).Trim(); commands.Add(command);
                if (command == "GET_STATUS") await receiver.SendAsync(Encoding.UTF8.GetBytes("GET_STATUS " + (paused ? "PAUSED" : "PLAYING") + " fixture,original,hash\n"), packet.RemoteEndPoint, budget.Token);
                else if (command == "PAUSE_TOGGLE") paused = !paused;
                else if (command == "QUIT") return;
            }
        });
        Assert.StartsWith("GET_STATUS PLAYING ", await control.StatusAsync(budget.Token)); await control.SetPausedAsync(true,budget.Token); await control.SetPausedAsync(true,budget.Token); Assert.True(paused);
        await control.SaveStateAsync(budget.Token); await control.LoadStateAsync(budget.Token); await control.SetPausedAsync(false,budget.Token); Assert.False(paused); await control.QuitAsync(budget.Token); await listener;
        Assert.Equal(2, commands.Count(command => command == "PAUSE_TOGGLE")); Assert.Contains("SAVE_STATE", commands); Assert.Contains("LOAD_STATE", commands); Assert.All(commands, command => Assert.Contains(command, new[] { "GET_STATUS", "PAUSE_TOGGLE", "SAVE_STATE", "LOAD_STATE", "QUIT" }));
    }
}
