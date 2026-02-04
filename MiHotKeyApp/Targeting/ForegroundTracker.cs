namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Native;

internal sealed class ForegroundTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly nint[] _history;
    private int _next;
    private int _count;

    private readonly User32.WinEventDelegate _cb;
    private nint _hook;

    public ForegroundTracker(int capacity = 10)
    {
        _history = new nint[Math.Max(2, capacity)];
        _cb = OnWinEvent;
    }

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

    public (nint Foreground, nint Previous) GetForegroundAndPrevious()
    {
        var fg = User32.GetForegroundWindow();
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
                if (h != 0 && h != fg)
                {
                    prev = h;
                    break;
                }
            }
        }

        return (fg, prev);
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == 0)
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

    public void Dispose()
    {
        if (_hook != 0)
        {
            User32.UnhookWinEvent(_hook);
            _hook = 0;
        }
    }
}

