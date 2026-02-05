namespace MiHotKeyApp.Execution;

using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

internal sealed class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "MiHotKey";

    private readonly ILogger _logger;
    private readonly string _valueName;

    public AutostartManager(ILogger logger, string? valueName = null)
    {
        _logger = logger;
        _valueName = string.IsNullOrWhiteSpace(valueName) ? DefaultValueName : valueName.Trim();
    }

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                _logger.LogWarning("autostart key missing hkcu={key}", RunKeyPath);
                return;
            }

            if (!enabled)
            {
                if (key.GetValue(_valueName) is not null)
                {
                    key.DeleteValue(_valueName, throwOnMissingValue: false);
                    _logger.LogInformation("autostart disabled name={name}", _valueName);
                }
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                exe = Application.ExecutablePath;
            }

            var cmd = $"\"{exe}\"";
            var existing = key.GetValue(_valueName) as string;
            if (!string.Equals(existing, cmd, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(_valueName, cmd, RegistryValueKind.String);
                _logger.LogInformation("autostart enabled name={name} cmd=\"{cmd}\"", _valueName, cmd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "autostart apply failed enabled={enabled}", enabled ? 1 : 0);
        }
    }
}
