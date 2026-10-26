using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Operations;

/// <summary>Docker lifecycle hooks use a private Unix socket, never the public HTTP API or a stored credential.</summary>
public sealed class ContainerUpdateControl(ContainerUpdateCoordinator updates, IHostApplicationLifetime lifetime,
    ILogger<ContainerUpdateControl> logger) : BackgroundService
{
    public const string SocketDirectory = "/tmp/home-container-update";
    public const string SocketPath = SocketDirectory + "/control.sock";
    private const int MaximumMessage = 8192;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Command(int Protocol, string Action, string? Digest = null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsLinux() || !updates.Supported) return;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try { await started.Task.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        await updates.CompleteStartupAsync(stoppingToken);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var workerToken = run.Token;
        var bound = false;
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (Directory.Exists(SocketDirectory))
            {
                AppFiles.RejectLink(SocketDirectory);
                if (File.GetUnixFileMode(SocketDirectory) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
                    throw new IOException("Private control directory has unexpected permissions.");
            }
            else Directory.CreateDirectory(SocketDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (File.Exists(SocketPath)) throw new IOException("Private control socket already exists.");
            listener.Bind(new UnixDomainSocketEndPoint(SocketPath)); bound = true;
            File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(2);
            var accepting = AcceptAsync(); var expiring = ExpireAsync();
            await Task.WhenAny(accepting, expiring);
            run.Cancel();
            await Task.WhenAll(accepting, expiring);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        { updates.ControlUnavailable(); logger.LogWarning("Automatic update control is unavailable: {ErrorType}", ex.GetType().Name); }
        finally
        {
            try { if (bound && File.Exists(SocketPath)) File.Delete(SocketPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning("Automatic update socket cleanup deferred: {ErrorType}", ex.GetType().Name); }
        }

        async Task AcceptAsync()
        {
            while (!workerToken.IsCancellationRequested)
            {
                using var socket = await listener.AcceptAsync(workerToken);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(workerToken);
                request.CancelAfter(TimeSpan.FromMinutes(3));
                try
                {
                    using var read = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
                    read.CancelAfter(TimeSpan.FromSeconds(5));
                    var command = JsonSerializer.Deserialize<Command>(await ReceiveAsync(socket, read.Token), Json);
                    object response;
                    if (command?.Protocol != 1) response = new ContainerUpdateReply(1, 75, "invalid", "Unsupported update protocol.");
                    else if (command.Action == "prepare") response = await updates.PrepareAsync(request.Token);
                    else if (command.Action == "cancel") response = await updates.CancelPreparationAsync(request.Token);
                    else if (command.Action == "complete") response = new ContainerUpdateReply(1, 0, "ready", "Home startup completed.");
                    else if (command.Action is "status" or "downloading" or "ready" or "failed")
                        response = await updates.AgentAsync(new(command.Protocol, command.Action, command.Digest), request.Token);
                    else response = new ContainerUpdateReply(1, 75, "invalid", "Unsupported update command.");
                    await SendAsync(socket, JsonSerializer.Serialize(response, Json), request.Token);
                }
                catch (DomainException)
                { await SendAsync(socket, "{\"protocol\":1,\"exitCode\":75,\"state\":\"deferred\"}", request.Token); }
                catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException)
                { logger.LogDebug("Automatic update control request ended: {ErrorType}", ex.GetType().Name); }
            }
        }
        async Task ExpireAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(workerToken)) await updates.ExpireAsync(workerToken);
        }
    }
    public static async Task<int> RunCommandAsync(string action)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
        { Console.WriteLine("Container automatic-update control is unavailable."); return 75; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (!timeout.IsCancellationRequested)
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
                await SendAsync(socket, JsonSerializer.Serialize(new Command(1, action), Json), timeout.Token);
                var reply = JsonSerializer.Deserialize<ContainerUpdateReply>(await ReceiveAsync(socket, timeout.Token), Json);
                if (reply is null || reply.Protocol != 1 || reply.ExitCode is not (0 or 75)) return 75;
                Console.WriteLine(reply.Message); return reply.ExitCode;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException)
            {
                if (action != "complete" || timeout.IsCancellationRequested) break;
                try { await Task.Delay(1000, timeout.Token); } catch (OperationCanceledException) { break; }
            }
        }
        Console.WriteLine("Home update control did not confirm readiness; this update is deferred."); return 75;
    }
    public static async Task<int> RunAgentCommandAsync(string payload)
    {
        if (!OperatingSystem.IsLinux() || payload.Length > 10000) return 75;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var command = JsonSerializer.Deserialize<Command>(text, Json);
            if (command?.Protocol != 1 || command.Action is not ("status" or "downloading" or "ready" or "failed")) return 75;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
            await SendAsync(socket, JsonSerializer.Serialize(command, Json), timeout.Token);
            var reply = await ReceiveAsync(socket, timeout.Token);
            Console.WriteLine(reply);
            using var value = JsonDocument.Parse(reply);
            return value.RootElement.TryGetProperty("exitCode", out var code) ? code.GetInt32() : 0;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or SocketException or IOException or OperationCanceledException) { return 75; }
    }
    private static async Task<string> ReceiveAsync(Socket socket, CancellationToken token)
    {
        var buffer = new byte[MaximumMessage]; var length = 0;
        while (length < buffer.Length)
        {
            var count = await socket.ReceiveAsync(buffer.AsMemory(length), SocketFlags.None, token);
            if (count == 0) throw new IOException("Incomplete control message.");
            length += count;
            var end = Array.IndexOf(buffer, (byte)'\n', 0, length);
            if (end >= 0) return Encoding.UTF8.GetString(buffer, 0, end);
        }
        throw new IOException("Control message exceeds its bound.");
    }
    private static async Task SendAsync(Socket socket, string message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        if (bytes.Length > MaximumMessage) throw new IOException("Control message exceeds its bound.");
        var sent = 0;
        while (sent < bytes.Length)
        {
            var count = await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, token);
            if (count == 0) throw new IOException("Control connection closed.");
            sent += count;
        }
    }
}
