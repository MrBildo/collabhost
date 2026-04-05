using System.Threading.Channels;

namespace Collabhost.Api.Shared;

public class RingBuffer<T>(int capacity = 1000)
{
    private readonly T[] _buffer = new T[capacity];
    private readonly Lock _lock = new();
    private readonly Lock _subscriberLock = new();
    private readonly List<Channel<(long Id, T Item)>> _subscribers = [];
    private int _head;
    private int _count;
    private long _sequenceId;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public int Capacity => capacity;

    public void Add(T item)
    {
        long id;

        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % capacity;

            if (_count < capacity)
            {
                _count++;
            }

            id = ++_sequenceId;
        }

        // Notify subscribers outside the main lock to avoid deadlocks
        lock (_subscriberLock)
        {
            foreach (var channel in _subscribers)
            {
                if (!channel.Writer.TryWrite((id, item)))
                {
                    // Channel is full -- drop oldest and retry
                    channel.Reader.TryRead(out _);
                    channel.Writer.TryWrite((id, item));
                }
            }
        }
    }

    public IReadOnlyList<T> GetAll()
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                return [];
            }

            var result = new T[_count];
            var start = _count < capacity ? 0 : _head;

            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % capacity];
            }

            return result;
        }
    }

    public IReadOnlyList<T> GetLast(int count)
    {
        lock (_lock)
        {
            var actual = Math.Min(count, _count);

            if (actual == 0)
            {
                return [];
            }

            var result = new T[actual];
            var start = _count < capacity
                ? _count - actual
                : (_head - actual + capacity) % capacity;

            for (var i = 0; i < actual; i++)
            {
                result[i] = _buffer[(start + i) % capacity];
            }

            return result;
        }
    }

    public IReadOnlyList<(long Id, T Item)> GetLastWithIds(int count)
    {
        lock (_lock)
        {
            var actual = Math.Min(count, _count);

            if (actual == 0)
            {
                return [];
            }

            var result = new (long Id, T Item)[actual];
            var start = _count < capacity
                ? _count - actual
                : (_head - actual + capacity) % capacity;

            var firstId = _sequenceId - actual + 1;

            for (var i = 0; i < actual; i++)
            {
                result[i] = (firstId + i, _buffer[(start + i) % capacity]);
            }

            return result;
        }
    }

    public ChannelReader<(long Id, T Item)> Subscribe()
    {
        var channel = Channel.CreateBounded<(long, T)>
        (
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            }
        );

        lock (_subscriberLock)
        {
            _subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<(long Id, T Item)> reader)
    {
        lock (_subscriberLock)
        {
            var index = _subscribers.FindIndex(ch => ch.Reader == reader);

            if (index >= 0)
            {
                _subscribers[index].Writer.Complete();
                _subscribers.RemoveAt(index);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
