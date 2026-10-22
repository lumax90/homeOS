using System.Collections.Concurrent;
using System.Threading.Channels;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed class EventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<HomeEvent>> _subscribers = new();

    public (Guid Id, ChannelReader<HomeEvent> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<HomeEvent>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(string type)
    {
        var message = new HomeEvent(type, DateTimeOffset.UtcNow.ToString("O"));
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(message);
    }
}
