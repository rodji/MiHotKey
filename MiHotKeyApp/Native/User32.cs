namespace MiHotKeyApp.Native;

using System.Runtime.InteropServices;
using System.Text;

internal static class User32
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const uint INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

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

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    public static string GetWindowTitle(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return "";
        }

        // GetWindowTextLengthW is best-effort and may return 0 for some windows.
        // Use a capped buffer; with correct CharSet.Unicode this is safe.
        var len = GetWindowTextLengthW(hwnd);
        var cap = Math.Clamp(len + 1, 2, 4096);

        var sb = new StringBuilder(cap);
        var copied = GetWindowTextW(hwnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : "";
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
}
