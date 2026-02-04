namespace MiHotKeyApp.Input.Hotkey;

using System.Windows.Forms;

internal static class HotkeyParser
{
    public static HotkeyDefinition Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Hotkey is empty");
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new FormatException($"Invalid hotkey: {text}");
        }

        var modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Win;
                continue;
            }

            if (key != Keys.None)
            {
                throw new FormatException($"Hotkey has multiple main keys: {text}");
            }

            if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            {
                key = (Keys)char.ToUpperInvariant(part[0]);
                continue;
            }

            if (Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed))
            {
                key = parsed;
                continue;
            }

            throw new FormatException($"Unknown key token '{part}' in '{text}'");
        }

        if (key == Keys.None)
        {
            throw new FormatException($"Hotkey has no main key: {text}");
        }

        return new HotkeyDefinition(modifiers, key);
    }
}

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

internal readonly record struct HotkeyDefinition(HotkeyModifiers Modifiers, Keys Key);

