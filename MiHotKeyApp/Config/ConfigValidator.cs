namespace MiHotKeyApp.Config;

using MiHotKeyApp.Input.Hotkey;
using MiHotKeyApp.Sending;

internal static class ConfigValidator
{
    public static void Validate(AppConfig cfg)
    {
        if (cfg.Version != 1)
        {
            throw new InvalidDataException($"Unsupported config version: {cfg.Version}");
        }

        if (cfg.App.LogBufferSize < 10 || cfg.App.LogBufferSize > 10000)
        {
            throw new InvalidDataException("app.logBufferSize must be between 10 and 10000");
        }

        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in cfg.Targets.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                throw new InvalidDataException("targets.rules[].id is required");
            }

            if (!ruleIds.Add(rule.Id))
            {
                throw new InvalidDataException($"Duplicate targets.rules id: {rule.Id}");
            }

            if (rule.Proc.Length == 0)
            {
                throw new InvalidDataException($"targets.rules[{rule.Id}].proc must not be empty");
            }
        }

        foreach (var (id, sc) in cfg.Shortcuts)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sc.Keys))
            {
                throw new InvalidDataException("shortcuts entries must have non-empty id and keys");
            }
        }

        var shortcuts = new HashSet<string>(cfg.Shortcuts.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var route in cfg.Routes)
        {
            if (!ruleIds.Contains(route.Rule))
            {
                throw new InvalidDataException($"routes rule not found: {route.Rule}");
            }

            if (!shortcuts.Contains(route.Shortcut))
            {
                throw new InvalidDataException($"routes shortcut not found: {route.Shortcut}");
            }
        }

        foreach (var hotkey in cfg.Inputs.Hotkeys)
        {
            if (string.IsNullOrWhiteSpace(hotkey.Id) || string.IsNullOrWhiteSpace(hotkey.Keys))
            {
                throw new InvalidDataException("inputs.hotkeys[] must have id and keys");
            }

            _ = HotkeyParser.Parse(hotkey.Keys);
        }

        foreach (var wmi in cfg.Inputs.Wmi)
        {
            if (string.IsNullOrWhiteSpace(wmi.Id) || string.IsNullOrWhiteSpace(wmi.Query))
            {
                throw new InvalidDataException("inputs.wmi[] must have id and query");
            }

            if (string.IsNullOrWhiteSpace(wmi.Extract.Prop))
            {
                throw new InvalidDataException($"inputs.wmi[{wmi.Id}].extract.prop is required");
            }

            if (wmi.Extract.Index < 0)
            {
                throw new InvalidDataException($"inputs.wmi[{wmi.Id}].extract.index must be >= 0");
            }

            if (wmi.Map.Count == 0)
            {
                throw new InvalidDataException($"inputs.wmi[{wmi.Id}].map must not be empty");
            }
        }

        foreach (var (id, sc) in cfg.Shortcuts)
        {
            var parsed = ShortcutParser.Parse(sc.Keys);
            if (sc.Send == SendMode.Scan && !ScanCodeMap.TryGetScanCode(parsed.Key, out _))
            {
                throw new InvalidDataException($"shortcuts['{id}'] uses send=scan but key '{parsed.Key}' has no scan code mapping");
            }
        }
    }
}
