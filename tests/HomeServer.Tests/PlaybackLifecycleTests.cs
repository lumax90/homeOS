using HomeServer.Core;
using HomeServer.Playback;

namespace HomeServer.Tests;

public sealed class PlaybackLifecycleTests
{
    private static PlaybackLifecycle Starting() => new(new PlaybackSession("fixture", "media", "video-transcode", "", 0, 4, [], null, "starting"));
    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("closed")]
    public void LatePlaylistReadyCannotReopenATerminalEncoderState(string terminal)
    {
        var state = Starting();
        if (terminal == "completed") state.Complete(); else if (terminal == "failed") state.Fail("budget failure"); else state.Close();
        state.Ready(); Assert.Equal(terminal, state.Current.State);
        if (terminal == "failed") { state.Complete(); state.Close(); Assert.Equal("budget failure", state.Current.Error); }
    }
    [Fact]
    public async Task ConcurrentPlaylistAndEncoderCompletionAlwaysRetainCompletion()
    {
        var states = Enumerable.Range(0, 64).Select(_ => Starting()).ToArray();
        await Task.WhenAll(states.SelectMany(state => new[] { Task.Run(state.Ready), Task.Run(state.Complete) }));
        Assert.All(states, state => Assert.Equal("completed", state.Current.State));
    }
}
