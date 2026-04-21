namespace MiHotKeyApp.Targeting;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MiHotKeyApp.Native;

[Flags]
internal enum WindowInfoFields
{
    None = 0,
    ProcessName = 1,
    Title = 2,
    ClassName = 4,
    All = ProcessName | Title | ClassName,
}

internal sealed class WindowInfoProvider
{
    private const int WindowTitleTimeoutMs = 50;
    private readonly ILogger _logger;

    public WindowInfoProvider(ILogger logger)
    {
        _logger = logger;
    }

    public WindowInfo GetInfo(nint hwnd, WindowInfoFields fields = WindowInfoFields.All)
    {
        if (hwnd == 0)
        {
            return new WindowInfo(0, 0, "", "", "");
        }

        uint pid = 0;
        if ((fields & WindowInfoFields.ProcessName) != 0)
        {
            _ = User32.GetWindowThreadProcessId(hwnd, out pid);
        }

        var title = "";
        if ((fields & WindowInfoFields.Title) != 0
            && !User32.TryGetWindowTitle(hwnd, WindowTitleTimeoutMs, out title))
        {
            _logger.LogWarning("window title read timed out hwnd=0x{hwnd:X}", (nuint)hwnd);
        }

        var cls = (fields & WindowInfoFields.ClassName) != 0
            ? User32.GetWindowClassName(hwnd)
            : "";

        var procName = "";
        if ((fields & WindowInfoFields.ProcessName) != 0 && pid != 0)
        {
            try
            {
                procName = Process.GetProcessById((int)pid).ProcessName ?? "";
            }
            catch
            {
            }
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
