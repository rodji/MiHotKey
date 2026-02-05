namespace MiHotKeyApp.Input.Wmi;

using System.Globalization;
using System.Management;
using MiHotKeyApp.Config;
using MiHotKeyApp.Execution;
using MiHotKeyApp.Logging;
using Microsoft.Extensions.Logging;

internal sealed class WmiTriggerSource : ITriggerSource
{
    private readonly ILogger _logger;
    private readonly SessionState _session;
    private readonly object _gate = new();

    private readonly List<Watcher> _watchers = [];
    private WmiInputConfig[] _configs = [];

    public WmiTriggerSource(ILogger logger, SessionState session)
    {
        _logger = logger;
        _session = session;
    }

    public event Action<string, int, string>? EventReceived;

    public void SetSubscriptions(IEnumerable<WmiInputConfig> wmi)
    {
        lock (_gate)
        {
            _configs = wmi.ToArray();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            StopUnsafe();
            foreach (var cfg in _configs)
            {
                try
                {
                    var scope = new ManagementScope($@"\\.\{cfg.Namespace}");
                    scope.Connect();

                    var query = new WqlEventQuery(cfg.Query);
                    var watcher = new ManagementEventWatcher(scope, query);
                    var gate = new RepeatGate(cfg.DebounceMs);

                    watcher.EventArrived += (_, e) => OnArrived(cfg, gate, e.NewEvent);
                    watcher.Start();
                    _watchers.Add(new Watcher(watcher));

                    _logger.LogInformation("{id} started ns={ns}", cfg.Id, cfg.Namespace);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{id} failed to start", cfg.Id);
                }
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopUnsafe();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void StopUnsafe()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.Dispose();
            }
            catch
            {
            }
        }

        _watchers.Clear();
    }

    private void OnArrived(WmiInputConfig cfg, RepeatGate gate, ManagementBaseObject ev)
    {
        try
        {
            foreach (var (prop, expected) in cfg.Where)
            {
                var actual = ev.Properties[prop]?.Value?.ToString();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var bytes = TryGetByteArray(ev.Properties[cfg.Extract.Prop]?.Value);
            if (bytes is null || bytes.Length <= cfg.Extract.Index)
            {
                return;
            }

            var code = bytes[cfg.Extract.Index];
            var key = code.ToString(CultureInfo.InvariantCulture);
            if (!cfg.Map.TryGetValue(key, out var mappedEvent))
            {
                return;
            }

            if (cfg.RepeatHandling.Equals("firstDownOnlyUntilUp", StringComparison.OrdinalIgnoreCase))
            {
                if (!gate.ShouldAccept(mappedEvent))
                {
                    return;
                }
            }

            var policy = cfg.SessionPolicy;
            if (cfg.SessionPolicyByEvent.TryGetValue(mappedEvent, out var perEvent))
            {
                policy = perEvent;
            }

            if (!IsAllowed(policy))
            {
                _logger.LogDebug(
                    "drop src={src} code={code} event={event} policy={policy} locked={locked} remote={remote}",
                    cfg.Id,
                    code,
                    mappedEvent,
                    policy,
                    _session.IsLocked ? 1 : 0,
                    _session.IsRemoteSession ? 1 : 0);
                return;
            }

            EventReceived?.Invoke(cfg.Id, code, mappedEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMI event processing failed");
        }
    }

    private bool IsAllowed(InputSessionPolicy policy)
    {
        return policy switch
        {
            InputSessionPolicy.Any => true,
            InputSessionPolicy.RequireUnlocked => !_session.IsLocked,
            InputSessionPolicy.RequireLocalSession => !_session.IsRemoteSession,
            InputSessionPolicy.RequireUnlockedLocalSession => !_session.IsLocked && !_session.IsRemoteSession,
            _ => true,
        };
    }

    private static byte[]? TryGetByteArray(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return bytes;
        }

        if (value is ushort[] ushorts)
        {
            var b = new byte[ushorts.Length];
            for (var i = 0; i < ushorts.Length; i++)
            {
                b[i] = (byte)ushorts[i];
            }
            return b;
        }

        if (value is Array arr && arr.Length > 0 && arr.GetValue(0) is IConvertible)
        {
            var b = new byte[arr.Length];
            for (var i = 0; i < arr.Length; i++)
            {
                b[i] = Convert.ToByte(arr.GetValue(i), CultureInfo.InvariantCulture);
            }
            return b;
        }

        return null;
    }

    private sealed class Watcher : IDisposable
    {
        private readonly ManagementEventWatcher _watcher;

        public Watcher(ManagementEventWatcher watcher)
        {
            _watcher = watcher;
        }

        public void Dispose()
        {
            try
            {
                _watcher.Stop();
            }
            catch
            {
            }

            _watcher.Dispose();
        }
    }
}
