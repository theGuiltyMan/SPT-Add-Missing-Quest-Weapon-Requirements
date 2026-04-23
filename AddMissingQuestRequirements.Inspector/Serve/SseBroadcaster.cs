using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace AddMissingQuestRequirements.Inspector.Serve;

/// <summary>
/// Fan-out for SSE subscribers. Each subscriber owns a bounded channel;
/// the broadcaster writes the same event payload to every channel.
/// TryWrite is non-blocking so a slow subscriber cannot stall the publisher.
/// </summary>
public sealed class SseBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(capacity: 32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void Publish(string eventName, string jsonPayload)
    {
        var sb = new StringBuilder();
        sb.Append("event: ").Append(eventName).Append('\n');
        sb.Append("data: ").Append(jsonPayload).Append("\n\n");
        var frame = sb.ToString();
        foreach (var kv in _subscribers)
        {
            kv.Value.Writer.TryWrite(frame);
        }
    }
}
