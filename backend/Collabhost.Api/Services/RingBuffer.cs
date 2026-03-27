namespace Collabhost.Api.Services;

public class RingBuffer<T>(int capacity = 1000)
{
    private readonly T[] _buffer = new T[capacity];
    private readonly object _lock = new();
    private int _head;
    private int _count;

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
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % capacity;
            if (_count < capacity)
            {
                _count++;
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

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
