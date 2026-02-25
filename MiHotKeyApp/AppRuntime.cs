namespace MiHotKeyApp;

using MiHotKeyApp.Config;
using MiHotKeyApp.Audio;
using MiHotKeyApp.Execution;
using MiHotKeyApp.Input;
using MiHotKeyApp.Input.Hotkey;
using MiHotKeyApp.Input.Wmi;
using MiHotKeyApp.Logging;
using MiHotKeyApp.Routing;
using MiHotKeyApp.Sending;
using MiHotKeyApp.Targeting;
using Microsoft.Extensions.Logging;

internal sealed class AppRuntime : IDisposable
{
    private readonly string _baseDir;
    private readonly ConfigStore _configStore;
    private readonly RingLogBuffer _logBuffer;
    private readonly RingBufferLoggerProvider _logProvider;
    private readonly ILoggerFactory _loggerFactory;

    private readonly ILogger _logConfig;
    private readonly ILogger _logDiag;

    private readonly SessionState _session;
    private readonly AutostartManager _autostart;

    private readonly ForegroundTracker _foreground;
    private readonly TargetSelector _selector;
    private readonly WindowInfoProvider _windowInfo;
    private readonly WindowRuleMatcher _matcher;
    private readonly FocusController _focus;
    private readonly KeySender _sender;
    private readonly ProgramRunner _programRunner;
    private readonly AudioDeviceManager _audio;
    private readonly Router _router;

    private readonly GlobalHotkeySource _hotkeys;
    private readonly WmiTriggerSource _wmi;
    private readonly TriggerDispatcher _dispatcher;

    private AppConfig _config;
    private string _resolvedConfigPath;
    private bool? _foregroundTrackingOverride;

    public AppRuntime(string baseDir, SynchronizationContext ui)
    {
        _baseDir = baseDir;
        _configStore = new ConfigStore(baseDir);

        _logBuffer = new RingLogBuffer(new AppConfig().App.LogBufferSize);
        _logProvider = new RingBufferLoggerProvider(_logBuffer, new LoggingSection());
        _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(_logProvider);
        });

        _logConfig = _loggerFactory.CreateLogger(LogCategories.Config);
        _logDiag = _loggerFactory.CreateLogger(LogCategories.Diag);

        _session = new SessionState(_loggerFactory.CreateLogger(LogCategories.Config));
        _autostart = new AutostartManager(_loggerFactory.CreateLogger(LogCategories.Config));

        _foreground = new ForegroundTracker(10);
        _foreground.Start();

        _selector = new TargetSelector(_foreground);
        _windowInfo = new WindowInfoProvider();
        _matcher = new WindowRuleMatcher();
        _focus = new FocusController();
        _sender = new KeySender(_loggerFactory.CreateLogger(LogCategories.Send));
        _programRunner = new ProgramRunner(baseDir, _loggerFactory.CreateLogger(LogCategories.Exec));
        _audio = new AudioDeviceManager(_loggerFactory.CreateLogger(LogCategories.Audio));
        _router = new Router(
            _loggerFactory,
            _selector,
            _windowInfo,
            _matcher,
            _focus,
            _sender,
            _programRunner,
            _audio);

        _hotkeys = new GlobalHotkeySource(_loggerFactory.CreateLogger(LogCategories.InputHotkey));
        _wmi = new WmiTriggerSource(_loggerFactory.CreateLogger(LogCategories.InputWmi), _session);

        _dispatcher = new TriggerDispatcher(ui, _loggerFactory, _hotkeys, _wmi);
        _dispatcher.TriggerFired += OnTrigger;

        _config = new AppConfig();
        _resolvedConfigPath = Path.Combine(_baseDir, "config.json");
    }

    public RingLogBuffer LogBuffer => _logBuffer;
    public ILoggerFactory LoggerFactory => _loggerFactory;
    public TraySection Tray => _config.Tray;
    public bool ForegroundTrackingEnabled => _foreground.IsEnabled;
    public bool AutostartEnabled => _config.App.Autostart.Enabled;

    public (string Id, string Title)[] UiPrograms =>
        _config.Programs
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (kvp.Key, string.IsNullOrWhiteSpace(kvp.Value.Title) ? kvp.Key : kvp.Value.Title))
            .ToArray();

    public void Start()
    {
        var expected = Environment.Is64BitProcess ? 40 : 28;
        var actual = System.Runtime.InteropServices.Marshal.SizeOf<MiHotKeyApp.Native.User32.INPUT>();
        if (actual != expected)
        {
            _logConfig.LogWarning("SendInput INPUT size mismatch expected={expected} actual={actual}", expected, actual);
        }

        ReloadConfig(initial: true);
        _dispatcher.Start();
    }

    public void ReloadConfig(bool initial = false)
    {
        try
        {
            var primaryPath = Path.Combine(_baseDir, "config.json");
            var loaded = _configStore.LoadFromPath(primaryPath);
            var resolved = _configStore.ResolveConfigPath(loaded.App.ConfigPath);

            if (!string.Equals(resolved, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                loaded = _configStore.LoadFromPath(resolved);
            }

            ApplyConfig(loaded, resolved);
            _foregroundTrackingOverride = null;
            if (loaded.Logging.ShowConfigPathsInLog)
            {
                _logConfig.LogInformation("reload ok path=\"{path}\" alt=\"{alt}\"", resolved, loaded.App.AltConfigPathHint);
            }
            else
            {
                _logConfig.LogInformation("reload ok");
            }
        }
        catch (Exception ex)
        {
            if (initial)
            {
                _logConfig.LogError(ex, "reload failed (using defaults)");
            }
            else
            {
                _logConfig.LogError(ex, "reload failed (keeping previous)");
            }
        }
    }

    private void ApplyConfig(AppConfig cfg, string resolvedPath)
    {
        _config = cfg;
        _resolvedConfigPath = resolvedPath;

        _autostart.Apply(cfg.App.Autostart.Enabled);

        var effectiveTrackingEnabled = _foregroundTrackingOverride ?? cfg.App.ForegroundTrackingEnabled;
        _foreground.Configure(effectiveTrackingEnabled, cfg.App.ForegroundHistorySize);

        _logProvider.UpdateConfig(cfg.Logging);
        _logBuffer.Resize(cfg.App.LogBufferSize);

        _dispatcher.ApplyConfig(cfg);
        _router.ApplyConfig(cfg);
    }

    private void OnTrigger(string triggerId)
    {
        _router.HandleTrigger(triggerId);
    }

    public bool RunProgram(string programId)
    {
        return _router.RunProgram(programId, context: "ui");
    }

    public void SetForegroundTrackingEnabled(bool enabled)
    {
        _foregroundTrackingOverride = enabled;
        _foreground.Configure(enabled, _config.App.ForegroundHistorySize);
        _logConfig.LogInformation("foregroundTracking enabled={enabled}", enabled ? 1 : 0);
    }

    public void SetAutostartEnabled(bool enabled)
    {
        if (string.IsNullOrWhiteSpace(_resolvedConfigPath))
        {
            _logConfig.LogWarning("autostart toggle ignored: config path is empty");
            return;
        }

        if (_configStore.TrySetAutostartEnabled(_resolvedConfigPath, enabled, out var error))
        {
            ReloadConfig();
            return;
        }

        _logConfig.LogError("autostart toggle failed enabled={enabled} path=\"{path}\" err=\"{err}\"", enabled ? 1 : 0, _resolvedConfigPath, error ?? "");
    }

    public void RunDiagnostics()
    {
        try
        {
            _logDiag.LogInformation("diagnostics start");
            LogForegroundTracking();
            LogTopLevelWindows();
            LogAudioDevices();
            _logDiag.LogInformation("diagnostics end");
        }
        catch (Exception ex)
        {
            _logDiag.LogError(ex, "diagnostics failed");
        }
    }

    private void LogForegroundTracking()
    {
        _logDiag.LogInformation("foregroundTracking enabled={enabled}", _foreground.IsEnabled ? 1 : 0);

        var (fg, prev) = _foreground.GetForegroundAndPrevious();
        if (fg != 0)
        {
            var wi = _windowInfo.GetInfo(fg);
            _logDiag.LogInformation("foreground hwnd=0x{hwnd:X} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.ProcessName, wi.ClassName, wi.Title);
        }

        if (prev != 0)
        {
            var wi = _windowInfo.GetInfo(prev);
            _logDiag.LogInformation("previous hwnd=0x{hwnd:X} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.ProcessName, wi.ClassName, wi.Title);
        }

        var history = _foreground.GetHistorySnapshot();
        _logDiag.LogInformation("foreground history count={count}", history.Length);
        foreach (var hwnd in history)
        {
            if (hwnd == 0)
            {
                continue;
            }

            var wi = _windowInfo.GetInfo(hwnd);
            _logDiag.LogInformation("history hwnd=0x{hwnd:X} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.ProcessName, wi.ClassName, wi.Title);
        }
    }

    private void LogTopLevelWindows()
    {
        var list = _windowInfo.GetTopLevelWindows()
            .Select(hwnd => _windowInfo.GetInfo(hwnd))
            .Where(w => w.Hwnd != 0)
            .ToList();

        if (!_config.App.Diagnostics.SortByTabOrder)
        {
            list = list
                .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        _logDiag.LogInformation("top windows count={count} order={order}", list.Count, _config.App.Diagnostics.SortByTabOrder ? "tab" : "sorted");
        foreach (var wi in list)
        {
            _logDiag.LogInformation("top hwnd=0x{hwnd:X} proc={proc} cls={cls} title=\"{title}\"", (nuint)wi.Hwnd, wi.ProcessName, wi.ClassName, wi.Title);
        }
    }

    private void LogAudioDevices()
    {
        var devices = _audio.GetDevicesSnapshot();
        _logDiag.LogInformation("audio devices count={count}", devices.Length);
        foreach (var d in devices)
        {
            _logDiag.LogInformation(
                "audio flow={flow} id=\"{id}\" name=\"{name}\" defaultConsole={console} defaultMultimedia={multi} defaultComms={comms}",
                d.Flow,
                d.Id,
                d.Name,
                d.IsDefaultConsole ? 1 : 0,
                d.IsDefaultMultimedia ? 1 : 0,
                d.IsDefaultCommunications ? 1 : 0);

            _logDiag.LogInformation(
                "audio config flow={flow} scope=Single role=Communications deviceId=\"{id}\" action=ToggleMute",
                d.Flow,
                d.Id);
        }
    }

    public void Dispose()
    {
        _dispatcher.TriggerFired -= OnTrigger;
        _dispatcher.Dispose();
        _foreground.Dispose();
        _session.Dispose();
        _loggerFactory.Dispose();
    }
}
