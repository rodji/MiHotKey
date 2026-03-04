namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Config;
using MiHotKeyApp.Execution;
using MiHotKeyApp.Native;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

internal sealed class ForegroundTrackingController : IDisposable
{
    private const int RefreshMs = 2000;
    private static readonly TimeSpan SmartIdleThreshold = TimeSpan.FromMinutes(1);

    private readonly ForegroundTracker _tracker;
    private readonly SessionState _session;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;

    private AppSection _app = new();
    private bool? _override;
    private bool _appliedEnabled;
    private int _appliedCapacity = -1;

    public ForegroundTrackingController(ForegroundTracker tracker, SessionState session, ILogger logger)
    {
        _tracker = tracker;
        _session = session;
        _logger = logger;
        _timer = new System.Threading.Timer(_ => Refresh("timer"), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsEnabled => _tracker.IsEnabled;
    public int Depth => _app.TargetSearchDepth;
    public ForegroundTrackingMode Mode => _app.ToggleForegroundTracking;

    public void Start()
    {
        _timer.Change(RefreshMs, RefreshMs);
    }

    public void ApplyConfig(AppSection app)
    {
        _app = app;
        _override = null;
        Refresh("config", force: true);
    }

    public void SetOverride(bool enabled)
    {
        _override = enabled;
        Refresh("ui", force: true);
    }

    public ForegroundTrackingStatus GetStatus()
    {
        var screensaver = IsScreenSaverRunning();
        var idle = GetUserIdleTime();
        return new ForegroundTrackingStatus(
            IsEnabled: _tracker.IsEnabled,
            Depth: _app.TargetSearchDepth,
            Mode: _app.ToggleForegroundTracking,
            IsLocked: _session.IsLocked,
            IsScreenSaverRunning: screensaver,
            Idle: idle);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void Refresh(string source, bool force = false)
    {
        try
        {
            var depth = _app.TargetSearchDepth;
            var enabled = ShouldTrack(depth);
            var capacity = GetCapacity(depth);

            lock (_gate)
            {
                if (!force
                    && _appliedEnabled == enabled
                    && _appliedCapacity == capacity)
                {
                    return;
                }

                _tracker.Configure(enabled, capacity);
                _appliedEnabled = enabled;
                _appliedCapacity = capacity;
            }

            var status = GetStatus();
            _logger.LogInformation(
                "foregroundTracking source={source} enabled={enabled} depth={depth} mode={mode} override={override} locked={locked} screensaver={screensaver} idleSec={idleSec}",
                source,
                enabled ? 1 : 0,
                depth,
                _app.ToggleForegroundTracking,
                _override.HasValue ? (_override.Value ? 1 : 0) : -1,
                status.IsLocked ? 1 : 0,
                status.IsScreenSaverRunning ? 1 : 0,
                (int)status.Idle.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "foregroundTracking refresh failed source={source}", source);
        }
    }

    private bool ShouldTrack(int depth)
    {
        if (depth <= 1)
        {
            return false;
        }

        if (_override.HasValue)
        {
            return _override.Value;
        }

        return _app.ToggleForegroundTracking switch
        {
            ForegroundTrackingMode.Off => false,
            ForegroundTrackingMode.AlwaysOn => true,
            ForegroundTrackingMode.Smart => !ShouldSuspendInSmartMode(),
            _ => true,
        };
    }

    private static int GetCapacity(int depth)
    {
        if (depth <= 1)
        {
            return 2;
        }

        return Math.Min(1000, Math.Max(8, depth * 4));
    }

    private bool ShouldSuspendInSmartMode()
    {
        if (_session.IsLocked)
        {
            return true;
        }

        if (IsScreenSaverRunning())
        {
            return true;
        }

        return GetUserIdleTime() >= SmartIdleThreshold;
    }

    private static bool IsScreenSaverRunning()
    {
        return User32.SystemParametersInfo(User32.SPI_GETSCREENSAVERRUNNING, 0, out var running, 0) && running;
    }

    private static TimeSpan GetUserIdleTime()
    {
        var info = new User32.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<User32.LASTINPUTINFO>(),
        };

        if (!User32.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var now = unchecked((uint)Environment.TickCount);
        var idleMs = unchecked(now - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}

internal readonly record struct ForegroundTrackingStatus(
    bool IsEnabled,
    int Depth,
    ForegroundTrackingMode Mode,
    bool IsLocked,
    bool IsScreenSaverRunning,
    TimeSpan Idle);
