namespace MiHotKeyApp.UI;

using MiHotKeyApp.Logging;
using Microsoft.Extensions.Logging;

internal sealed class LogWindowPresenter : IDisposable
{
    private readonly RingLogBuffer _buffer;
    private readonly LogWindow _window;

    public LogWindowPresenter(RingLogBuffer buffer)
    {
        _buffer = buffer;
        _window = new LogWindow();
        _window.FormClosing += (_, e) =>
        {
            e.Cancel = true;
            _window.Hide();
        };

        _window.RefreshRequested += Refresh;
        _window.LevelChanged += _ => Refresh();
        _buffer.Updated += OnUpdated;
    }

    public void Show()
    {
        if (_window.Visible)
        {
            _window.Activate();
            return;
        }

        Refresh();
        _window.Show();
        _window.Activate();
    }

    public void Refresh()
    {
        var entries = _buffer.Snapshot(_window.MinLevel);
        var text = string.Join(Environment.NewLine, entries.Select(e => e.Line));
        _window.SetText(text);
    }

    private void OnUpdated()
    {
        if (_window.IsDisposed || !_window.Visible)
        {
            return;
        }

        if (_window.InvokeRequired)
        {
            _window.BeginInvoke(Refresh);
            return;
        }

        Refresh();
    }

    public void Dispose()
    {
        _buffer.Updated -= OnUpdated;
        if (!_window.IsDisposed)
        {
            _window.Close();
            _window.Dispose();
        }
    }
}

