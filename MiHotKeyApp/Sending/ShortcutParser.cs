namespace MiHotKeyApp.Sending;

using System.Windows.Forms;

internal static class ShortcutParser
{
    public static ParsedShortcut Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Shortcut is empty");
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = ShortcutModifiers.None;
        Keys key = Keys.None;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ShortcutModifiers.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ShortcutModifiers.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ShortcutModifiers.Shift;
                continue;
            }

            if (key != Keys.None)
            {
                throw new FormatException($"Shortcut has multiple main keys: {text}");
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
            throw new FormatException($"Shortcut has no main key: {text}");
        }

        return new ParsedShortcut(mods, key, text);
    }
}

[Flags]
internal enum ShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
}

internal readonly record struct ParsedShortcut(ShortcutModifiers Modifiers, Keys Key, string Text);

