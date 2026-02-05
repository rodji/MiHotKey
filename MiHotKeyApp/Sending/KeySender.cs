namespace MiHotKeyApp.Sending;

using System.Runtime.InteropServices;
using MiHotKeyApp.Config;
using MiHotKeyApp.Native;
using Microsoft.Extensions.Logging;

internal sealed class KeySender
{
    private readonly ILogger _logger;

    // Virtual-key codes for modifiers (WinUser.h)
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_LWIN = 0x5B;

    // Set 1 (IBM PC/AT) scan codes used by SendInput(KEYEVENTF_SCANCODE) for modifier keys.
    private const ushort SC_CONTROL = 0x1D;
    private const ushort SC_SHIFT = 0x2A;
    private const ushort SC_MENU = 0x38; // Alt
    private const ushort SC_LWIN = 0x5B; // Extended (0xE0 0x5B)

    // MapVirtualKey(VK->VSC) returns a value where low byte is the scan code (for most keys).
    private const ushort SCANCODE_LOWBYTE_MASK = 0xFF;

    // WM_KEYDOWN/WM_KEYUP lParam bit layout.
    // repeatCount (0-15), scanCode (16-23), extended (24), prev (30), transition (31)
    // https://learn.microsoft.com/windows/win32/inputdev/wm-keydown
    private const int KEYLPARAM_SCANCODE_SHIFT = 16;
    private const int KEYLPARAM_EXTENDED_BIT = 24;
    private const int KEYLPARAM_PREV_STATE_BIT = 30;
    private const int KEYLPARAM_TRANSITION_BIT = 31;
    private const uint KEYLPARAM_REPEATCOUNT_1 = 1u;

    public KeySender(ILogger logger)
    {
        _logger = logger;
    }

    public bool Send(ParsedShortcut shortcut, SendMode mode, Timing timing) => Send(targetHwnd: 0, shortcut, mode, timing);

    public bool Send(nint targetHwnd, ParsedShortcut shortcut, SendMode mode, Timing timing)
    {
        try
        {
            return mode switch
            {
                SendMode.Scan => SendScan(shortcut, timing),
                SendMode.Vk => SendVk(shortcut, timing),
                SendMode.Messages => SendMessages(targetHwnd, shortcut, timing),
                SendMode.Global => SendScan(shortcut, timing),
                _ => SendScan(shortcut, timing),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send failed mode={mode}", mode);
            return false;
        }
    }

    private static bool SendScan(ParsedShortcut shortcut, Timing timing)
    {
        var inputs = new List<User32.INPUT>();

        AddMods(inputs, shortcut.Modifiers, down: true, scan: true);
        Sleep(timing.ModDownToKeyDown);
        AddKey(inputs, shortcut.Key, down: true, scan: true);
        Sleep(timing.KeyDownToKeyUp);
        AddKey(inputs, shortcut.Key, down: false, scan: true);
        Sleep(timing.KeyUpToModUp);
        AddMods(inputs, shortcut.Modifiers, down: false, scan: true);

        return Send(inputs);
    }

    private static bool SendVk(ParsedShortcut shortcut, Timing timing)
    {
        var inputs = new List<User32.INPUT>();

        AddMods(inputs, shortcut.Modifiers, down: true, scan: false);
        Sleep(timing.ModDownToKeyDown);
        AddKey(inputs, shortcut.Key, down: true, scan: false);
        Sleep(timing.KeyDownToKeyUp);
        AddKey(inputs, shortcut.Key, down: false, scan: false);
        Sleep(timing.KeyUpToModUp);
        AddMods(inputs, shortcut.Modifiers, down: false, scan: false);

        return Send(inputs);
    }

    private static bool SendMessages(nint hwnd, ParsedShortcut shortcut, Timing timing)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd))
        {
            return false;
        }

        var ok = true;

        ok &= PostMods(hwnd, shortcut.Modifiers, down: true);
        Sleep(timing.ModDownToKeyDown);
        ok &= PostKey(hwnd, (ushort)shortcut.Key, down: true, extended: false);
        Sleep(timing.KeyDownToKeyUp);
        ok &= PostKey(hwnd, (ushort)shortcut.Key, down: false, extended: false);
        Sleep(timing.KeyUpToModUp);
        ok &= PostMods(hwnd, shortcut.Modifiers, down: false);

        return ok;
    }

    private static void AddMods(List<User32.INPUT> inputs, ShortcutModifiers mods, bool down, bool scan)
    {
        if (mods.HasFlag(ShortcutModifiers.Control))
        {
            inputs.Add(MakeKey(scan, vk: VK_CONTROL, scanCode: SC_CONTROL, down, extended: false));
        }

        if (mods.HasFlag(ShortcutModifiers.Shift))
        {
            inputs.Add(MakeKey(scan, vk: VK_SHIFT, scanCode: SC_SHIFT, down, extended: false));
        }

        if (mods.HasFlag(ShortcutModifiers.Alt))
        {
            inputs.Add(MakeKey(scan, vk: VK_MENU, scanCode: SC_MENU, down, extended: false));
        }

        if (mods.HasFlag(ShortcutModifiers.Win))
        {
            inputs.Add(MakeKey(scan, vk: VK_LWIN, scanCode: SC_LWIN, down, extended: true));
        }
    }

    private static bool PostMods(nint hwnd, ShortcutModifiers mods, bool down)
    {
        var ok = true;
        if (mods.HasFlag(ShortcutModifiers.Control))
        {
            ok &= PostKey(hwnd, vk: VK_CONTROL, down, extended: false);
        }

        if (mods.HasFlag(ShortcutModifiers.Shift))
        {
            ok &= PostKey(hwnd, vk: VK_SHIFT, down, extended: false);
        }

        if (mods.HasFlag(ShortcutModifiers.Alt))
        {
            ok &= PostKey(hwnd, vk: VK_MENU, down, extended: false);
        }

        if (mods.HasFlag(ShortcutModifiers.Win))
        {
            ok &= PostKey(hwnd, vk: VK_LWIN, down, extended: true);
        }

        return ok;
    }

    private static bool PostKey(nint hwnd, ushort vk, bool down, bool extended)
    {
        var scan = (ushort)(User32.MapVirtualKeyW(vk, User32.MAPVK_VK_TO_VSC) & SCANCODE_LOWBYTE_MASK);
        var lParam = MakeKeyLParam(scan, extended, down);

        var msg = down ? User32.WM_KEYDOWN : User32.WM_KEYUP;
        return User32.PostMessageW(hwnd, msg, wParam: vk, lParam);
    }

    private static nint MakeKeyLParam(ushort scanCode, bool extended, bool down)
    {
        var lp = KEYLPARAM_REPEATCOUNT_1;
        lp |= (uint)scanCode << KEYLPARAM_SCANCODE_SHIFT;
        if (extended)
        {
            lp |= 1u << KEYLPARAM_EXTENDED_BIT;
        }

        if (!down)
        {
            lp |= 1u << KEYLPARAM_PREV_STATE_BIT;
            lp |= 1u << KEYLPARAM_TRANSITION_BIT;
        }

        return (nint)lp;
    }

    private static void AddKey(List<User32.INPUT> inputs, System.Windows.Forms.Keys key, bool down, bool scan)
    {
        if (scan)
        {
            var sc = ScanCodeMap.TryGetScanCode(key, out var code) ? code : (ushort)0;
            inputs.Add(MakeKey(scan: true, vk: 0, scanCode: sc, down, extended: false));
            return;
        }

        inputs.Add(MakeKey(scan: false, vk: (ushort)key, scanCode: 0, down, extended: false));
    }

    private static User32.INPUT MakeKey(bool scan, ushort vk, ushort scanCode, bool down, bool extended)
    {
        var flags = 0u;
        if (!down)
        {
            flags |= User32.KEYEVENTF_KEYUP;
        }

        if (scan)
        {
            flags |= User32.KEYEVENTF_SCANCODE;
        }

        if (extended)
        {
            flags |= User32.KEYEVENTF_EXTENDEDKEY;
        }

        return new User32.INPUT
        {
            type = User32.INPUT_KEYBOARD,
            U = new User32.InputUnion
            {
                ki = new User32.KEYBDINPUT
                {
                    wVk = scan ? (ushort)0 : vk,
                    wScan = scan ? scanCode : (ushort)0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                }
            }
        };
    }

    private static bool Send(List<User32.INPUT> inputs)
    {
        var sent = User32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<User32.INPUT>());
        return sent == inputs.Count;
    }

    private static void Sleep(int ms)
    {
        if (ms <= 0)
        {
            return;
        }

        Thread.Sleep(ms);
    }
}

internal readonly record struct Timing(int ModDownToKeyDown, int KeyDownToKeyUp, int KeyUpToModUp);

internal static class ScanCodeMap
{
    public static bool TryGetScanCode(System.Windows.Forms.Keys key, out ushort sc)
    {
        sc = key switch
        {
            System.Windows.Forms.Keys.A => 0x1E,
            System.Windows.Forms.Keys.B => 0x30,
            System.Windows.Forms.Keys.C => 0x2E,
            System.Windows.Forms.Keys.D => 0x20,
            System.Windows.Forms.Keys.E => 0x12,
            System.Windows.Forms.Keys.F => 0x21,
            System.Windows.Forms.Keys.G => 0x22,
            System.Windows.Forms.Keys.H => 0x23,
            System.Windows.Forms.Keys.I => 0x17,
            System.Windows.Forms.Keys.J => 0x24,
            System.Windows.Forms.Keys.K => 0x25,
            System.Windows.Forms.Keys.L => 0x26,
            System.Windows.Forms.Keys.M => 0x32,
            System.Windows.Forms.Keys.N => 0x31,
            System.Windows.Forms.Keys.O => 0x18,
            System.Windows.Forms.Keys.P => 0x19,
            System.Windows.Forms.Keys.Q => 0x10,
            System.Windows.Forms.Keys.R => 0x13,
            System.Windows.Forms.Keys.S => 0x1F,
            System.Windows.Forms.Keys.T => 0x14,
            System.Windows.Forms.Keys.U => 0x16,
            System.Windows.Forms.Keys.V => 0x2F,
            System.Windows.Forms.Keys.W => 0x11,
            System.Windows.Forms.Keys.X => 0x2D,
            System.Windows.Forms.Keys.Y => 0x15,
            System.Windows.Forms.Keys.Z => 0x2C,
            _ => 0,
        };

        return sc != 0;
    }
}
