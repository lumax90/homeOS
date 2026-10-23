namespace HomeServer.Operations;

/// <summary>Atomically closes API admission between an idle check and a container stop.</summary>
public sealed class ContainerRequestGate(TimeProvider clock)
{
    private readonly object _gate = new();
    private int _requests;
    private string? _owner;
    private DateTimeOffset _until;

    public IDisposable? Enter()
    {
        lock (_gate)
        {
            if (_owner is not null && _until > clock.GetUtcNow()) return null;
            _requests++;
            return new Request(this);
        }
    }
    public bool Freeze(string owner, TimeSpan duration)
    {
        lock (_gate)
        {
            if (_requests != 0 || _owner is not null && _until > clock.GetUtcNow()) return false;
            _owner = owner; _until = clock.GetUtcNow().Add(duration); return true;
        }
    }
    public void Release(string owner)
    {
        lock (_gate) { if (_owner == owner) { _owner = null; _until = default; } }
    }
    private sealed class Request(ContainerRequestGate parent) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (parent._gate) parent._requests--;
        }
    }
}

public static class ContainerUpdateMiddleware
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next, ContainerRequestGate gate, bool supported)
    {
        if (!supported || !context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/api/health")
        { await next(context); return; }
        using var request = gate.Enter();
        if (request is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "10";
            await context.Response.WriteAsJsonAsync(new { code = "container-updating", message = "Home güncellemeye hazırlanıyor. Biraz sonra yeniden bağlan." });
            return;
        }
        // Open event channels are not user activity. Media streams keep their admission until closed.
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == StatusCodes.Status101SwitchingProtocols
                || context.Response.ContentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true) request.Dispose();
            return Task.CompletedTask;
        });
        await next(context);
    }
}
