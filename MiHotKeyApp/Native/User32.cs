namespace MiHotKeyApp.Native;

using System.Runtime.InteropServices;
using System.Text;

internal static class User32
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint SMTO_BLOCK = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const uint INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;

    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint SPI_GETSCREENSAVERRUNNING = 0x0072;

    public delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        uint fuFlags,
        uint uTimeout,
        out nint lpdwResult);

    public static bool TryGetWindowTitle(nint hwnd, int timeoutMs, out string title)
    {
        title = "";
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return true;
        }

        if (IsHungAppWindow(hwnd))
        {
            return false;
        }

        var flags = SMTO_BLOCK | SMTO_ABORTIFHUNG;
        if (SendMessageTimeoutW(hwnd, WM_GETTEXTLENGTH, 0, 0, flags, (uint)Math.Max(1, timeoutMs), out var lenResult) == 0)
        {
            return false;
        }

        var cap = Math.Clamp((int)lenResult + 1, 2, 4096);
        var buffer = Marshal.AllocHGlobal(cap * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (SendMessageTimeoutW(hwnd, WM_GETTEXT, (nint)cap, buffer, flags, (uint)Math.Max(1, timeoutMs), out _) == 0)
            {
                return false;
            }

            title = Marshal.PtrToStringUni(buffer) ?? "";
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    public static string GetWindowClassName(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return "";
        }

        var sb = new StringBuilder(256);
        var copied = GetClassNameW(hwnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : "";
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);
}
