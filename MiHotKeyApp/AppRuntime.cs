namespace MiHotKeyApp;

using MiHotKeyApp.Config;
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
    private readonly ConfigLoader _loader;
    private readonly RingLogBuffer _logBuffer;
    private readonly RingBufferLoggerProvider _logProvider;
    private readonly ILoggerFactory _loggerFactory;

    private readonly ILogger _logConfig;

    private readonly SessionState _session;
    private readonly AutostartManager _autostart;

    private readonly ForegroundTracker _foreground;
    private readonly TargetSelector _selector;
    private readonly WindowInfoProvider _windowInfo;
    private readonly WindowRuleMatcher _matcher;
    private readonly FocusController _focus;
    private readonly KeySender _sender;
    private readonly ProgramRunner _programRunner;
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
        _loader = new ConfigLoader(baseDir);

        _logBuffer = new RingLogBuffer(100);
        _logProvider = new RingBufferLoggerProvider(_logBuffer, new LoggingSection());
        _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(_logProvider);
        });

        _logConfig = _loggerFactory.CreateLogger(LogCategories.Config);

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
        _router = new Router(
            _loggerFactory,
            _selector,
            _windowInfo,
            _matcher,
            _focus,
            _sender,
            _programRunner);

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
            var loaded = _loader.LoadFromPath(primaryPath);
            var resolved = _loader.ResolveConfigPath(loaded.App.ConfigPath);

            if (!string.Equals(resolved, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                loaded = _loader.LoadFromPath(resolved);
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

    public void Dispose()
    {
        _dispatcher.TriggerFired -= OnTrigger;
        _dispatcher.Dispose();
        _foreground.Dispose();
        _session.Dispose();
        _loggerFactory.Dispose();
    }
}
