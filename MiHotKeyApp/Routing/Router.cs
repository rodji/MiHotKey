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
        _ = InvokeTrigger(triggerId, context: null);
    }

    public RouteInvocationResult InvokeTrigger(string triggerId, string? context)
    {
        if (!_routesByTrigger.TryGetValue(triggerId, out var routes))
        {
            _logMatch.LogInformation("trigger={trigger} routes=missing", triggerId);
            return RouteInvocationResult.MissingTrigger(triggerId);
        }

        if (TryHandleUnconditional(triggerId, routes, context, out var unconditionalResult))
        {
            return unconditionalResult;
        }

        return HandleRoutedTrigger(triggerId, routes, context);
    }

    private bool TryHandleUnconditional(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        string? context,
        out RouteInvocationResult result)
    {
        result = default;

        if (!routes.TryGetValue("", out var action))
        {
            return false;
        }

        if (action.Type == RouteActionType.Shortcut)
        {
            if (!_shortcuts.TryGetValue(action.Id, out var shortcut))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut} missing=1", triggerId, action.Id);
                result = RouteInvocationResult.ExecutionFailed($"configured shortcut not found: {action.Id}");
                return true;
            }

            _logMatch.LogInformation("trigger={trigger} rule=any shortcut={shortcut}", triggerId, action.Id);

            var res = SendShortcutUnconditional(shortcut);
            if (res.LogResult)
            {
                _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, res.Ok ? 1 : 0);
            }

            result = res.Ok
                ? RouteInvocationResult.Success($"route executed: {triggerId}")
                : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
            return true;
        }

        if (action.Type == RouteActionType.Program)
        {
            if (!_programs.TryGetValue(action.Id, out var program))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any program={program} missing=1", triggerId, action.Id);
                result = RouteInvocationResult.ExecutionFailed($"configured program not found: {action.Id}");
                return true;
            }

            _logExec.LogInformation("trigger={trigger} rule=any program={program}", triggerId, action.Id);
            var ok = _programRunner.TryStart(action.Id, program, context: BuildContext(triggerId, "<any>", context));
            result = ok
                ? RouteInvocationResult.Success($"route executed: {triggerId}")
                : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
            return true;
        }

        if (action.Type == RouteActionType.Audio)
        {
            if (!_audioDevices.TryGetValue(action.Id, out var audio))
            {
                _logMatch.LogInformation("trigger={trigger} rule=any audio={audio} missing=1", triggerId, action.Id);
                result = RouteInvocationResult.ExecutionFailed($"configured audio action not found: {action.Id}");
                return true;
            }

            _logAudio.LogInformation("trigger={trigger} rule=any audio={audio}", triggerId, action.Id);
            var ok = _audio.Execute(action.Id, audio, context: BuildContext(triggerId, "<any>", context));
            result = ok
                ? RouteInvocationResult.Success($"route executed: {triggerId}")
                : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
            return true;
        }

        _logMatch.LogInformation("trigger={trigger} rule=any actionType={type} unsupported=1", triggerId, action.Type);
        result = RouteInvocationResult.ExecutionFailed($"unsupported action type for route: {triggerId}");
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

    private RouteInvocationResult HandleRoutedTrigger(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        string? context)
    {
        if (TryHandleHistoryPass(triggerId, routes, context, out var historyResult))
        {
            return historyResult;
        }

        if (TryHandleGlobalFallback(triggerId, routes, context, out var globalResult))
        {
            return globalResult;
        }

        _logMatch.LogInformation("none");
        return RouteInvocationResult.NoMatch(triggerId);
    }

    private bool TryHandleHistoryPass(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        string? context,
        out RouteInvocationResult result)
    {
        result = default;
        var candidates = _selector.GetCandidates(_targetSearchDepth);
        if (candidates.Length == 0)
        {
            return false;
        }

        foreach (var hwnd in candidates)
        {
            var baseInfo = _info.GetInfo(hwnd, WindowInfoFields.ProcessName | WindowInfoFields.ClassName);
            LogCandidate(baseInfo);

            var candidateRules = _matcher.GetPotentialMatchesWithoutTitle(baseInfo);
            if (candidateRules.Length == 0)
            {
                continue;
            }

            var detailedInfo = baseInfo;
            var titleLoaded = false;
            foreach (var rule in candidateRules)
            {
                if (_matcher.RuleNeedsTitle(rule))
                {
                    if (!titleLoaded)
                    {
                        detailedInfo = _info.GetInfo(hwnd, WindowInfoFields.ProcessName | WindowInfoFields.Title | WindowInfoFields.ClassName);
                        titleLoaded = true;
                    }

                    if (!_matcher.IsMatch(detailedInfo, rule))
                    {
                        continue;
                    }
                }

                if (!routes.TryGetValue(rule.Id, out var action))
                {
                    _logMatch.LogDebug("trigger={trigger} rule={rule} prio={prio} action=missing skip=1", triggerId, rule.Id, rule.Prio);
                    continue;
                }

                var wi = titleLoaded ? detailedInfo : baseInfo;
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

                result = ExecuteAction(triggerId, rule, action, hwnd, context);
                return true;
            }
        }

        return false;
    }

    private bool TryHandleGlobalFallback(
        string triggerId,
        Dictionary<string, (RouteActionType Type, string Id)> routes,
        string? context,
        out RouteInvocationResult result)
    {
        result = default;
        var globalRules = _matcher.GetRulesInPriorityOrder()
            .Where(rule => routes.TryGetValue(rule.Id, out var action) && IsGlobalAction(action))
            .ToArray();

        if (globalRules.Length == 0)
        {
            return false;
        }

        var windows = _info.GetTopLevelWindows()
            .Where(WindowCandidateFilter.IsEligible)
            .ToArray();

        if (windows.Length == 0)
        {
            return false;
        }

        foreach (var rule in globalRules)
        {
            var action = routes[rule.Id];
            var needsTitle = _matcher.RuleNeedsTitle(rule);
            foreach (var hwnd in windows)
            {
                var baseInfo = _info.GetInfo(hwnd, WindowInfoFields.ProcessName | WindowInfoFields.ClassName);
                LogCandidate(baseInfo);
                if (!_matcher.MightMatchWithoutTitle(baseInfo, rule))
                {
                    continue;
                }

                var info = baseInfo;
                if (needsTitle)
                {
                    info = _info.GetInfo(hwnd, WindowInfoFields.ProcessName | WindowInfoFields.Title | WindowInfoFields.ClassName);
                    if (!_matcher.IsMatch(info, rule))
                    {
                        continue;
                    }
                }

                _logMatch.LogInformation(
                    "trigger={trigger} matched hwnd=0x{hwnd:X} pid={pid} proc={proc} cls={cls} title=\"{title}\" rule={rule} prio={prio} fallback=global",
                    triggerId,
                    (nuint)info.Hwnd,
                    info.Pid,
                    info.ProcessName,
                    info.ClassName,
                    info.Title,
                    rule.Id,
                    rule.Prio);

                result = ExecuteAction(triggerId, rule, action, info.Hwnd, context);
                return true;
            }
        }

        return false;
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

    private void LogCandidate(WindowInfo wi)
    {
        _logTarget.LogDebug("cand hwnd=0x{hwnd:X} pid={pid} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.Pid, wi.ProcessName, wi.ClassName, wi.Title);
    }

    private RouteInvocationResult ExecuteAction(
        string triggerId,
        TargetRuleConfig rule,
        (RouteActionType Type, string Id) action,
        nint hwnd,
        string? context)
    {
        if (action.Type == RouteActionType.Shortcut)
        {
            return ExecuteShortcut(triggerId, rule, action.Id, hwnd);
        }

        if (action.Type == RouteActionType.Program)
        {
            return ExecuteProgram(triggerId, rule, action.Id, hwnd, context);
        }

        if (action.Type == RouteActionType.Audio)
        {
            return ExecuteAudio(triggerId, rule, action.Id, hwnd, context);
        }

        _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} actionType={type} unsupported=1", triggerId, rule.Id, rule.Prio, action.Type);
        return RouteInvocationResult.ExecutionFailed($"unsupported action type for route: {triggerId}");
    }

    private RouteInvocationResult ExecuteShortcut(string triggerId, TargetRuleConfig rule, string shortcutId, nint hwnd)
    {
        if (!_shortcuts.TryGetValue(shortcutId, out var shortcut))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut} missing=1", triggerId, rule.Id, rule.Prio, shortcutId);
            return RouteInvocationResult.ExecutionFailed($"configured shortcut not found: {shortcutId}");
        }

        _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} shortcut={shortcut}", triggerId, rule.Id, rule.Prio, shortcutId);

        var res = SendShortcutToWindow(hwnd, shortcut);
        if (res.LogResult)
        {
            _logSend.LogInformation("mode={mode} keys=\"{keys}\" ok={ok}", shortcut.Mode, shortcut.Shortcut.Text, res.Ok ? 1 : 0);
        }

        return res.Ok
            ? RouteInvocationResult.Success($"route executed: {triggerId}")
            : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
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

    private RouteInvocationResult ExecuteProgram(string triggerId, TargetRuleConfig rule, string programId, nint hwnd, string? context)
    {
        if (!_programs.TryGetValue(programId, out var program))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program} missing=1", triggerId, rule.Id, rule.Prio, programId);
            return RouteInvocationResult.ExecutionFailed($"configured program not found: {programId}");
        }

        _logExec.LogInformation("trigger={trigger} rule={rule} prio={prio} program={program}", triggerId, rule.Id, rule.Prio, programId);
        var ok = _programRunner.TryStart(programId, program, context: BuildContext(triggerId, rule.Id, context, hwnd));
        return ok
            ? RouteInvocationResult.Success($"route executed: {triggerId}")
            : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
    }

    private RouteInvocationResult ExecuteAudio(string triggerId, TargetRuleConfig rule, string audioId, nint hwnd, string? context)
    {
        if (!_audioDevices.TryGetValue(audioId, out var audio))
        {
            _logMatch.LogInformation("trigger={trigger} rule={rule} prio={prio} audio={audio} missing=1", triggerId, rule.Id, rule.Prio, audioId);
            return RouteInvocationResult.ExecutionFailed($"configured audio action not found: {audioId}");
        }

        _logAudio.LogInformation("trigger={trigger} rule={rule} prio={prio} audio={audio}", triggerId, rule.Id, rule.Prio, audioId);
        var ok = _audio.Execute(audioId, audio, context: BuildContext(triggerId, rule.Id, context, hwnd));
        return ok
            ? RouteInvocationResult.Success($"route executed: {triggerId}")
            : RouteInvocationResult.ExecutionFailed($"route execution failed: {triggerId}");
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

    private static string BuildContext(string triggerId, string ruleId, string? context, nint hwnd = default)
    {
        var suffix = string.IsNullOrWhiteSpace(context) ? "" : $" src={context}";
        return hwnd == 0
            ? $"trigger={triggerId} rule={ruleId}{suffix}"
            : $"trigger={triggerId} rule={ruleId} hwnd=0x{(nuint)hwnd:X}{suffix}";
    }
}
