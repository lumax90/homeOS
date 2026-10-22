using HomeServer.Core;

namespace HomeServer.Playback;

public sealed class PlaybackLifecycle(PlaybackSession initial)
{
    private PlaybackSession _current = initial;
    public PlaybackSession Current => Volatile.Read(ref _current);
    public void Ready() => Move("ready", null, state => state == "starting");
    public void Complete() => Move("completed", null, state => state is "starting" or "ready");
    public void Fail(string error) => Move("failed", error, state => state is not ("failed" or "closed"));
    public void Close() => Move("closed", null, state => state != "failed");
    private void Move(string next, string? error, Func<string, bool> allowed)
    {
        while (true)
        {
            var current = Current; if (!allowed(current.State)) return;
            var value = current with { State = next, Error = error };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, value, current), current)) return;
        }
    }
}
