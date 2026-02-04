namespace MiHotKeyApp.Input.Wmi;

internal sealed class RepeatGate
{
    private readonly object _gate = new();
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _debounceMs;

    public RepeatGate(int debounceMs)
    {
        _debounceMs = Math.Max(0, debounceMs);
    }

    public bool ShouldAccept(string mappedEvent)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_debounceMs > 0 && _lastSeen.TryGetValue(mappedEvent, out var prev) && (now - prev).TotalMilliseconds < _debounceMs)
            {
                return false;
            }
            _lastSeen[mappedEvent] = now;

            var dot = mappedEvent.LastIndexOf('.');
            if (dot <= 0 || dot == mappedEvent.Length - 1)
            {
                return true;
            }

            var name = mappedEvent[..dot];
            var kind = mappedEvent[(dot + 1)..];

            if (kind.Equals("down", StringComparison.OrdinalIgnoreCase))
            {
                if (_active.Contains(name))
                {
                    return false;
                }

                _active.Add(name);
                return true;
            }

            if (kind.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                _active.Remove(name);
                return true;
            }

            return true;
        }
    }
}

