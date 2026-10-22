using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;

namespace HomeServer.Tests;

public sealed class PublishedWatchRoomTests
{
    [PublishedServerFact]
    public async Task RealRestartAndProcessCrashPauseDurableRoomsAndFencePreviousTransportEpochs()
    {
        using var home = new TestWorkspace(); var source = home.Write("Isolated-room-protocol.mp4", Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        var library = home.AddLibrary(); await home.Scan(library.Id); var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 10).Items);
        // An isolated protocol fixture supplies duration; native decode/burn-in is exercised separately.
        home.Store.SaveAnalysis(media.Id, MediaAnalysisService.Fingerprint(new FileInfo(source)), new(120, null, null, null, null));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var executable = Environment.GetEnvironmentVariable("HOME_TEST_SERVER")!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "--urls", $"http://127.0.0.1:{port}", "--Home:DataPath", home.Paths.Data, "--Home:Downloads:LoopbackOnly", "true", "--Home:FfmpegPath", "absent", "--Home:FfprobePath", "absent" }) start.ArgumentList.Add(argument);
        var processes = new List<Process>(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        using var handler = new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        WatchIdentity identity = new("", "");
        async Task Healthy()
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try { using var result = await client.GetAsync("/api/health", timeout.Token); if (result.IsSuccessStatusCode) return; } catch (HttpRequestException) { }
                await Task.Delay(100, timeout.Token);
            }
        }
        async Task Session()
        {
            var session = await client.GetFromJsonAsync<JsonElement>("/api/session", timeout.Token);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
            identity = new(session.GetProperty("userId").GetString() ?? "", session.GetProperty("sessionId").GetString() ?? "");
        }
        try
        {
            var original = Process.Start(start)!; processes.Add(original); await Healthy(); await Session();
            (await client.PostAsJsonAsync("/api/setup", new Credentials("Room protocol fixture", "isolated-room-password-2026"), timeout.Token)).EnsureSuccessStatusCode(); await Session();
            var clientId = Guid.NewGuid().ToString("N");
            async Task<PlaybackSession> Play(long revision, WatchPlaybackBinding? watch = null)
            {
                var response = await client.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, new(["mp4"], [], [], false), StartSeconds: 42, ClientId: clientId, ClientRevision: revision, Watch: watch), timeout.Token);
                response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<PlaybackSession>(timeout.Token))!;
            }
            var first = await Play(1);
            var response = await client.PostAsJsonAsync("/api/watch/rooms", new WatchRoomCreate(clientId, 1, first.Id!, "together", 42, Identity: identity), timeout.Token); response.EnsureSuccessStatusCode();
            var joined = (await response.Content.ReadFromJsonAsync<WatchRoomConnection>(timeout.Token))!;
            var oldBinding = new WatchPlaybackBinding(joined.Room.Id, joined.MemberId, 1, joined.Room.Epoch);
            (await client.PostAsync("/api/operations/restart", null, timeout.Token)).EnsureSuccessStatusCode(); await original.WaitForExitAsync(timeout.Token); await Healthy();
            var runtime = await client.GetFromJsonAsync<JsonElement>("/api/operations/runtime", timeout.Token); var replacement = Process.GetProcessById(runtime.GetProperty("processId").GetInt32()); processes.Add(replacement);
            await Session();
            var recovered = (await client.GetFromJsonAsync<WatchRoomSnapshot>($"/api/watch/rooms/{joined.Room.Id}?member={joined.MemberId}", timeout.Token))!;
            Assert.Equal("paused", recovered.State); Assert.Equal(42, recovered.PositionSeconds); Assert.True(recovered.Epoch > joined.Room.Epoch); Assert.Single((await client.GetFromJsonAsync<WatchRoomSummary[]>("/api/watch/rooms", timeout.Token))!);
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(first.Url, timeout.Token)).StatusCode);
            var stale = await client.PostAsJsonAsync("/api/playback", new PlaybackStart(media.Id, new(["mp4"], [], [], false), ClientId: clientId, ClientRevision: 2, Watch: oldBinding), timeout.Token); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var second = await Play(3, oldBinding with { Epoch = recovered.Epoch });
            var pulse = await client.PostAsJsonAsync($"/api/watch/rooms/{joined.Room.Id}/members/{joined.MemberId}/heartbeat", new WatchRoomHeartbeat(1, 1, recovered.Epoch, recovered.SyncRevision, second.Id, 42, true, false, Identity: identity), timeout.Token); pulse.EnsureSuccessStatusCode();
            replacement.Kill(true); await replacement.WaitForExitAsync(timeout.Token); processes.Add(Process.Start(start)!); await Healthy(); await Session();
            var afterCrash = (await client.GetFromJsonAsync<WatchRoomSnapshot>($"/api/watch/rooms/{joined.Room.Id}?member={joined.MemberId}", timeout.Token))!;
            Assert.Equal("paused", afterCrash.State); Assert.Equal(42, afterCrash.PositionSeconds); Assert.True(afterCrash.Epoch > recovered.Epoch);
            Assert.Empty((await client.GetFromJsonAsync<PlaybackUsage>("/api/account/playback", timeout.Token))!.Active);
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync(second.Url, timeout.Token)).StatusCode);
            var third = await Play(4, oldBinding with { Epoch = afterCrash.Epoch }); Assert.NotEqual(second.Id, third.Id);
            (await client.PostAsJsonAsync($"/api/watch/clients/{clientId}/leave", new WatchRoomLeave(2, identity), timeout.Token)).EnsureSuccessStatusCode();
            Assert.Empty((await client.GetFromJsonAsync<PlaybackUsage>("/api/account/playback", timeout.Token))!.Active);
            Assert.Equal(Enumerable.Range(0, 256).Select(value => (byte)value), await File.ReadAllBytesAsync(source, timeout.Token));
        }
        finally
        {
            var pidPath = Path.Combine(home.Paths.Data, "home.pid");
            if (File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath), out var pid) && processes.All(process => process.Id != pid))
            { try { var running = Process.GetProcessById(pid); if (running.MainModule?.FileName == executable) processes.Add(running); else running.Dispose(); } catch (ArgumentException) { } }
            foreach (var process in processes) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch (InvalidOperationException) { } process.Dispose(); }
        }
    }
}
