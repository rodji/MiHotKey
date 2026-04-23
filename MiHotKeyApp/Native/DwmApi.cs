namespace MiHotKeyApp.Native;

using System.Runtime.InteropServices;

internal static class DwmApi
{
    private const uint DWMWA_CLOAKED = 14;

    public static bool IsWindowCloaked(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        try
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0
                && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);
}
