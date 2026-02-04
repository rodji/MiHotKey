namespace MiHotKeyApp.Input;

using MiHotKeyApp.Config;
using MiHotKeyApp.Logging;
using MiHotKeyApp.Input.Hotkey;
using MiHotKeyApp.Input.Wmi;
using Microsoft.Extensions.Logging;

internal sealed class TriggerDispatcher : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly ILogger _logHotkey;
    private readonly ILogger _logWmi;
    private readonly ILogger _logTrigger;
    private readonly GlobalHotkeySource _hotkeys;
    private readonly WmiTriggerSource _wmi;

    private Dictionary<string, string[]> _eventToTriggers = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    public TriggerDispatcher(
        SynchronizationContext ui,
        ILoggerFactory loggerFactory,
        GlobalHotkeySource hotkeys,
        WmiTriggerSource wmi)
    {
        _ui = ui;
        _logHotkey = loggerFactory.CreateLogger(LogCategories.InputHotkey);
        _logWmi = loggerFactory.CreateLogger(LogCategories.InputWmi);
        _logTrigger = loggerFactory.CreateLogger(LogCategories.Trigger);
        _hotkeys = hotkeys;
        _wmi = wmi;

        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _wmi.EventReceived += OnWmiEventReceived;
    }

    public event Action<string>? TriggerFired;

    public void ApplyConfig(AppConfig cfg)
    {
        _eventToTriggers = BuildEventToTriggers(cfg.Bindings);
        _hotkeys.SetHotkeys(cfg.Inputs.Hotkeys);
        if (_started)
        {
            _wmi.Stop();
        }

        _wmi.SetSubscriptions(cfg.Inputs.Wmi);

        if (_started)
        {
            _wmi.Start();
        }
    }

    public void Start()
    {
        _hotkeys.Start();
        _wmi.Start();
        _started = true;
    }

    public void Dispose()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _wmi.EventReceived -= OnWmiEventReceived;
        _hotkeys.Dispose();
        _wmi.Dispose();
    }

    private static Dictionary<string, string[]> BuildEventToTriggers(Dictionary<string, string[]> bindings)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (triggerId, events) in bindings)
        {
            foreach (var ev in events)
            {
                if (!map.TryGetValue(ev, out var list))
                {
                    list = [];
                    map[ev] = list;
                }

                list.Add(triggerId);
            }
        }

        return map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private void OnHotkeyPressed(string triggerId, string keys)
    {
        _logHotkey.LogInformation("id={trigger} keys={keys}", triggerId, keys);
        FireTrigger(triggerId);
    }

    private void OnWmiEventReceived(string sourceId, int code, string mappedEvent)
    {
        _logWmi.LogInformation("src={src} code={code} mapped={mapped}", sourceId, code, mappedEvent);

        if (!_eventToTriggers.TryGetValue(mappedEvent, out var triggers) || triggers.Length == 0)
        {
            return;
        }

        foreach (var triggerId in triggers)
        {
            FireTrigger(triggerId);
        }
    }

    private void FireTrigger(string triggerId)
    {
        _ui.Post(_ =>
        {
            _logTrigger.LogInformation("id={trigger}", triggerId);
            TriggerFired?.Invoke(triggerId);
        }, null);
    }
}
