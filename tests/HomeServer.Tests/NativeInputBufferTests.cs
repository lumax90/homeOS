using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Tests;
public sealed class NativeInputBufferTests
{
    [Fact]
    public void ShortPressReleaseCannotDisappearBetweenRelayFrames()
    {
        var input=new NativeInputBuffer();input.Stage(1<<8);input.Stage(0);
        Assert.Equal(1<<8,input.Next(false));Assert.Equal(0,input.Next(false));Assert.Equal(0,input.Next(false));
    }
    [Fact]
    public void HeldDuplicatesDoNotQueueAndPauseClearsAllStaleEdges()
    {
        var input=new NativeInputBuffer();input.Stage(1<<8);for(var i=0;i<100;i++)input.Stage(1<<8);
        Assert.Equal(1<<8,input.Next(false));input.Stage(0);Assert.Equal(0,input.Next(false));
        input.Stage(1<<8);Assert.Equal(0,input.Next(true));Assert.Equal(0,input.Next(false));
    }
    [Fact]
    public void QueueOverflowAndUnsupportedButtonsFailClosed()
    {
        var input=new NativeInputBuffer();for(var i=0;i<32;i++)input.Stage(i%2==0?(ushort)(1<<8):(ushort)0);
        Assert.Equal(429,Assert.Throws<DomainException>(()=>input.Stage(1<<8)).StatusCode);Assert.Equal(0,input.Next(false));
        Assert.Throws<DomainException>(()=>input.Stage(ushort.MaxValue));
    }
    [Fact]
    public void BackgroundPolicyResultIsNotAcceptedWhenDeniedStaleOrFromAFutureClock()
    {
        var now=DateTime.UtcNow.Ticks;
        Assert.True(NativeLocalInputGuard.IsRecentSafeResult(true,now,now));
        Assert.False(NativeLocalInputGuard.IsRecentSafeResult(false,now,now));
        Assert.False(NativeLocalInputGuard.IsRecentSafeResult(true,now-TimeSpan.FromSeconds(5).Ticks,now));
        Assert.False(NativeLocalInputGuard.IsRecentSafeResult(true,now+1,now));
    }
}
