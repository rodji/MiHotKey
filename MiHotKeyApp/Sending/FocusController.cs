namespace MiHotKeyApp.Sending;

using MiHotKeyApp.Native;

internal sealed class FocusController
{
    private const int ForegroundSwitchTimeoutMs = 300;
    private const int ForegroundPollDelayMs = 10;

    public bool TryActivateTemporarily(nint targetHwnd, Func<bool> action)
    {
        var prev = User32.GetForegroundWindow();
        if (targetHwnd == 0)
        {
            return false;
        }

        try
        {
            if (prev != targetHwnd)
            {
                TryActivateWindow(targetHwnd, prev);
                if (!WaitForForeground(targetHwnd, ForegroundSwitchTimeoutMs))
                {
                    return false;
                }
            }

            return action();
        }
        finally
        {
            if (prev != 0 && prev != targetHwnd)
            {
                _ = User32.SetForegroundWindow(prev);
            }
        }
    }

    private static void TryActivateWindow(nint targetHwnd, nint currentForeground)
    {
        var currentThread = Kernel32.GetCurrentThreadId();
        var fgThread = User32.GetWindowThreadProcessId(currentForeground, out _);
        var targetThread = User32.GetWindowThreadProcessId(targetHwnd, out _);

        try
        {
            if (fgThread != 0)
            {
                User32.AttachThreadInput(currentThread, fgThread, true);
            }

            if (targetThread != 0)
            {
                User32.AttachThreadInput(currentThread, targetThread, true);
            }

            User32.SetForegroundWindow(targetHwnd);
        }
        finally
        {
            if (targetThread != 0)
            {
                User32.AttachThreadInput(currentThread, targetThread, false);
            }

            if (fgThread != 0)
            {
                User32.AttachThreadInput(currentThread, fgThread, false);
            }
        }
    }

    private static bool WaitForForeground(nint targetHwnd, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (User32.GetForegroundWindow() == targetHwnd)
            {
                return true;
            }

            Thread.Sleep(ForegroundPollDelayMs);
        }

        return User32.GetForegroundWindow() == targetHwnd;
    }
}
