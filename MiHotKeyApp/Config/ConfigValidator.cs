namespace MiHotKeyApp.Config;

using MiHotKeyApp.Input.Hotkey;
using MiHotKeyApp.Sending;

internal static class ConfigValidator
{
    public static void Validate(AppConfig cfg)
    {
        if (cfg.Version != 2)
        {
            throw new InvalidDataException($"Unsupported config version: {cfg.Version}");
        }

        if (cfg.App.LogBufferSize < 10 || cfg.App.LogBufferSize > 10000)
        {
            throw new InvalidDataException("app.logBufferSize must be between 10 and 10000");
        }

        if (cfg.App.ForegroundHistorySize < 0 || cfg.App.ForegroundHistorySize > 1000)
        {
            throw new InvalidDataException("app.foregroundHistorySize must be between 0 and 1000");
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

        var programs = new HashSet<string>(cfg.Programs.Keys, StringComparer.OrdinalIgnoreCase);
        var audioDevices = new HashSet<string>(cfg.AudioDevices.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, prog) in cfg.Programs)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prog.File))
            {
                throw new InvalidDataException("programs entries must have non-empty id and file");
            }

            if (prog.UseShellExecute && prog.Env.Count > 0)
            {
                throw new InvalidDataException($"programs['{id}'] has useShellExecute=true but also sets env; env is only supported when useShellExecute=false");
            }
        }

        foreach (var (id, audio) in cfg.AudioDevices)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("audioDevices entries must have non-empty id");
            }

            var hasDeviceId = !string.IsNullOrWhiteSpace(audio.DeviceId);
            var hasDeviceIds = audio.DeviceIds.Any(static value => !string.IsNullOrWhiteSpace(value));

            if (hasDeviceId && hasDeviceIds)
            {
                throw new InvalidDataException($"audioDevices['{id}'] cannot set both deviceId and deviceIds");
            }

            if (audio.Scope == AudioDeviceScope.AllActiveInFlow && (hasDeviceId || hasDeviceIds))
            {
                throw new InvalidDataException($"audioDevices['{id}'] scope=AllActiveInFlow cannot be combined with deviceId/deviceIds");
            }

            foreach (var deviceId in audio.DeviceIds)
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    throw new InvalidDataException($"audioDevices['{id}'].deviceIds must not contain empty values");
                }
            }
        }

        var shortcuts = new HashSet<string>(cfg.Shortcuts.Keys, StringComparer.OrdinalIgnoreCase);
        var triggerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hotkey in cfg.Inputs.Hotkeys)
        {
            if (!string.IsNullOrWhiteSpace(hotkey.Id))
            {
                triggerIds.Add(hotkey.Id);
            }
        }
        foreach (var binding in cfg.Bindings.Keys)
        {
            triggerIds.Add(binding);
        }

        foreach (var (trigger, routes) in cfg.RoutesByTrigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                throw new InvalidDataException("routesByTrigger keys must not be empty");
            }

            if (!triggerIds.Contains(trigger))
            {
                throw new InvalidDataException($"routesByTrigger trigger not found in inputs/bindings: {trigger}");
            }

            foreach (var route in routes)
            {
                var rule = (route.Rule ?? "").Trim();
                if (rule.Length > 0 && !ruleIds.Contains(rule))
                {
                    throw new InvalidDataException($"routesByTrigger['{trigger}'] rule not found: {route.Rule}");
                }

                if (string.IsNullOrWhiteSpace(route.ActionId))
                {
                    throw new InvalidDataException($"routesByTrigger['{trigger}'] has empty actionId for rule: {route.Rule}");
                }

                if (route.ActionType == RouteActionType.Shortcut && !shortcuts.Contains(route.ActionId))
                {
                    throw new InvalidDataException($"routesByTrigger['{trigger}'] shortcut not found: {route.ActionId}");
                }

                if (route.ActionType == RouteActionType.Program && !programs.Contains(route.ActionId))
                {
                    throw new InvalidDataException($"routesByTrigger['{trigger}'] program not found: {route.ActionId}");
                }

                if (route.ActionType == RouteActionType.Audio && !audioDevices.Contains(route.ActionId))
                {
                    throw new InvalidDataException($"routesByTrigger['{trigger}'] audio device not found: {route.ActionId}");
                }
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

            if (wmi.SessionPolicyByEvent.Count > 0)
            {
                var knownEvents = new HashSet<string>(wmi.Map.Values, StringComparer.OrdinalIgnoreCase);
                foreach (var (ev, _) in wmi.SessionPolicyByEvent)
                {
                    if (string.IsNullOrWhiteSpace(ev))
                    {
                        throw new InvalidDataException($"inputs.wmi[{wmi.Id}].sessionPolicyByEvent has empty event key");
                    }

                    if (!knownEvents.Contains(ev))
                    {
                        throw new InvalidDataException($"inputs.wmi[{wmi.Id}].sessionPolicyByEvent event not found in map values: {ev}");
                    }
                }
            }
        }

        foreach (var logi in cfg.Inputs.Logi)
        {
            if (string.IsNullOrWhiteSpace(logi.Id))
            {
                throw new InvalidDataException("inputs.logi[] must have id");
            }

            if (logi.Map.Count == 0)
            {
                throw new InvalidDataException($"inputs.logi[{logi.Id}].map must not be empty");
            }

            if (!string.IsNullOrWhiteSpace(logi.VendorId) && !TryParseHexWord(logi.VendorId, out _))
            {
                throw new InvalidDataException($"inputs.logi[{logi.Id}].vendorId must be hex (for example '046D')");
            }

            if (!string.IsNullOrWhiteSpace(logi.ProductId) && !TryParseHexWord(logi.ProductId, out _))
            {
                throw new InvalidDataException($"inputs.logi[{logi.Id}].productId must be hex (for example 'B35B')");
            }

            if (logi.UsagePage is < 0 or > 0xFFFF)
            {
                throw new InvalidDataException($"inputs.logi[{logi.Id}].usagePage must be 0..65535");
            }

            if (logi.Usage is < 0 or > 0xFFFF)
            {
                throw new InvalidDataException($"inputs.logi[{logi.Id}].usage must be 0..65535");
            }

            if (logi.SessionPolicyByEvent.Count > 0)
            {
                var knownEvents = new HashSet<string>(logi.Map.Values, StringComparer.OrdinalIgnoreCase);
                foreach (var (ev, _) in logi.SessionPolicyByEvent)
                {
                    if (string.IsNullOrWhiteSpace(ev))
                    {
                        throw new InvalidDataException($"inputs.logi[{logi.Id}].sessionPolicyByEvent has empty event key");
                    }

                    if (!knownEvents.Contains(ev))
                    {
                        throw new InvalidDataException($"inputs.logi[{logi.Id}].sessionPolicyByEvent event not found in map values: {ev}");
                    }
                }
            }
        }

        foreach (var (id, sc) in cfg.Shortcuts)
        {
            var parsed = ShortcutParser.Parse(sc.Keys);
            if ((sc.Send == SendMode.Scan || sc.Send == SendMode.Global) && !ScanCodeMap.TryGetScanCode(parsed.Key, out _))
            {
                throw new InvalidDataException($"shortcuts['{id}'] uses send=scan/global but key '{parsed.Key}' has no scan code mapping");
            }
        }
    }

    private static bool TryParseHexWord(string value, out ushort parsed)
    {
        var s = value.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsed);
    }
}
