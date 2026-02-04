namespace MiHotKeyApp.Routing;

using MiHotKeyApp.Config;
using MiHotKeyApp.Logging;
using MiHotKeyApp.Native;
using MiHotKeyApp.Sending;
using MiHotKeyApp.Targeting;
using Microsoft.Extensions.Logging;

internal sealed class Router
{
    private readonly ILogger _logTarget;
    private readonly ILogger _logMatch;
    private readonly ILogger _logSend;

    private readonly TargetSelector _selector;
    private readonly WindowInfoProvider _info;
    private readonly WindowRuleMatcher _matcher;
    private readonly FocusController _focus;
    private readonly KeySender _sender;

    private readonly Dictionary<string, string> _ruleToShortcut = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (ParsedShortcut Shortcut, SendMode Mode)> _shortcuts = new(StringComparer.OrdinalIgnoreCase);

    private TargetSelectionMode _mode = TargetSelectionMode.ForegroundThenPrevious;
    private FocusPolicy _focusPolicy = FocusPolicy.ActivateTargetTemporarily;
    private Timing _timing = new(5, 2, 2);

    public Router(
        ILoggerFactory loggerFactory,
        TargetSelector selector,
        WindowInfoProvider info,
        WindowRuleMatcher matcher,
        FocusController focus,
        KeySender sender)
    {
        _logTarget = loggerFactory.CreateLogger(LogCategories.Target);
        _logMatch = loggerFactory.CreateLogger(LogCategories.Match);
        _logSend = loggerFactory.CreateLogger(LogCategories.Send);

        _selector = selector;
        _info = info;
        _matcher = matcher;
        _focus = focus;
        _sender = sender;
    }

    public void ApplyConfig(AppConfig cfg)
    {
        _mode = cfg.App.TargetSelectionMode;
        _focusPolicy = cfg.App.FocusPolicy;
        _timing = new Timing(cfg.App.SendTimingMs.ModDownToKeyDown, cfg.App.SendTimingMs.KeyDownToKeyUp, cfg.App.SendTimingMs.KeyUpToModUp);

        _matcher.SetRules(cfg.Targets.Rules);

        _ruleToShortcut.Clear();
        foreach (var route in cfg.Routes)
        {
            _ruleToShortcut[route.Rule] = route.Shortcut;
        }

        _shortcuts = cfg.Shortcuts.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var parsed = ShortcutParser.Parse(kvp.Value.Keys);
                return (parsed, kvp.Value.Send);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public void HandleTrigger(string triggerId)
    {
        var candidates = _selector.GetCandidates(_mode);
        if (candidates.Length == 0)
        {
            _logMatch.LogInformation("none reason=noCandidates");
            return;
        }

        foreach (var hwnd in candidates)
        {
            var wi = _info.GetInfo(hwnd);
            _logTarget.LogDebug("cand hwnd=0x{hwnd:X} pid={pid} proc={proc} title=\"{title}\"", (nuint)wi.Hwnd, wi.Pid, wi.ProcessName, wi.Title);

            var rule = _matcher.Match(wi);
            if (rule is null)
            {
                continue;
            }

            if (!_ruleToShortcut.TryGetValue(rule.Id, out var shortcutId))
            {
                _logMatch.LogInformation("rule={rule} prio={prio} shortcut=missing", rule.Id, rule.Prio);
                return;
            }

            if (!_shortcuts.TryGetValue(shortcutId, out var shortcut))
            {
                _logMatch.LogInformation("rule={rule} prio={prio} shortcut={shortcut} missing=1", rule.Id, rule.Prio, shortcutId);
                return;
            }

            _logMatch.LogInformation("rule={rule} prio={prio} shortcut={shortcut}", rule.Id, rule.Prio, shortcutId);

            var ok = false;
            if (_focusPolicy == FocusPolicy.NoFocusChange)
            {
                if (User32.GetForegroundWindow() != hwnd)
                {
                    _logSend.LogWarning("mode={mode} keys=\"{keys}\" ok=0 err=NoFocusChange", shortcut.Mode, shortcut.Shortcut.Text);
                    return;
                }

                ok = _sender.Send(shortcut.Shortcut, shortcut.Mode, _timing);
            }
            else
            {
                ok = _focus.TryActivateTemporarily(hwnd, () => _sender.Send(shortcut.Shortcut, shortcut.Mode, _timing));
            }

            _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, ok ? 1 : 0);
            return;
        }

        _logMatch.LogInformation("none");
    }
}
