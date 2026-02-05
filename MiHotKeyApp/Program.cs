namespace MiHotKeyApp;

using MiHotKeyApp.Logging;
using MiHotKeyApp.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var ui = SynchronizationContext.Current ?? new SynchronizationContext();

        using var runtime = new AppRuntime(AppContext.BaseDirectory, ui);
        runtime.Start();

        using var tray = new TrayAppContext(runtime.LogBuffer, runtime.LoggerFactory.CreateLogger(LogCategories.Error));
        tray.ApplyTrayConfig(runtime.Tray);
        tray.SetForegroundTrackingChecked(runtime.ForegroundTrackingEnabled);
        tray.SetAutostartChecked(runtime.AutostartEnabled);
        tray.ApplyPrograms(runtime.UiPrograms);
        tray.ReloadConfigRequested += () =>
        {
            runtime.ReloadConfig();
            tray.ApplyTrayConfig(runtime.Tray);
            tray.SetForegroundTrackingChecked(runtime.ForegroundTrackingEnabled);
            tray.SetAutostartChecked(runtime.AutostartEnabled);
            tray.ApplyPrograms(runtime.UiPrograms);
        };
        tray.ForegroundTrackingToggled += enabled => runtime.SetForegroundTrackingEnabled(enabled);
        tray.AutostartToggled += enabled =>
        {
            runtime.SetAutostartEnabled(enabled);
            tray.SetAutostartChecked(runtime.AutostartEnabled);
        };
        tray.ProgramRunRequested += programId => runtime.RunProgram(programId);

        Application.Run(tray);
    }
}
