namespace MiHotKeyApp.UI;

using MiHotKeyApp.Config;
using MiHotKeyApp.Logging;
using Microsoft.Extensions.Logging;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _reload;
    private readonly ToolStripMenuItem _showLog;
    private readonly ToolStripMenuItem _runPrograms;
    private readonly ToolStripMenuItem _toggleForegroundTracking;
    private readonly ToolStripMenuItem _exit;
    private readonly LogWindowPresenter _logPresenter;
    private readonly ILogger _logger;

    public TrayAppContext(RingLogBuffer logBuffer, ILogger logger)
    {
        _logger = logger;
        _logPresenter = new LogWindowPresenter(logBuffer);

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "MiHotKey",
        };

        var menu = new ContextMenuStrip();

        _reload = new ToolStripMenuItem("Reload config");
        _reload.Click += (_, __) => ReloadConfigRequested?.Invoke();
        menu.Items.Add(_reload);

        _showLog = new ToolStripMenuItem("Show log");
        _showLog.Click += (_, __) => _logPresenter.Show();
        menu.Items.Add(_showLog);

        _runPrograms = new ToolStripMenuItem("Run");
        menu.Items.Add(_runPrograms);

        _toggleForegroundTracking = new ToolStripMenuItem("Foreground tracking")
        {
            CheckOnClick = true,
            Checked = true,
        };
        _toggleForegroundTracking.Click += (_, __) => ForegroundTrackingToggled?.Invoke(_toggleForegroundTracking.Checked);
        menu.Items.Add(_toggleForegroundTracking);

        menu.Items.Add(new ToolStripSeparator());

        _exit = new ToolStripMenuItem("Exit");
        _exit.Click += (_, __) => ExitThread();
        menu.Items.Add(_exit);

        _tray.ContextMenuStrip = menu;
    }

    public event Action? ReloadConfigRequested;
    public event Action<bool>? ForegroundTrackingToggled;
    public event Action<string>? ProgramRunRequested;

    public void ApplyTrayConfig(TraySection tray)
    {
        _reload.Visible = tray.ReloadConfig;
        _showLog.Visible = tray.ShowLog;
        _exit.Visible = tray.Exit;
        _toggleForegroundTracking.Visible = tray.ToggleForegroundTracking;
    }

    public void ApplyPrograms((string Id, string Title)[] programs)
    {
        _runPrograms.DropDownItems.Clear();

        foreach (var (id, title) in programs)
        {
            var item = new ToolStripMenuItem(title);
            item.Click += (_, __) => ProgramRunRequested?.Invoke(id);
            _runPrograms.DropDownItems.Add(item);
        }

        _runPrograms.Visible = programs.Length > 0;
    }

    public void SetForegroundTrackingChecked(bool enabled)
    {
        _toggleForegroundTracking.Checked = enabled;
    }

    protected override void ExitThreadCore()
    {
        try
        {
            _logPresenter.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose tray UI");
        }

        base.ExitThreadCore();
    }
}
