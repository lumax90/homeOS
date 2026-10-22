using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Home.NativeTests;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;

if ((args.Length != 6 && args.Length != 8) || args[0] != "--review-root" || args[2] != "--output-root" || args[4] != "--core-build" || args.Length == 8 && args[6] != "--frontend-build") throw new ArgumentException("Use --review-root <cache> --output-root <isolated root> --core-build <released-core receipt> [--frontend-build <upstream-backport receipt>]");
var reviewRoot = Path.GetFullPath(args[1]); var outputRoot = AppFiles.DirectoryIn(Path.GetFullPath(args[3]), "attempt-" + Guid.NewGuid().ToString("N"));
foreach(var port in new[]{62401,62402,62403,62404,62405,62406,62420,62421,62422,62423})if(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint=>endpoint.Port==port)||System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint=>endpoint.Port==port))throw new InvalidOperationException("Native game ports are in use; diagnostic stopped.");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var retro = await RequireReview("retroarch"); var mtx = await RequireReview("mediamtx");
var executable = Path.Combine(reviewRoot, "retroarch", "files", retro.Files.Single(file => Path.GetFileName(file.Path) == "retroarch.exe").Path);
FrontendBuild? frontend = null;
if (args.Length == 8)
{
    frontend = JsonSerializer.Deserialize<FrontendBuild>(await File.ReadAllTextAsync(args[7]), options) ?? throw new InvalidDataException("Frontend build receipt required.");
    if (frontend.SourceVersion != "1.22.2" || frontend.SourceCommit != "69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576" || frontend.UpstreamInputFix != "cfb1204314dd6b061a7b1cda77221735939bf5ba" || !frontend.PatchedPrototype ||
        !Path.GetFullPath(frontend.ExecutablePath).StartsWith(reviewRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Exact isolated upstream backport required.");
    VerifiedFilePublication.Require(frontend.ExecutablePath, frontend.ExecutableBytes, frontend.ExecutableSha256);
    foreach (var file in frontend.Dependencies) VerifiedFilePublication.Require(file.Path, file.Bytes, file.Sha256);
    executable = frontend.ExecutablePath;
}
var build = JsonSerializer.Deserialize<CoreBuild>(await File.ReadAllTextAsync(args[5]), options) ?? throw new InvalidDataException("Missing core build receipt.");
if (build.SourceVersion != "0.10.5" || build.SourceCommit != "26b7884bc25a5933960f3cdcd98bac1ae14d42e2" || !Path.GetFullPath(build.CorePath).StartsWith(reviewRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A released, reviewed core inside this cache is required.");
VerifiedFilePublication.Require(build.CorePath, build.CoreBytes, build.CoreSha256); var coreFile = build.CorePath;
Console.WriteLine("SYNTHETIC ISOLATED NATIVE TEST — no user ROM, BIOS, library or browser is used.");
var version = await MediaProcess.RunAsync(executable, ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None, outputRoot);
var features = await MediaProcess.RunAsync(executable, ["--features"], TimeSpan.FromSeconds(10), CancellationToken.None, outputRoot);
var info = GetCoreInfo(coreFile);
if (info.Name != "mGBA" || info.Version != "0.10.5") throw new InvalidDataException("Development or unexpected core version cannot be promoted.");
await File.WriteAllBytesAsync(Path.Combine(outputRoot, "SYNTHETIC-TEST.gba"), SyntheticGba.Create());
await File.WriteAllTextAsync(Path.Combine(outputRoot, "README.txt"), "Original synthetic ARM fixture for isolated automated native emulation/recording tests only. Not a user game or a catalog entry. No Nintendo logo, external ROM or BIOS data. Never import this into a normal Home library.\n");
await File.WriteAllTextAsync(Path.Combine(outputRoot, "retroarch.cfg"), RetroArchHostConfiguration.Create(outputRoot));
await File.WriteAllTextAsync(Path.Combine(outputRoot, "core-options.cfg"), RetroArchHostConfiguration.CoreOptions);
await File.WriteAllTextAsync(Path.Combine(outputRoot,"record.cfg"),RetroArchHostConfiguration.Recording+(frontend?.HomeHostAdapter=="home-host-1"?RetroArchHostConfiguration.LowDelayMux:""));
var workspaceCore = PrepareCore(outputRoot);
var arguments = new[] { "--verbose", "--config", Path.Combine(outputRoot, "retroarch.cfg"), "-L", workspaceCore, "--record", Path.Combine(outputRoot, "SYNTHETIC-output.ts"), "--recordconfig", Path.Combine(outputRoot, "record.cfg"), "--max-frames", "180", Path.Combine(outputRoot, "SYNTHETIC-TEST.gba") };
var recordingClock = System.Diagnostics.Stopwatch.StartNew();
string? error = null;
try { await MediaProcess.RunAsync(executable, arguments, TimeSpan.FromSeconds(30), CancellationToken.None, outputRoot); }
catch (Exception reason) { error = reason.ToString(); }
recordingClock.Stop();
var recorded = Path.Combine(outputRoot, "SYNTHETIC-output.ts");
object? streaming = null;
if (error is null) try { streaming = await ProbeStreaming(); } catch (Exception failure) { error = failure.ToString(); }
var report = new { testedAt = DateTimeOffset.UtcNow, outputRoot, version, features, frontendSha256 = frontend?.ExecutableSha256 ?? retro.Files.Single(file => Path.GetFileName(file.Path) == "retroarch.exe").Sha256, patchedPrototype = frontend?.PatchedPrototype ?? false, upstreamInputFix = frontend?.UpstreamInputFix, core = info, build.CoreSha256, recordingSeconds = recordingClock.Elapsed.TotalSeconds, recordingExists = File.Exists(recorded), recordingBytes = File.Exists(recorded) ? new FileInfo(recorded).Length : 0, streaming, error, launchedUserGame = false, privateDesktopCapture = false, browserAcceptanceTested = false };
await File.WriteAllTextAsync(Path.Combine(outputRoot, "probe.json"), JsonSerializer.Serialize(report, options));
Console.WriteLine(JsonSerializer.Serialize(report, options));
if (error is not null) Environment.ExitCode = 1;

async Task<Review> RequireReview(string id)
{
    var folder = Path.Combine(reviewRoot, id); var path = MediaPaths.ResolveFile(folder, "review.json");
    var review = JsonSerializer.Deserialize<Review>(await File.ReadAllTextAsync(path), options) ?? throw new InvalidDataException("Invalid software review.");
    foreach (var file in review.Files) VerifiedFilePublication.Require(MediaPaths.ResolveFile(folder, "files/" + file.Path), file.Bytes, file.Sha256);
    return review;
}
async Task<object> ProbeStreaming()
{
    var directory = AppFiles.DirectoryIn(outputRoot, "streaming"); var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    await File.WriteAllTextAsync(Path.Combine(directory, "mediamtx.yml"), MediaMtxGameConfiguration.Create("synthetic-fixture", secret, 62402, 62403, 62404, 62405));
    var mediaExe = Path.Combine(reviewRoot, "mediamtx", "files", mtx.Files.Single(file => Path.GetFileName(file.Path) == "mediamtx.exe").Path);
    using var media = MediaProcess.Start(mediaExe, [Path.Combine(directory, "mediamtx.yml")], workingDirectory: directory);
    var mediaOut = MediaProcess.ReadErrorTailAsync(media.StandardOutput); var mediaError = MediaProcess.ReadErrorTailAsync(media.StandardError);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = limit.Token;
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("home-native:" + secret)));
    System.Diagnostics.Process? engine = null; RetroArchControl? control = null; Task<string>? engineError = null, engineOut = null;
    try
    {
        for (var i = 0; i < 50; i++) { if (media.HasExited) throw new InvalidOperationException(await mediaError); try { using var response = await client.GetAsync("http://127.0.0.1:62404/v3/config/global/get", token); if (response.IsSuccessStatusCode) break; } catch (HttpRequestException) { } await Task.Delay(50, token); }
        await File.WriteAllBytesAsync(Path.Combine(directory, "SYNTHETIC-TEST.gba"), SyntheticGba.Create());
         var loopback=frontend?.HostAdapter?.LoopbackOnly==true;
         if(!loopback&&!NativeLocalInputGuard.InspectPorts(executable,[62401,62406]))throw new InvalidOperationException("The native command/input listeners are not private.");
         var inputPort=loopback?62420:62401;
         if(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint=>endpoint.Port==62406||endpoint.Port>=inputPort&&endpoint.Port<inputPort+(loopback?4:1)))throw new InvalidOperationException("An isolated native input/command port is already in use.");
         await File.WriteAllTextAsync(Path.Combine(directory,"retroarch.cfg"),loopback?RetroArchHostConfiguration.CreatePrivateHost(directory,inputPort,62406,null):RetroArchHostConfiguration.Create(directory,inputPort,62406));
        await File.WriteAllTextAsync(Path.Combine(directory, "core-options.cfg"), RetroArchHostConfiguration.CoreOptions);
         await File.WriteAllTextAsync(Path.Combine(directory,"record.cfg"),RetroArchHostConfiguration.Recording+(frontend?.HomeHostAdapter=="home-host-1"?RetroArchHostConfiguration.LowDelayMux:""));
        var localCore = PrepareCore(directory);
        engine = MediaProcess.Start(executable, ["--verbose", "--config", Path.Combine(directory, "retroarch.cfg"), "-L", localCore, "--record", RetroArchHostConfiguration.LocalMpegTs(62402), "--recordconfig", Path.Combine(directory, "record.cfg"), Path.Combine(directory, "SYNTHETIC-TEST.gba")], workingDirectory: directory, redirectInput: true);
        engineError = MediaProcess.ReadErrorTailAsync(engine.StandardError); engineOut = MediaProcess.ReadErrorTailAsync(engine.StandardOutput); control = new RetroArchControl(engine, 62406);
        for (var i = 0; i < 100 && !System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint => endpoint.Port == 62406); i++) { if (engine.HasExited) throw new InvalidOperationException(await engineError); await Task.Delay(25, token); }
        string? ready = null;
        for (var i = 0; i < 100; i++)
        {
            if (engine.HasExited) throw new InvalidOperationException(await engineError);
            using var response = await client.GetAsync("http://127.0.0.1:62404/v3/paths/get/synthetic-fixture", token);
            if (response.IsSuccessStatusCode) { var text = await response.Content.ReadAsStringAsync(token); using var document = JsonDocument.Parse(text); if (document.RootElement.GetProperty("ready").GetBoolean()) { ready = text; break; } }
            await Task.Delay(50, token);
        }
        if (ready is null) throw new InvalidOperationException("The actual native MPEG-TS source did not become ready.");
         var initial = await control.StatusAsync(token);
          if(loopback&&System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(endpoint=>endpoint.Port==62406||endpoint.Port>=inputPort&&endpoint.Port<inputPort+4).Any(endpoint=>!IPAddress.IsLoopback(endpoint.Address)))throw new InvalidOperationException("Reviewed input sockets must bind loopback only.");
        await control.EnablePacedHeadlessModeAsync(token);
         using var pad = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); pad.Connect(IPAddress.Loopback,inputPort);
        await RetroPadProtocol.ReleaseAllAsync(pad, token);
        var releasedPixel = await WaitPixel(0x03e0, token);
        await pad.SendAsync(RetroPadProtocol.Button(8, true), token);
        var pressedPixel = await WaitPixel(0x001f, token);
        await Task.Delay(250,token);var heldPixel=await ReadPixel(token);if(heldPixel!=0x001f)throw new InvalidOperationException("Remote input did not remain held without repeated datagrams.");
        var held = await ReadFixture(token);
        var observations = new List<FixtureInput> { held };
        // The fixture paints a full Mode-3 screen; one sample can span multiple native frames.
        // Keep the ten-consecutive-sample requirement, but wait a bounded period rather than assuming 250 ms is enough.
        for (var i = 0; i < 50 && held.ConsecutiveHeldFrames < 10; i++)
        {
            await Task.Delay(20, token); var next = await ReadFixture(token); observations.Add(next);
            if (next.Keys != 0x03fe || next.ConsecutiveHeldFrames < held.ConsecutiveHeldFrames) throw new InvalidOperationException("A held input reset during consecutive native fixture samples.");
            held = next;
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "held-input-observations.json"), JsonSerializer.Serialize(observations, options), token);
        if (held.ConsecutiveHeldFrames < 10) throw new InvalidOperationException("Held input was not consecutive across at least ten native frames.");
        await pad.SendAsync(RetroPadProtocol.Button(0, true), token); await pad.SendAsync(RetroPadProtocol.Button(7, true), token);
        var combined = await WaitFixtureKeys(0x03ec, token); await Task.Delay(250, token);
        if ((await ReadFixture(token)).Keys != 0x03ec) throw new InvalidOperationException("Simultaneous A+B+Right did not remain held.");
        await pad.SendAsync(RetroPadProtocol.Button(0, false), token); await pad.SendAsync(RetroPadProtocol.Button(7, false), token);
        await WaitFixtureKeys(0x03fe, token);
        await control.SetPausedAsync(true, token); var paused = await control.StatusAsync(token);
        await control.SaveStateAsync(token); var state = Path.Combine(directory, "saves", "SYNTHETIC-TEST.state");
        for (var i = 0; i < 100 && !File.Exists(state); i++) await Task.Delay(25, token);
        if (!File.Exists(state) || new FileInfo(state).Length == 0) throw new InvalidOperationException("Native save state was not written.");
        var stateBytes = new FileInfo(state).Length; var stateHash = StateFolders.Hash(state);
        await RetroPadProtocol.ReleaseAllAsync(pad, token); await control.SetPausedAsync(false, token);
        await WaitPixel(0x03e0, token); await control.SetPausedAsync(true, token); await control.LoadStateAsync(token);
        var restoredPixel = await WaitPixel(0x001f, token);
        await control.SetPausedAsync(false, token); var resumed = await control.StatusAsync(token);
        var offer = SyntheticWhep.Offer(); using var sdp = new StringContent(offer, Encoding.UTF8, "application/sdp");
        using var negotiated = await client.PostAsync("http://127.0.0.1:62403/synthetic-fixture/whep", sdp, token);
        var answer = await negotiated.Content.ReadAsStringAsync(token);
        if ((int)negotiated.StatusCode != 201 || !answer.Contains("H264", StringComparison.Ordinal) || !answer.Contains("opus", StringComparison.Ordinal)) throw new InvalidOperationException("WHEP did not negotiate the actual H264/Opus source: " + negotiated.StatusCode + " " + answer);
        if (negotiated.Headers.Location is { } location) { var address = new Uri(new Uri("http://127.0.0.1:62403"), location); if (address.Host != "127.0.0.1" || address.Port != 62403) throw new InvalidOperationException("Unexpected WHEP resource."); using var deleted = await client.DeleteAsync(address, token); }
        var windowless = engine.MainWindowHandle == IntPtr.Zero;
        await File.WriteAllTextAsync(Path.Combine(directory, "negotiation.json"), JsonSerializer.Serialize(new { initial, paused, resumed, mediaSource = JsonSerializer.Deserialize<JsonElement>(ready), stateBytes, stateHash, releasedPixel, pressedPixel, restoredPixel, whepStatus = (int)negotiated.StatusCode, negotiatedH264Opus = true, windowless, browserDecodeVerified = false }, options));
        await control.QuitAsync(token); await engine.WaitForExitAsync(token);
         return new{initial,paused,resumed,mediaSource=JsonSerializer.Deserialize<JsonElement>(ready),stateBytes,stateHash,releasedPixel,pressedPixel,heldPixel,held,combined,simultaneousHeldInputVerified=true,restoredPixel,nativeSaveRestoreVerified=true,whepStatus=(int)negotiated.StatusCode,negotiatedH264Opus=true,windowless,remotePad="verified-through-original-GBA-fixture",privateInputGuard=loopback||NativeLocalInputGuard.Inspect(executable,62401),browserDecodeVerified=false};
    }
    finally
    {
        if (engine is not null) { MediaProcess.Kill(engine); await engine.WaitForExitAsync(CancellationToken.None); if (control is not null) await control.DisposeAsync(); if (engineError is not null) await File.WriteAllTextAsync(Path.Combine(directory, "engine.stderr.log"), await engineError); if (engineOut is not null) await File.WriteAllTextAsync(Path.Combine(directory, "engine.stdout.log"), await engineOut); engine.Dispose(); }
        MediaProcess.Kill(media); await media.WaitForExitAsync(CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "media.stdout.log"), await mediaOut); await File.WriteAllTextAsync(Path.Combine(directory, "media.stderr.log"), await mediaError);
    }
}
async Task<ushort> ReadPixel(CancellationToken token)
{
    return BinaryPrimitives.ReadUInt16LittleEndian(await ReadFixtureMemory("6000000", 2, token));
}
async Task<FixtureInput> ReadFixture(CancellationToken token)
{
    var bytes = await ReadFixtureMemory("2000000", 12, token);
    return new((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(bytes) & 0x03ff), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
}
async Task<FixtureInput> WaitFixtureKeys(ushort expected, CancellationToken token)
{
    for (var i = 0; i < 100; i++) { var value = await ReadFixture(token); if (value.Keys == expected) return value; await Task.Delay(20, token); }
    throw new InvalidOperationException("Original fixture did not receive expected simultaneous keys: " + expected.ToString("x4"));
}
async Task<byte[]> ReadFixtureMemory(string address, int length, CancellationToken token)
{
    using var debugger = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); debugger.Connect(IPAddress.Loopback, 62406);
    await debugger.SendAsync(Encoding.ASCII.GetBytes($"READ_CORE_MEMORY {address} {length}\n"), token);
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(2));
    var response = Encoding.ASCII.GetString((await debugger.ReceiveAsync(budget.Token)).Buffer).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (response.Length != length + 2 || response[0] != "READ_CORE_MEMORY" || response[1] != address) throw new InvalidDataException("Unexpected isolated fixture memory response.");
    return response.Skip(2).Select(value => Convert.ToByte(value, 16)).ToArray();
}
async Task<ushort> WaitPixel(ushort expected, CancellationToken token)
{
    for (var i = 0; i < 100; i++) { var value = await ReadPixel(token); if (value == expected) return value; await Task.Delay(20, token); }
    throw new InvalidOperationException("The isolated GBA fixture did not reach its expected pixel state: " + expected.ToString("x4"));
}
string PrepareCore(string directory)
{
    var target = Path.Combine(AppFiles.DirectoryIn(directory, "cores"), "mgba_libretro.dll"); File.Copy(coreFile, target, false);
    var metadata = retro.Files.Single(file => Path.GetFileName(file.Path) == "mgba_libretro.info");
    File.Copy(Path.Combine(reviewRoot, "retroarch", "files", metadata.Path), Path.Combine(AppFiles.DirectoryIn(directory, "info"), "mgba_libretro.info"), false);
    return target;
}
static CoreDescription GetCoreInfo(string file)
{
    var handle = NativeLibrary.Load(file);
    try
    {
        var getter = Marshal.GetDelegateForFunctionPointer<InfoGetter>(NativeLibrary.GetExport(handle, "retro_get_system_info"));
        getter(out var info);
        return new(Marshal.PtrToStringUTF8(info.Name) ?? "", Marshal.PtrToStringUTF8(info.Version) ?? "", Marshal.PtrToStringUTF8(info.Extensions) ?? "", info.NeedFullPath);
    }
    finally { NativeLibrary.Free(handle); }
}
record FileReview(string Path, long Bytes, string Sha256);
record Review(FileReview[] Files);
record CoreBuild(string SourceVersion, string SourceCommit, string CorePath, string CoreSha256, long CoreBytes);
record FrontendBuild(string SourceVersion,string SourceCommit,string UpstreamInputFix,bool PatchedPrototype,string ExecutablePath,long ExecutableBytes,string ExecutableSha256,NativeRuntimeFile[] Dependencies,string? HomeHostAdapter=null,HostAdapter? HostAdapter=null);
record HostAdapter(bool LoopbackOnly);
record FixtureInput(ushort Keys, uint SampleFrames, uint ConsecutiveHeldFrames);
record CoreDescription(string Name, string Version, string Extensions, bool NeedFullPath);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InfoGetter(out CoreInfo info);
[StructLayout(LayoutKind.Sequential)] struct CoreInfo { public IntPtr Name, Version, Extensions; [MarshalAs(UnmanagedType.I1)] public bool NeedFullPath; [MarshalAs(UnmanagedType.I1)] public bool BlockExtract; }
