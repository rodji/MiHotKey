namespace MiHotKeyApp.Sending;

using MiHotKeyApp.Native;

internal sealed class FocusController
{
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
}
