namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Native;

internal static class WindowCandidateFilter
{
    public static bool IsEligible(nint hwnd)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd) || !User32.IsWindowVisible(hwnd))
        {
            return false;
        }

        var root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        if (root == 0 || root != hwnd)
        {
            return false;
        }

        return !IsIgnoredClass(User32.GetWindowClassName(hwnd));
    }

    public static bool IsIgnoredClass(string cls)
    {
        if (cls.Length == 0)
        {
            return false;
        }

        // Skip taskbar, task switcher, tooltips, and transient explorer surfaces
        // so short alt-tab memory keeps real app windows near the front.
        return cls.Equals("ForegroundStaging", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("TaskListThumbnailWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("ThumbnailDeviceHelperWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("tooltips_class32", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("Progman", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);
    }
}
