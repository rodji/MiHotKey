namespace MiHotKeyApp.Sending;

using System.Runtime.InteropServices;
using MiHotKeyApp.Config;
using MiHotKeyApp.Native;
using Microsoft.Extensions.Logging;

internal sealed class KeySender
{
    private readonly ILogger _logger;

    public KeySender(ILogger logger)
    {
        _logger = logger;
    }

    public bool Send(ParsedShortcut shortcut, SendMode mode, Timing timing)
    {
        try
        {
            return mode switch
            {
                SendMode.Scan => SendScan(shortcut, timing),
                SendMode.Vk => SendVk(shortcut, timing),
                _ => SendScan(shortcut, timing),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendInput failed");
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

    private static void AddMods(List<User32.INPUT> inputs, ShortcutModifiers mods, bool down, bool scan)
    {
        if (mods.HasFlag(ShortcutModifiers.Control))
        {
            inputs.Add(MakeKey(scan, vk: 0x11, scanCode: 0x1D, down));
        }

        if (mods.HasFlag(ShortcutModifiers.Shift))
        {
            inputs.Add(MakeKey(scan, vk: 0x10, scanCode: 0x2A, down));
        }

        if (mods.HasFlag(ShortcutModifiers.Alt))
        {
            inputs.Add(MakeKey(scan, vk: 0x12, scanCode: 0x38, down));
        }
    }

    private static void AddKey(List<User32.INPUT> inputs, System.Windows.Forms.Keys key, bool down, bool scan)
    {
        if (scan)
        {
            var sc = ScanCodeMap.TryGetScanCode(key, out var code) ? code : (ushort)0;
            inputs.Add(MakeKey(scan: true, vk: 0, scanCode: sc, down));
            return;
        }

        inputs.Add(MakeKey(scan: false, vk: (ushort)key, scanCode: 0, down));
    }

    private static User32.INPUT MakeKey(bool scan, ushort vk, ushort scanCode, bool down)
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
