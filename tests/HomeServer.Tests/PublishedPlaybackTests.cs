using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Tests;

public sealed class PublishedPlaybackTests
{
    [PublishedServerFact]
    public async Task RealKestrelRestartAndProcessCrashRetainLimitsButRevokeOldStreamLeases()
    {
        using var home = new TestWorkspace(); var payload = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        home.Write("Isolated-range.mp4", payload); var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var executable = Environment.GetEnvironmentVariable("HOME_TEST_SERVER")!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "--urls", $"http://127.0.0.1:{port}", "--Home:DataPath", home.Paths.Data, "--Home:Downloads:LoopbackOnly", "true", "--Home:FfmpegPath", "absent", "--Home:FfprobePath", "absent" }) start.ArgumentList.Add(argument);
        var processes = new List<Process>(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        async Task Healthy()
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try { using var result = await client.GetAsync("/api/health", timeout.Token); if (result.IsSuccessStatusCode) return; } catch (HttpRequestException) { }
                await Task.Delay(100, timeout.Token);
            }
        }
        async Task Csrf()
        {
            var state = await client.GetFromJsonAsync<JsonElement>("/api/session", timeout.Token); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", state.GetProperty("csrfToken").GetString());
        }
        try
        {
            var original = Process.Start(start)!; processes.Add(original); await Healthy(); await Csrf();
            (await client.PostAsJsonAsync("/api/setup", new Credentials("Isolated playback", "local-playback-runtime-2026"), timeout.Token)).EnsureSuccessStatusCode(); await Csrf();
            (await client.PutAsJsonAsync("/api/users/1/playback-limits", new UserPlaybackLimits(1, 1), timeout.Token)).EnsureSuccessStatusCode();
            var input = new PlaybackStart(media.Id, new(["mp4"], [], [], false), ClientId: Guid.NewGuid().ToString("N"), ClientRevision: 1);
            async Task<PlaybackSession> Play(long revision)
            { var result = await client.PostAsJsonAsync("/api/playback", input with { ClientRevision = revision }, timeout.Token); result.EnsureSuccessStatusCode(); return (await result.Content.ReadFromJsonAsync<PlaybackSession>(timeout.Token))!; }
            var first = await Play(1);
            using (var range = new HttpRequestMessage(HttpMethod.Get, first.Url))
            {
                range.Headers.Range = new RangeHeaderValue(20, 39); using var response = await client.SendAsync(range, timeout.Token);
                Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(payload[20..40], await response.Content.ReadAsByteArrayAsync(timeout.Token));
            }
            (await client.PostAsync("/api/operations/restart", null, timeout.Token)).EnsureSuccessStatusCode(); await original.WaitForExitAsync(timeout.Token); await Healthy();
            var runtime = await client.GetFromJsonAsync<JsonElement>("/api/operations/runtime", timeout.Token); var replacement = Process.GetProcessById(runtime.GetProperty("processId").GetInt32()); processes.Add(replacement);
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(first.Url, timeout.Token)).StatusCode);
            var usage = (await client.GetFromJsonAsync<PlaybackUsage>("/api/account/playback", timeout.Token))!;
            Assert.Empty(usage.Active); Assert.Equal(1, usage.User.MaximumStreams); Assert.Equal("server-stopped", Assert.Single(usage.History).EndReason);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/playback", input, timeout.Token)).StatusCode);
            var second = await Play(2); replacement.Kill(true); await replacement.WaitForExitAsync(timeout.Token);
            processes.Add(Process.Start(start)!); await Healthy();
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(second.Url, timeout.Token)).StatusCode);
            usage = (await client.GetFromJsonAsync<PlaybackUsage>("/api/account/playback", timeout.Token))!;
            Assert.Empty(usage.Active); Assert.Equal(1, usage.User.MaximumStreams); Assert.Contains(usage.History, item => item.Id == second.Id && item.EndReason == "server-restarted");
            var third = await Play(3); Assert.NotEqual(second.Id, third.Id);
            (await client.DeleteAsync($"/api/playback/{third.Id}", timeout.Token)).EnsureSuccessStatusCode();
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(home.Source, "Isolated-range.mp4"), timeout.Token));
        }
        finally
        {
            var pidPath = Path.Combine(home.Paths.Data, "home.pid");
            if (File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath), out var pid) && processes.All(process => process.Id != pid))
            {
                try { var running = Process.GetProcessById(pid); if (running.MainModule?.FileName == executable) processes.Add(running); else running.Dispose(); } catch (ArgumentException) { }
            }
            foreach (var process in processes)
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }
}
