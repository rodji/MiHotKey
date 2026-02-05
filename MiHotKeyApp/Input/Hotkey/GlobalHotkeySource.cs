namespace MiHotKeyApp.Input.Hotkey;

using System.Runtime.InteropServices;
using System.Windows.Forms;
using MiHotKeyApp.Config;
using Microsoft.Extensions.Logging;

internal sealed class GlobalHotkeySource : NativeWindow, ITriggerSource
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_NOREPEAT = 0x4000;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private int _nextId = 1;
    private readonly Dictionary<int, (string TriggerId, string KeysText)> _idToTrigger = [];
    private readonly List<int> _registeredIds = [];

    public GlobalHotkeySource(ILogger logger)
    {
        _logger = logger;
        CreateHandle(new CreateParams());
    }

    public event Action<string, string>? HotkeyPressed;

    public void SetHotkeys(IEnumerable<HotkeyInputConfig> hotkeys)
    {
        lock (_gate)
        {
            UnregisterAllUnsafe();
            foreach (var hk in hotkeys)
            {
                var def = HotkeyParser.Parse(hk.Keys);
                var id = _nextId++;
                var ok = RegisterHotKey(Handle, id, (int)def.Modifiers | MOD_NOREPEAT, (int)def.Key);
                if (!ok)
                {
                    _logger.LogWarning("Failed to register hotkey trigger={trigger} keys={keys}", hk.Id, hk.Keys);
                    continue;
                }

                _registeredIds.Add(id);
                _idToTrigger[id] = (hk.Id, hk.Keys);
            }
        }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            var id = (int)m.WParam;
            if (_idToTrigger.TryGetValue(id, out var info))
            {
                HotkeyPressed?.Invoke(info.TriggerId, info.KeysText);
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            UnregisterAllUnsafe();
        }

        DestroyHandle();
    }

    private void UnregisterAllUnsafe()
    {
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(Handle, id);
        }

        _registeredIds.Clear();
        _idToTrigger.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
