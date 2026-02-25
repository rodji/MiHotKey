namespace MiHotKeyApp.Sending;

using MiHotKeyApp.Native;

internal sealed class FocusController
{
    // Upper bound for waiting until Windows actually switches foreground to the requested window.
    // We intentionally wait because SetForegroundWindow may return before the target starts processing input.
    private const int ForegroundSwitchTimeoutMs = 300;

    // Poll interval while waiting for foreground confirmation.
    private const int ForegroundPollDelayMs = 10;

    // Small settle delay after activation. Some apps (Chrome/WebView apps) may ignore the first shortcut
    // if input is sent immediately after the foreground change notification.
    private const int PostActivateSettleDelayMs = 60;

    // Small delay before restoring focus back, so the target has time to consume key up events.
    private const int PreRestoreSettleDelayMs = 40;

    // Runs an action while the target window is foreground, then restores previous foreground window.
    // This is used for SendInput-based shortcuts that must be delivered to a specific app.
    public bool TryActivateTemporarily(nint targetHwnd, Func<bool> action)
    {
        var prev = User32.GetForegroundWindow();
        var switched = false;
        if (targetHwnd == 0)
        {
            return false;
        }

        try
        {
            if (prev != targetHwnd)
            {
                // Temporarily promote the target to foreground so SendInput goes to the intended app.
                TryActivateWindow(targetHwnd, prev);
                if (!WaitForForeground(targetHwnd, ForegroundSwitchTimeoutMs))
                {
                    // Do not send anything if Windows refused/delayed the switch too much.
                    return false;
                }

                switched = true;
                Thread.Sleep(PostActivateSettleDelayMs);
            }

            return action();
        }
        finally
        {
            if (switched && PreRestoreSettleDelayMs > 0)
            {
                Thread.Sleep(PreRestoreSettleDelayMs);
            }

            if (prev != 0 && prev != targetHwnd)
            {
                // Best-effort restore only; failure here should not affect the already executed action.
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
            // AttachThreadInput helps bypass common focus restrictions when our thread differs from
            // the current foreground window thread and/or the target window thread.
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

            // Polling is sufficient here: we only need a short, bounded wait before sending input.
            Thread.Sleep(ForegroundPollDelayMs);
        }

        return User32.GetForegroundWindow() == targetHwnd;
    }
}
