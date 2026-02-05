namespace MiHotKeyApp.Execution;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Windows.Forms;

internal sealed class SessionState : IDisposable
{
    private readonly ILogger _logger;
    private volatile bool _isLocked;

    public SessionState(ILogger logger)
    {
        _logger = logger;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public bool IsLocked => _isLocked;

    public bool IsRemoteSession => SystemInformation.TerminalServerSession;

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _isLocked = true;
            _logger.LogInformation("session locked remote={remote}", IsRemoteSession ? 1 : 0);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _isLocked = false;
            _logger.LogInformation("session unlocked remote={remote}", IsRemoteSession ? 1 : 0);
        }
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}

