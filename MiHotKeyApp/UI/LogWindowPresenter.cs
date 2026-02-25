namespace MiHotKeyApp.UI;

using MiHotKeyApp.Logging;
using Microsoft.Extensions.Logging;

internal sealed class LogWindowPresenter : IDisposable
{
    private const int VisibleLineLimit = 100;
    private readonly RingLogBuffer _buffer;
    private readonly LogWindow _window;
    private int _refreshQueued;

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
        System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);

        if (_window.IsDisposed)
        {
            return;
        }

        var entries = _buffer.SnapshotTail(_window.MinLevel, VisibleLineLimit);
        var text = string.Join(Environment.NewLine, entries.Select(e => e.Line));
        _window.SetText(text);
    }

    private void OnUpdated()
    {
        if (_window.IsDisposed || !_window.Visible)
        {
            return;
        }

        if (System.Threading.Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (_window.InvokeRequired)
        {
            _window.BeginInvoke((Action)Refresh);
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
