using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using HomeServer.Gaming;
using System.Diagnostics;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Usenet;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

if (args.Length == 2 && args[0] == "--measure-input-guard")
{
    var measurements = new List<object>();
    for (var attempt = 0; attempt < 3; attempt++)
    {
        var clock = Stopwatch.StartNew(); var first = NativeLocalInputGuard.Inspect(args[1], 62401); var second = NativeLocalInputGuard.Inspect(args[1], 62406); clock.Stop();
        measurements.Add(new { attempt, milliseconds = clock.Elapsed.TotalMilliseconds, privateInput = first && second });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { measurements, launchedGame = false, changedFirewall = false })); return;
}
if (args.Length == 2 && args[0] == "--inspect-ffmpeg")
{
    var inspectRoot = Path.GetFullPath(args[1]);
    var review = JsonSerializer.Deserialize<SoftwareReview>(await File.ReadAllTextAsync(MediaPaths.ResolveFile(inspectRoot, "review.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Reviewed RetroArch files required.");
    var file = review.Files.Single(file => Path.GetFileName(file.Path) == "avutil-56.dll");
    var path = MediaPaths.ResolveFile(inspectRoot, "files/" + file.Path); VerifiedFilePublication.Require(path, file.Bytes, file.Sha256);
    var handle = NativeLibrary.Load(path);
    try
    {
        var version = Marshal.GetDelegateForFunctionPointer<FfmpegVersion>(NativeLibrary.GetExport(handle, "av_version_info"));
        Console.WriteLine(JsonSerializer.Serialize(new { version = Marshal.PtrToStringUTF8(version()), file.Sha256, launchedEmulator = false, openedListener = false }));
    }
    finally { NativeLibrary.Free(handle); }
    return;
}
if (args.Length == 2 && args[0] == "--inspect-archive")
{
    AppFiles.RejectLink(args[1]);
    using var inspected = ArchiveFactory.OpenArchive(args[1], ReaderOptions.ForFilePath);
    Console.WriteLine(JsonSerializer.Serialize(new { entries = inspected.Entries.Where(entry => entry.Size > 256L * 1024 * 1024 || (entry.Key ?? "").Contains("mgba", StringComparison.OrdinalIgnoreCase)).Select(entry => new { entry.Key, entry.Size, entry.IsDirectory }), duplicatePaths = inspected.Entries.Where(entry => !entry.IsDirectory).GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key) }));
    return;
}
if (args.Length != 2 || args[0] != "--review-root") throw new ArgumentException("Usage: Home.NativeAssets --review-root <new development cache>");
var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root); AppFiles.RejectLink(root);
using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(15));
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; budget.Cancel(); };
var token = budget.Token;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var definitions = new[]
{
    new SoftwareSource("retroarch", "1.22.2", "GPL-3.0", "https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch.7z", "RetroArch.7z", null, "https://github.com/libretro/RetroArch/tree/v1.22.2"),
    new SoftwareSource("mgba-core", "RetroArch stable 1.22.2 core set", "MPL-2.0 (mGBA; exact build review pending)", "https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch_cores.7z", "RetroArch_cores.7z", null, "https://github.com/mgba-emu/mgba"),
    new SoftwareSource("sunshine", "2026.914.233613", "GPL-3.0", "https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-lite.zip", "sunshine.zip", "233008e46f4c0e501a586cbfd6c4fd4a4c0d414a0b5fc7f13c070eb92ec3824b", "https://github.com/LizardByte/Sunshine/tree/v2026.914.233613"),
    new SoftwareSource("moonlight-web", "2.10.0", "GPL-3.0", "https://github.com/MrCreativ3001/moonlight-web-stream/releases/download/v2.10.0/moonlight-web-x86_64-pc-windows-gnu.zip", "moonlight-web.zip", "1dc3019952c610fbd7deb76dc84e3c4c6f26458ebb44823ea1f02ad883a36da9", "https://github.com/MrCreativ3001/moonlight-web-stream/tree/v2.10.0"),
    new SoftwareSource("mediamtx", "1.21.2", "MIT", "https://github.com/bluenviron/mediamtx/releases/download/v1.21.2/mediamtx_v1.21.2_windows_amd64.zip", "mediamtx.zip", "a84224f5a490855684e62ba6fb71e80ea2a5e16aed61883bb58380290e93e467", "https://github.com/bluenviron/mediamtx/tree/v1.21.2"),
    new SoftwareSource("cmake", "3.31.8", "BSD-3-Clause (development only)", "https://github.com/Kitware/CMake/releases/download/v3.31.8/cmake-3.31.8-windows-x86_64.zip", "cmake.zip", "81aa9964dbabd71fe02e7ec50472fd3ad56138c49944515ece9001efbff8d719", "https://github.com/Kitware/CMake/tree/v3.31.8"),
    new SoftwareSource("mingw", "GCC 16.2.0 / mingw-w64 14", "GPL-3.0 toolchain / runtime exceptions (development only)", "https://github.com/niXman/mingw-builds-binaries/releases/download/16.2.0-rt_v14-rev1/x86_64-16.2.0-release-posix-seh-msvcrt-rt_v14-rev1.7z", "mingw.7z", "a3cfb25037981ea1cd2f4e5452fa45c700b20fffc311e5d78de90dedd142b73a", "https://github.com/niXman/mingw-builds-binaries/releases/tag/16.2.0-rt_v14-rev1")
};
// Observations of these exact official-HTTPS artifacts, NOT publisher signatures/checksum manifests.
var observedPins = new Dictionary<string, string> { ["retroarch"] = "b2139b1d0f9d4526dc6b5ce23cbb3efdc766096fa6f2c3df016818b486ac6372", ["mgba-core"] = "86b871e11b9b4772ac644b40a38f2c8e9449da1f355eae7da08aa061148547b0" };
var allowedHosts = new[] { "buildbot.libretro.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" };
using var client = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
    ConnectTimeout = TimeSpan.FromSeconds(20),
    ConnectCallback = async (context, cancel) =>
    {
        if (!allowedHosts.Contains(context.DnsEndPoint.Host, StringComparer.OrdinalIgnoreCase) || context.DnsEndPoint.Port != 443) throw new HttpRequestException("Unapproved software host.");
        foreach (var address in (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancel)).Where(address => RemoteMediaHttp.Allowed(address, false)))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, 443), cancel); return new NetworkStream(socket, true); } catch { socket.Dispose(); }
        }
        throw new HttpRequestException("No permitted public address for software host.");
    }
}) { Timeout = Timeout.InfiniteTimeSpan };
client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeNativeSoftwareReview/0.34");
var reviews = new List<SoftwareReview>();
foreach (var definition in definitions)
{
    var folder = AppFiles.DirectoryIn(root, definition.Id); var receipt = Path.Combine(folder, "review.json");
    if (File.Exists(receipt))
    {
        AppFiles.RejectLink(receipt);
        var prior = JsonSerializer.Deserialize<SoftwareReview>(File.ReadAllText(receipt), json) ?? throw new InvalidDataException("Invalid prior review.");
        if (prior.Source != definition) throw new InvalidDataException("Different source in prior review; files were preserved.");
        VerifiedFilePublication.Require(Path.Combine(folder, definition.FileName), prior.ArchiveBytes, prior.ObservedArchiveSha256);
        foreach (var file in prior.Files) VerifiedFilePublication.Require(MediaPaths.ResolveFile(folder, "files/" + file.Path), file.Bytes, file.Sha256);
        reviews.Add(prior); continue;
    }
    var archiveFile = Path.Combine(folder, definition.FileName);
    if (File.Exists(archiveFile))
    {
        var approvedHash = definition.ExpectedArchiveSha256 ?? observedPins.GetValueOrDefault(definition.Id);
        if (approvedHash is null || await VerifiedFilePublication.HashAsync(archiveFile, token) != approvedHash) throw new InvalidDataException("An unreviewed archive already exists; it was preserved.");
    }
    Console.WriteLine("Reviewing software only: " + definition.Id);
    if (!File.Exists(archiveFile)) await DownloadAsync(definition, archiveFile);
    var archiveHash = await VerifiedFilePublication.HashAsync(archiveFile, token);
    if (definition.ExpectedArchiveSha256 is { } expected && expected != archiveHash) throw new InvalidDataException("Publisher release digest mismatch: " + definition.Id);
    var destination = AppFiles.DirectoryIn(folder, "files"); var selected = new List<ReviewedFile>();
    using (var archive = ArchiveFactory.OpenArchive(archiveFile, ReaderOptions.ForFilePath))
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var count = 0; long expanded = 0;
        if (archive.Type == ArchiveType.SevenZip || archive.IsSolid)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry()) await ExtractAsync(reader.Entry, reader.OpenEntryStream);
        }
        else foreach (var entry in archive.Entries) await ExtractAsync(entry, entry.OpenEntryStream);
        async Task ExtractAsync(IEntry entry, Func<Stream> open)
        {
            token.ThrowIfCancellationRequested();
            if (++count > 40000 || entry.LinkTarget is not null || entry.Attrib is { } attr && ((attr >> 16 & 0xf000) == 0xa000 || (attr & 0x400) != 0)) throw new InvalidDataException("Unsafe software archive entry.");
            var relative = DownloadFiles.ValidateRelative((entry.Key ?? "").TrimEnd('/', '\\')).Replace('\\', '/');
            if (entry.IsDirectory) return;
            if (!seen.Add(relative) || entry.Size < 0) throw new InvalidDataException("Duplicate/invalid archive entry: " + relative);
            if (!Selected(definition.Id, relative)) return;
            if (entry.Size > 256 * 1024 * 1024) throw new InvalidDataException("Selected software entry exceeds the limit: " + relative);
            expanded += entry.Size; if (expanded > (definition.Id == "mingw" ? 2L * 1024 * 1024 * 1024 : 512L * 1024 * 1024)) throw new InvalidDataException("Software extraction limit exceeded.");
            var parent = Path.GetDirectoryName(relative); if (!string.IsNullOrEmpty(parent)) DownloadFiles.EnsureDirectory(destination, parent);
            var path = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            await using var input = open(); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            var crc = new Crc32(); using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[128 * 1024]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, token)) != 0) { total += read; if (total > entry.Size) throw new InvalidDataException("Entry exceeds declared size."); crc.Append(buffer.AsSpan(0, read)); digest.AppendData(buffer.AsSpan(0, read)); await output.WriteAsync(buffer.AsMemory(0, read), token); }
            if (total != entry.Size || entry.Crc != 0 && unchecked((uint)entry.Crc) != crc.Value) throw new InvalidDataException("Software archive size/CRC mismatch.");
            selected.Add(new(relative, total, Convert.ToHexStringLower(digest.GetHashAndReset())));
        }
    }
    var review = new SoftwareReview(definition, archiveHash, new FileInfo(archiveFile).Length, definition.ExpectedArchiveSha256 is null ? "official-HTTPS-observation-not-publisher-signature" : "github-release-sha256", selected.ToArray(), DateTimeOffset.UtcNow.ToString("O"));
    await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(review, json), token); reviews.Add(review);
    Console.WriteLine($"{definition.Id}: {selected.Count} software files, archive SHA-256 {archiveHash}");
}
Console.WriteLine(JsonSerializer.Serialize(new { reviews = reviews.Select(review => new { id = review.Source.Id, review.ObservedArchiveSha256, review.ArchiveBytes, review.IntegrityKind, files = review.Files.Length }), installedDriver = false, launchedEmulator = false, downloadedRomOrBios = false }, json));

async Task DownloadAsync(SoftwareSource definition, string target)
{
    var uri = new Uri(definition.Url);
    for (var redirects = 0; redirects < 6; redirects++)
    {
        if (uri.Scheme != "https" || uri.UserInfo.Length != 0 || !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Unapproved software redirect.");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308) { uri = new Uri(uri, response.Headers.Location ?? throw new InvalidDataException("Missing software redirect.")); continue; }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 300 * 1024 * 1024) throw new InvalidDataException("Software archive too large.");
        var pending = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[128 * 1024]; long total = 0; int bytes;
            while ((bytes = await input.ReadAsync(buffer, token)) != 0) { total += bytes; if (total > 300 * 1024 * 1024) throw new InvalidDataException("Software archive exceeds the body limit."); await output.WriteAsync(buffer.AsMemory(0, bytes), token); }
            if (response.Content.Headers.ContentLength is { } expected && expected != total) throw new InvalidDataException("Software download incomplete.");
            await output.FlushAsync(token); output.Flush(true);
        }
        File.Move(pending, target, false); return;
    }
    throw new InvalidDataException("Too many software redirects.");
}
static bool Selected(string id, string path)
{
    var name = Path.GetFileName(path); var parts = path.Split('/');
    if (id == "mgba-core") return name == "mgba_libretro.dll";
    if (id == "retroarch") return name is "retroarch.exe" or "retroarch.cfg" or "COPYING" or "COPYING.md" or "LICENSE" or "LICENSE.txt" || name == "mgba_libretro.info" || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && parts.Length <= 2;
    return true; // Reviewed software ZIPs only; nothing is run or added to a Home runtime here.
}
record SoftwareSource(string Id, string Version, string License, string Url, string FileName, string? ExpectedArchiveSha256, string SourceUrl);
record ReviewedFile(string Path, long Bytes, string Sha256);
record SoftwareReview(SoftwareSource Source, string ObservedArchiveSha256, long ArchiveBytes, string IntegrityKind, ReviewedFile[] Files, string ReviewedAt);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FfmpegVersion();
