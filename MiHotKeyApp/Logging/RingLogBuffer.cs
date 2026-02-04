namespace MiHotKeyApp.Logging;

using Microsoft.Extensions.Logging;

internal sealed class RingLogBuffer
{
    private readonly object _gate = new();
    private LogEntry[] _entries;
    private int _next;
    private int _count;
    private long _seq;

    public RingLogBuffer(int capacity)
    {
        _entries = new LogEntry[Math.Max(10, capacity)];
    }

    public event Action? Updated;

    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _entries.Length;
            }
        }
    }

    public void Resize(int newCapacity)
    {
        newCapacity = Math.Max(10, newCapacity);
        lock (_gate)
        {
            if (newCapacity == _entries.Length)
            {
                return;
            }

            var snapshot = SnapshotUnsafe();
            _entries = new LogEntry[newCapacity];
            _next = 0;
            _count = 0;
            foreach (var entry in snapshot.TakeLast(newCapacity))
            {
                _entries[_next] = entry;
                _next = (_next + 1) % _entries.Length;
                _count = Math.Min(_count + 1, _entries.Length);
            }
        }

        Updated?.Invoke();
    }

    public void Append(LogLevel level, string line)
    {
        lock (_gate)
        {
            _seq++;
            _entries[_next] = new LogEntry(_seq, level, line);
            _next = (_next + 1) % _entries.Length;
            _count = Math.Min(_count + 1, _entries.Length);
        }

        Updated?.Invoke();
    }

    public LogEntry[] Snapshot(LogLevel minLevel = LogLevel.Trace)
    {
        lock (_gate)
        {
            return SnapshotUnsafe()
                .Where(e => e.Level >= minLevel)
                .ToArray();
        }
    }

    private LogEntry[] SnapshotUnsafe()
    {
        if (_count == 0)
        {
            return [];
        }

        var result = new LogEntry[_count];
        var start = _next - _count;
        if (start < 0)
        {
            start += _entries.Length;
        }

        for (var i = 0; i < _count; i++)
        {
            result[i] = _entries[(start + i) % _entries.Length];
        }

        return result;
    }
}

internal readonly record struct LogEntry(long Seq, LogLevel Level, string Line);

