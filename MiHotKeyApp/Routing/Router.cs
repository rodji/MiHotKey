namespace MiHotKeyApp.Routing;

using MiHotKeyApp.Config;
using MiHotKeyApp.Execution;
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
    private readonly ILogger _logExec;

    private readonly TargetSelector _selector;
    private readonly WindowInfoProvider _info;
    private readonly WindowRuleMatcher _matcher;
    private readonly FocusController _focus;
    private readonly KeySender _sender;
    private readonly ProgramRunner _programRunner;

    private readonly Dictionary<string, Dictionary<string, (RouteActionType Type, string Id)>> _routesByTrigger = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (ParsedShortcut Shortcut, SendMode Mode)> _shortcuts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProgramConfig> _programs = new(StringComparer.OrdinalIgnoreCase);

    private TargetSelectionMode _mode = TargetSelectionMode.ForegroundThenPrevious;
    private FocusPolicy _focusPolicy = FocusPolicy.ActivateTargetTemporarily;
    private Timing _timing = new(5, 2, 2);

    public Router(
        ILoggerFactory loggerFactory,
        TargetSelector selector,
        WindowInfoProvider info,
        WindowRuleMatcher matcher,
        FocusController focus,
        KeySender sender,
        ProgramRunner programRunner)
    {
        _logTarget = loggerFactory.CreateLogger(LogCategories.Target);
        _logMatch = loggerFactory.CreateLogger(LogCategories.Match);
        _logSend = loggerFactory.CreateLogger(LogCategories.Send);
        _logExec = loggerFactory.CreateLogger(LogCategories.Exec);

        _selector = selector;
        _info = info;
        _matcher = matcher;
        _focus = focus;
        _sender = sender;
        _programRunner = programRunner;
    }

    public void ApplyConfig(AppConfig cfg)
    {
        _mode = cfg.App.TargetSelectionMode;
        _focusPolicy = cfg.App.FocusPolicy;
        _timing = new Timing(cfg.App.SendTimingMs.ModDownToKeyDown, cfg.App.SendTimingMs.KeyDownToKeyUp, cfg.App.SendTimingMs.KeyUpToModUp);

        _matcher.SetRules(cfg.Targets.Rules);

        _routesByTrigger.Clear();
        foreach (var (triggerId, routes) in cfg.RoutesByTrigger)
        {
            var map = new Dictionary<string, (RouteActionType Type, string Id)>(StringComparer.OrdinalIgnoreCase);
            foreach (var route in routes)
            {
                var rule = (route.Rule ?? "").Trim();
                map[rule] = (route.ActionType, route.ActionId);
            }
            _routesByTrigger[triggerId] = map;
        }

        _shortcuts = cfg.Shortcuts.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var parsed = ShortcutParser.Parse(kvp.Value.Keys);
                return (parsed, kvp.Value.Send);
            },
            StringComparer.OrdinalIgnoreCase);

        _programs = new Dictionary<string, ProgramConfig>(cfg.Programs, StringComparer.OrdinalIgnoreCase);
    }

    public void HandleTrigger(string triggerId)
    {
        if (!_routesByTrigger.TryGetValue(triggerId, out var routes))
        {
            _logMatch.LogInformation("trigger={trigger} routes=missing", triggerId);
            return;
        }

        if (routes.TryGetValue("", out var unconditional))
        {
            if (unconditional.Type == RouteActionType.Shortcut)
            {
                if (!_shortcuts.TryGetValue(unconditional.Id, out var shortcut))
                {
                    _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut} missing=1", triggerId, unconditional.Id);
                    return;
                }

                _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut}", triggerId, unconditional.Id);

                var ok = false;
                if (shortcut.Mode == SendMode.Messages)
                {
                    var hwnd = User32.GetForegroundWindow();
                    if (hwnd == 0)
                    {
                        _logSend.LogWarning("mode={mode} keys=\"{keys}\" ok=0 err=NoForegroundWindow", shortcut.Mode, shortcut.Shortcut.Text);
                        return;
                    }

                    ok = _sender.Send(hwnd, shortcut.Shortcut, shortcut.Mode, _timing);
                }
                else
                {
                    ok = _sender.Send(shortcut.Shortcut, shortcut.Mode, _timing);
                }

                _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, ok ? 1 : 0);
                return;
            }

            if (unconditional.Type == RouteActionType.Program)
            {
                if (!_programs.TryGetValue(unconditional.Id, out var program))
                {
                    _logMatch.LogInformation("trigger={trigger} rule=any program={program} missing=1", triggerId, unconditional.Id);
                    return;
                }

                _logExec.LogInformation("trigger={trigger} rule=any program={program}", triggerId, unconditional.Id);
                _ = _programRunner.TryStart(unconditional.Id, program, context: $"trigger={triggerId} rule=<any>");
                return;
            }

            _logMatch.LogInformation("trigger={trigger} rule=any actionType={type} unsupported=1", triggerId, unconditional.Type);
            return;
        }

        var candidates = _selector.GetCandidates(_mode);
        if (candidates.Length == 0)
        {
            _logMatch.LogInformation("none reason=noCandidates");
            return;
        }

        foreach (var hwnd in candidates)
        {
            var wi = _info.GetInfo(hwnd);
            _logTarget.LogDebug("cand hwnd=0x{hwnd:X} pid={pid} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.Pid, wi.ProcessName, wi.ClassName, wi.Title);

            var rule = _matcher.Match(wi);
            if (rule is null)
            {
                continue;
            }

            if (!routes.TryGetValue(rule.Id, out var action))
            {
                _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} action=missing", triggerId, rule.Id, rule.Prio);
                return;
            }

            if (action.Type == RouteActionType.Shortcut)
            {
                if (!_shortcuts.TryGetValue(action.Id, out var shortcut))
                {
                    _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut} missing=1", triggerId, rule.Id, rule.Prio, action.Id);
                    return;
                }

                _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut}", triggerId, rule.Id, rule.Prio, action.Id);

                var ok = false;
                if (shortcut.Mode == SendMode.Messages)
                {
                    ok = _sender.Send(hwnd, shortcut.Shortcut, shortcut.Mode, _timing);
                }
                else if (_focusPolicy == FocusPolicy.NoFocusChange)
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

            if (action.Type == RouteActionType.Program)
            {
                if (!_programs.TryGetValue(action.Id, out var program))
                {
                    _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program} missing=1", triggerId, rule.Id, rule.Prio, action.Id);
                    return;
                }

                _logExec.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program}", triggerId, rule.Id, rule.Prio, action.Id);
                _ = _programRunner.TryStart(action.Id, program, context: $"trigger={triggerId} rule={rule.Id} hwnd=0x{(nuint)hwnd:X}");
                return;
            }

            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} actionType={type} unsupported=1", triggerId, rule.Id, rule.Prio, action.Type);
            return;
        }

        _logMatch.LogInformation("none");
    }

    public bool RunProgram(string programId, string? context)
    {
        if (!_programs.TryGetValue(programId, out var program))
        {
            _logExec.LogWarning("program missing id={id} ctx=\"{ctx}\"", programId, context ?? "");
            return false;
        }

        return _programRunner.TryStart(programId, program, context);
    }
}
