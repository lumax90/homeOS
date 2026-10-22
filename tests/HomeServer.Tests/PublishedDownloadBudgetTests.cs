using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HomeServer.Core;
using MonoTorrent;

namespace HomeServer.Tests;

public sealed class PublishedDownloadBudgetTests
{
    [PublishedServerFact]
    public async Task RealProcessCrashKeepsBudgetDebitAndQuotaWaitWithoutStartingInternetTransfers()
    {
        using var home = new TestWorkspace(); var file = home.Write("Isolated-budget.mkv", new byte[4096]);
        var torrent = (await new TorrentCreator { PieceLength = 16 * 1024 }.CreateAsync(new TorrentFileSource(file))).Encode();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var executable = Environment.GetEnvironmentVariable("HOME_TEST_SERVER")!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "--urls", $"http://127.0.0.1:{port}", "--Home:DataPath", home.Paths.Data, "--Home:Downloads:LoopbackOnly", "true", "--Home:FfmpegPath", "absent", "--Home:FfprobePath", "absent" }) start.ArgumentList.Add(argument);
        var processes = new List<Process>(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var handler = new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }; using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        async Task Healthy()
        {
            while (true) { timeout.Token.ThrowIfCancellationRequested(); try { using var result = await client.GetAsync("/api/health", timeout.Token); if (result.IsSuccessStatusCode) return; } catch (HttpRequestException) { } await Task.Delay(100, timeout.Token); }
        }
        async Task Session()
        {
            var state = await client.GetFromJsonAsync<JsonElement>("/api/session", timeout.Token);
            foreach (var (header, field) in new[] { ("X-CSRF-TOKEN", "csrfToken"), ("X-Home-User", "userId"), ("X-Home-Session", "sessionId") }) { client.DefaultRequestHeaders.Remove(header); if (state.GetProperty(field).GetString() is { Length: > 0 } value) client.DefaultRequestHeaders.Add(header, value); }
        }
        async Task<DownloadRecord> WaitDownload(string id, Func<DownloadRecord, bool> predicate)
        {
            while (true) { timeout.Token.ThrowIfCancellationRequested(); var row = (await client.GetFromJsonAsync<DownloadRecord>($"/api/downloads/{id}", timeout.Token))!; if (predicate(row)) return row; await Task.Delay(100, timeout.Token); }
        }
        async Task<DownloadRecord> Add()
        {
            using var content = new ByteArrayContent(torrent); content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            var result = await client.PostAsync("/api/downloads/torrent", content, timeout.Token); result.EnsureSuccessStatusCode(); return (await result.Content.ReadFromJsonAsync<DownloadRecord>(timeout.Token))!;
        }
        try
        {
            var original = Process.Start(start)!; processes.Add(original); await Healthy(); await Session();
            (await client.PostAsJsonAsync("/api/setup", new Credentials("Budget fixture", "isolated-budget-runtime-2026"), timeout.Token)).EnsureSuccessStatusCode(); await Session();
            (await client.PutAsJsonAsync("/api/users/1/download-budget", new DownloadBudgetPolicy(MonthlyAcquisitions: 1, MonthlyPayloadBytes: 1), timeout.Token)).EnsureSuccessStatusCode();
            var added = await Add(); var held = await WaitDownload(added.Id, row => row.Status == "budget-waiting"); Assert.Equal("monthly-payload", held.Ownership!.WaitReason);
            Assert.Equal(0, held.Ownership.ChargedBytes); Assert.Equal(0, held.ReceivedBytes);
            (await client.PutAsJsonAsync("/api/users/1/download-budget", new DownloadBudgetPolicy(MonthlyAcquisitions: 1, MonthlyPayloadBytes: 4096, Revision: 1), timeout.Token)).EnsureSuccessStatusCode();
            await WaitDownload(added.Id, row => row.Ownership!.ChargedBytes == 4096);
            original.Kill(true); await original.WaitForExitAsync(timeout.Token); processes.Add(Process.Start(start)!); await Healthy(); await Session();
            var usage = (await client.GetFromJsonAsync<DownloadBudgetUsage>("/api/account/download-usage", timeout.Token))!; Assert.Equal(1, usage.Acquisitions); Assert.Equal(4096, usage.CommittedBytes);
            Assert.Equal(added.Id, (await Add()).Id);
            (await client.PutAsJsonAsync("/api/users/1/download-budget", new DownloadBudgetPolicy(MonthlyAcquisitions: 1, MonthlyPayloadBytes: 4096, MaximumConcurrent: 0, Revision: 2), timeout.Token)).EnsureSuccessStatusCode();
            var stopped = await WaitDownload(added.Id, row => row.Status == "budget-waiting"); Assert.Equal(0, stopped.ReceivedBytes); Assert.Equal(4096, stopped.Ownership!.ChargedBytes);
            var history = (await client.GetFromJsonAsync<DownloadBudgetHistory>("/api/account/download-history", timeout.Token))!;
            Assert.Single(history.Items, item => item.Action == "payload"); Assert.Single(history.Items, item => item.Action == "admission"); Assert.Equal(new byte[4096], await File.ReadAllBytesAsync(file, timeout.Token));
        }
        finally
        {
            foreach (var process in processes) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch (InvalidOperationException) { } process.Dispose(); }
        }
    }
}
