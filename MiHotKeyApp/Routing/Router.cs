namespace MiHotKeyApp.Routing;

using MiHotKeyApp.Config;
using MiHotKeyApp.Audio;
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
    private readonly ILogger _logAudio;

    private readonly TargetSelector _selector;
    private readonly WindowInfoProvider _info;
    private readonly WindowRuleMatcher _matcher;
    private readonly FocusController _focus;
    private readonly KeySender _sender;
    private readonly ProgramRunner _programRunner;
    private readonly AudioDeviceManager _audio;

    private readonly Dictionary<string, Dictionary<string, (RouteActionType Type, string Id)>> _routesByTrigger = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (ParsedShortcut Shortcut, SendMode Mode)> _shortcuts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProgramConfig> _programs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AudioDeviceConfig> _audioDevices = new(StringComparer.OrdinalIgnoreCase);

    private int _targetSearchDepth = 2;
    private FocusPolicy _focusPolicy = FocusPolicy.ActivateTargetTemporarily;
    private Timing _timing = new(5, 2, 2);

    private readonly record struct SendResult(bool Ok, bool LogResult)
    {
        public static SendResult Logged(bool ok) => new(ok, LogResult: true);
        public static SendResult Unlogged(bool ok) => new(ok, LogResult: false);
    }

    private enum RoutingPass
    {
        GlobalOnly,
        NonGlobalOnly,
    }

    public Router(
        ILoggerFactory loggerFactory,
        TargetSelector selector,
        WindowInfoProvider info,
        WindowRuleMatcher matcher,
        FocusController focus,
        KeySender sender,
        ProgramRunner programRunner,
        AudioDeviceManager audio)
    {
        _logTarget = loggerFactory.CreateLogger(LogCategories.Target);
        _logMatch = loggerFactory.CreateLogger(LogCategories.Match);
        _logSend = loggerFactory.CreateLogger(LogCategories.Send);
        _logExec = loggerFactory.CreateLogger(LogCategories.Exec);
        _logAudio = loggerFactory.CreateLogger(LogCategories.Audio);

        _selector = selector;
        _info = info;
        _matcher = matcher;
        _focus = focus;
        _sender = sender;
        _programRunner = programRunner;
        _audio = audio;
    }

    public void ApplyConfig(AppConfig cfg)
    {
        _targetSearchDepth = Math.Max(1, cfg.App.TargetSearchDepth);
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
        _audioDevices = new Dictionary<string, AudioDeviceConfig>(cfg.AudioDevices, StringComparer.OrdinalIgnoreCase);
    }

    public void HandleTrigger(string triggerId)
    {
        if (!_routesByTrigger.TryGetValue(triggerId, out var routes))
        {
            _logMatch.LogInformation("trigger={trigger} routes=missing", triggerId);
            return;
        }

        if (TryHandleUnconditional(triggerId, routes))
        {
            return;
        }

        HandleRoutedTrigger(triggerId, routes);
    }

    private bool TryHandleUnconditional(string triggerId, Dictionary<string, (RouteActionType Type, string Id)> routes)
    {
        if (!routes.TryGetValue("", out var action))
        {
            return false;
        }

        if (action.Type == RouteActionType.Shortcut)
        {
            if (!_shortcuts.TryGetValue(action.Id, out var shortcut))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut} missing=1", triggerId, action.Id);
                return true;
            }

            _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut}", triggerId, action.Id);

            var res = SendShortcutUnconditional(shortcut);
            if (res.LogResult)
            {
                _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, res.Ok ? 1 : 0);
            }
            return true;
        }

        if (action.Type == RouteActionType.Program)
        {
            if (!_programs.TryGetValue(action.Id, out var program))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any program={program} missing=1", triggerId, action.Id);
                return true;
            }

            _logExec.LogInformation("trigger={trigger} rule=any program={program}", triggerId, action.Id);
            _ = _programRunner.TryStart(action.Id, program, context: $"trigger={triggerId} rule=<any>");
            return true;
        }

        if (action.Type == RouteActionType.Audio)
        {
            if (!_audioDevices.TryGetValue(action.Id, out var audio))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any audio={audio} missing=1", triggerId, action.Id);
                return true;
            }

            _logAudio.LogInformation("trigger={trigger} rule=any audio={audio}", triggerId, action.Id);
            _ = _audio.Execute(action.Id, audio, context: $"trigger={triggerId} rule=<any>");
            return true;
        }

        _logMatch.LogInformation("trigger={trigger} rule=any actionType={type} unsupported=1", triggerId, action.Type);
        return true;
    }

    private SendResult SendShortcutUnconditional((ParsedShortcut Shortcut, SendMode Mode) shortcut)
    {
        if (shortcut.Mode == SendMode.Messages)
        {
            var hwnd = User32.GetForegroundWindow();
            if (hwnd == 0)
            {
                _logSend.LogWarning("mode={mode} keys=\"{keys}\" ok=0 err=NoForegroundWindow", shortcut.Mode, shortcut.Shortcut.Text);
                return SendResult.Unlogged(ok: false);
            }

            return SendResult.Logged(_sender.Send(hwnd, shortcut.Shortcut, shortcut.Mode, _timing));
        }

        return SendResult.Logged(_sender.Send(shortcut.Shortcut, shortcut.Mode, _timing));
    }

    private void HandleRoutedTrigger(string triggerId, Dictionary<string, (RouteActionType Type, string Id)> routes)
    {
        var hasGlobalRoutes = routes.Values.Any(IsGlobalAction);
        if (hasGlobalRoutes && TryHandlePass(triggerId, routes, RoutingPass.GlobalOnly, includeAllWindows: true))
        {
            return;
        }

        if (TryHandlePass(triggerId, routes, RoutingPass.NonGlobalOnly, includeAllWindows: false))
        {
            return;
        }

        _logMatch.LogInformation("none");
    }

    private bool TryHandlePass(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        RoutingPass pass,
        bool includeAllWindows)
    {
        var candidates = GetCandidates(includeAllWindows);
        if (candidates.Length == 0)
        {
            _logMatch.LogInformation("none reason=noCandidates");
            return false;
        }

        foreach (var hwnd in candidates)
        {
            if (TryHandleCandidate(triggerId, routes, hwnd, pass))
            {
                return true;
            }
        }

        return false;
    }

    private nint[] GetCandidates(bool includeAllWindows)
    {
        var candidates = _selector.GetCandidates(_targetSearchDepth);
        if (!includeAllWindows)
        {
            return candidates;
        }

        var set = new HashSet<nint>(candidates);
        var list = candidates.ToList();

        foreach (var hwnd in _info.GetTopLevelWindows())
        {
            if (hwnd == 0 || !set.Add(hwnd))
            {
                continue;
            }

            list.Add(hwnd);
        }

        return list.ToArray();
    }

    private bool IsGlobalAction((RouteActionType Type, string Id) action)
    {
        if (action.Type != RouteActionType.Shortcut)
        {
            return false;
        }

        return _shortcuts.TryGetValue(action.Id, out var shortcut)
            && shortcut.Mode == SendMode.Global;
    }

    private bool TryHandleCandidate(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        nint hwnd,
        RoutingPass pass)
    {
        var wi = _info.GetInfo(hwnd);
        _logTarget.LogDebug("cand hwnd=0x{hwnd:X} pid={pid} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.Pid, wi.ProcessName, wi.ClassName, wi.Title);

        var rule = _matcher.Match(wi);
        if (rule is null)
        {
            return false;
        }

        if (!routes.TryGetValue(rule.Id, out var action))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} action=missing", triggerId, rule.Id, rule.Prio);
            return true;
        }

        if (!IsActionInPass(action, pass))
        {
            return false;
        }

        _logMatch.LogInformation(
            "trigger={trigger} matched hwnd=0x{hwnd:X} pid={pid} proc={proc} cls={cls} title=\"{title}\" rule={rule} prio={prio}",
            triggerId,
            (nuint)wi.Hwnd,
            wi.Pid,
            wi.ProcessName,
            wi.ClassName,
            wi.Title,
            rule.Id,
            rule.Prio);

        return ExecuteAction(triggerId, rule, action, hwnd);
    }

    private bool IsActionInPass((RouteActionType Type, string Id) action, RoutingPass pass)
    {
        var isGlobal = IsGlobalAction(action);
        return pass switch
        {
            RoutingPass.GlobalOnly => isGlobal,
            RoutingPass.NonGlobalOnly => !isGlobal,
            _ => true,
        };
    }

    private bool ExecuteAction(string triggerId, TargetRuleConfig rule, (RouteActionType Type, string Id) action, nint hwnd)
    {
        if (action.Type == RouteActionType.Shortcut)
        {
            return ExecuteShortcut(triggerId, rule, action.Id, hwnd);
        }

        if (action.Type == RouteActionType.Program)
        {
            return ExecuteProgram(triggerId, rule, action.Id, hwnd);
        }

        if (action.Type == RouteActionType.Audio)
        {
            return ExecuteAudio(triggerId, rule, action.Id, hwnd);
        }

        _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} actionType={type} unsupported=1", triggerId, rule.Id, rule.Prio, action.Type);
        return true;
    }

    private bool ExecuteShortcut(string triggerId, TargetRuleConfig rule, string shortcutId, nint hwnd)
    {
        if (!_shortcuts.TryGetValue(shortcutId, out var shortcut))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut} missing=1", triggerId, rule.Id, rule.Prio, shortcutId);
            return true;
        }

        _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut}", triggerId, rule.Id, rule.Prio, shortcutId);

        var res = SendShortcutToWindow(hwnd, shortcut);
        if (res.LogResult)
        {
            _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, res.Ok ? 1 : 0);
        }
        return true;
    }

    private SendResult SendShortcutToWindow(nint hwnd, (ParsedShortcut Shortcut, SendMode Mode) shortcut)
    {
        if (shortcut.Mode == SendMode.Global)
        {
            return SendResult.Logged(_sender.Send(shortcut.Shortcut, shortcut.Mode, _timing));
        }

        if (shortcut.Mode == SendMode.Messages)
        {
            return SendResult.Logged(_sender.Send(hwnd, shortcut.Shortcut, shortcut.Mode, _timing));
        }

        if (_focusPolicy == FocusPolicy.NoFocusChange)
        {
            if (User32.GetForegroundWindow() != hwnd)
            {
                _logSend.LogWarning("mode={mode} keys=\"{keys}\" ok=0 err=NoFocusChange", shortcut.Mode, shortcut.Shortcut.Text);
                return SendResult.Unlogged(ok: false);
            }

            return SendResult.Logged(_sender.Send(shortcut.Shortcut, shortcut.Mode, _timing));
        }

        return SendResult.Logged(_focus.TryActivateTemporarily(hwnd, () => _sender.Send(shortcut.Shortcut, shortcut.Mode, _timing)));
    }

    private bool ExecuteProgram(string triggerId, TargetRuleConfig rule, string programId, nint hwnd)
    {
        if (!_programs.TryGetValue(programId, out var program))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program} missing=1", triggerId, rule.Id, rule.Prio, programId);
            return true;
        }

        _logExec.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program}", triggerId, rule.Id, rule.Prio, programId);
        _ = _programRunner.TryStart(programId, program, context: $"trigger={triggerId} rule={rule.Id} hwnd=0x{(nuint)hwnd:X}");
        return true;
    }

    private bool ExecuteAudio(string triggerId, TargetRuleConfig rule, string audioId, nint hwnd)
    {
        if (!_audioDevices.TryGetValue(audioId, out var audio))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} audio={audio} missing=1", triggerId, rule.Id, rule.Prio, audioId);
            return true;
        }

        _logAudio.LogInformation("trigger={trigger} rule={rule} prio={prio} audio={audio}", triggerId, rule.Id, rule.Prio, audioId);
        _ = _audio.Execute(audioId, audio, context: $"trigger={triggerId} rule={rule.Id} hwnd=0x{(nuint)hwnd:X}");
        return true;
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
