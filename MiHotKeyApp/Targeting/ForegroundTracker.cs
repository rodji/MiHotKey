namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Native;

internal sealed class ForegroundTracker : IDisposable
{
    private readonly object _gate = new();
    private nint[] _history;
    private int _next;
    private int _count;

    private readonly User32.WinEventDelegate _cb;
    private nint _hook;

    public ForegroundTracker(int capacity = 10)
    {
        _history = new nint[Math.Max(2, capacity)];
        _cb = OnWinEvent;
    }

    public bool IsEnabled => _hook != 0;

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        _hook = User32.SetWinEventHook(
            User32.EVENT_SYSTEM_FOREGROUND,
            User32.EVENT_SYSTEM_FOREGROUND,
            0,
            _cb,
            0,
            0,
            User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (_hook == 0)
        {
            return;
        }

        User32.UnhookWinEvent(_hook);
        _hook = 0;
        ClearHistory();
    }

    public void Configure(bool enabled, int capacity)
    {
        lock (_gate)
        {
            _history = new nint[Math.Max(2, capacity)];
            _next = 0;
            _count = 0;
        }

        if (enabled && capacity > 0)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public (nint Foreground, nint Previous) GetForegroundAndPrevious()
    {
        var fg = User32.GetForegroundWindow();
        if (!WindowCandidateFilter.IsEligible(fg))
        {
            fg = 0;
        }

        nint prev = 0;

        lock (_gate)
        {
            // Previous distinct from current foreground.
            for (var i = 1; i <= _count; i++)
            {
                var idx = _next - i;
                if (idx < 0)
                {
                    idx += _history.Length;
                }

                var h = _history[idx];
                if (h != 0 && h != fg && WindowCandidateFilter.IsEligible(h))
                {
                    prev = h;
                    break;
                }
            }
        }

        return (fg, prev);
    }

    public nint[] GetHistorySnapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return [];
            }

            var list = new nint[_count];
            for (var i = 0; i < _count; i++)
            {
                var idx = _next - 1 - i;
                if (idx < 0)
                {
                    idx += _history.Length;
                }

                list[i] = _history[idx];
            }

            return list;
        }
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!WindowCandidateFilter.IsEligible(hwnd))
        {
            return;
        }

        lock (_gate)
        {
            _history[_next] = hwnd;
            _next = (_next + 1) % _history.Length;
            _count = Math.Min(_count + 1, _history.Length);
        }
    }

    private void ClearHistory()
    {
        lock (_gate)
        {
            Array.Clear(_history);
            _next = 0;
            _count = 0;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
