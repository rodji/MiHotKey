namespace MiHotKeyApp.Targeting;

using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public nint[] GetTopLevelWindows()
    {
        var list = new List<nint>();
        var handle = GCHandle.Alloc(list);
        try
        {
            User32.EnumWindows(EnumWindowsCallback, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        return list.ToArray();
    }

    private static bool EnumWindowsCallback(nint hwnd, nint lParam)
    {
        var handle = GCHandle.FromIntPtr(lParam);
        if (handle.Target is List<nint> list)
        {
            list.Add(hwnd);
        }

        return true;
    }
}
