namespace MiHotKeyApp.Targeting;

using System.Diagnostics;
using MiHotKeyApp.Native;

internal sealed class WindowInfoProvider
{
    public WindowInfo GetInfo(nint hwnd)
    {
        if (hwnd == 0)
        {
            return new WindowInfo(0, 0, "", "", "");
        }

        _ = User32.GetWindowThreadProcessId(hwnd, out var pid);
        var title = User32.GetWindowTitle(hwnd);
        var cls = User32.GetWindowClassName(hwnd);

        var procName = "";
        try
        {
            procName = Process.GetProcessById((int)pid).ProcessName ?? "";
        }
        catch
        {
        }

        return new WindowInfo(hwnd, pid, procName, title, cls);
    }
}
