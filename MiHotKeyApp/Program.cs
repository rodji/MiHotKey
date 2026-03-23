namespace MiHotKeyApp;

using MiHotKeyApp.CommandLine;
using MiHotKeyApp.Ipc;
using MiHotKeyApp.Logging;
using MiHotKeyApp.Routing;
using MiHotKeyApp.UI;
using Microsoft.Extensions.Logging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var launchCommand = AppLaunchCommand.Parse(args);
        if (launchCommand.Kind == AppLaunchCommandKind.Invalid)
        {
            return Exit(launchCommand.ExitCode, launchCommand.Message, isError: true);
        }

        using var instanceLock = SingleInstanceLock.Create(AppContext.BaseDirectory);

        if (launchCommand.Kind == AppLaunchCommandKind.CallRoute)
        {
            if (!instanceLock.IsPrimary)
            {
                var response = AppCommandClient.Send(instanceLock.PipeName, launchCommand.ToIpcRequest());
                return Exit(response.ExitCode, response.Output, isError: response.ExitCode != AppExitCodes.Success);
            }

            // TODO: Bootstrap-on-demand could start the tray/runtime in the background,
            // wait until the named pipe server is ready, and then resend the command.
            // For now we fail fast so one-shot CLI usage does not unexpectedly leave
            // behind a new resident tray instance with partially initialized state.
            return Exit(
                AppExitCodes.InstanceNotRunning,
                "MiHotKey is not running. Planned follow-up: optionally bootstrap the tray instance, wait for IPC readiness, and resend the command.",
                isError: true);
        }

        if (!instanceLock.IsPrimary)
        {
            return AppExitCodes.Success;
        }

        ApplicationConfiguration.Initialize();

        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        using var runtime = new AppRuntime(AppContext.BaseDirectory, ui);
        runtime.Start();
        var logIpc = runtime.LoggerFactory.CreateLogger(LogCategories.Ipc);
        using var ipcServer = new AppCommandPipeServer(
            instanceLock.PipeName,
            request => DispatchIpcCommandAsync(request, runtime, ui, logIpc),
            logIpc);
        ipcServer.Start();

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
        tray.DiagnosticsRequested += () => runtime.RunDiagnostics();
        tray.ProgramRunRequested += programId => runtime.RunProgram(programId);

        Application.Run(tray);
        return AppExitCodes.Success;
    }

    private static Task<AppCommandResponse> DispatchIpcCommandAsync(
        AppCommandRequest request,
        AppRuntime runtime,
        SynchronizationContext ui,
        ILogger logger)
    {
        var tcs = new TaskCompletionSource<AppCommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(_ =>
        {
            try
            {
                tcs.TrySetResult(HandleIpcCommand(request, runtime, logger));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ipc command failed command={command} target={target}", request.Command ?? "", request.Target ?? "");
                tcs.TrySetResult(new AppCommandResponse(AppExitCodes.InternalError, "IPC command failed."));
            }
        }, null);

        return tcs.Task;
    }

    private static AppCommandResponse HandleIpcCommand(AppCommandRequest request, AppRuntime runtime, ILogger logger)
    {
        if (!string.Equals(request.Command, AppCommandRequest.CallRouteCommand, StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandResponse(AppExitCodes.InvalidArguments, $"Unsupported IPC command: {request.Command}");
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            return new AppCommandResponse(AppExitCodes.InvalidArguments, "Route name is required.");
        }

        logger.LogInformation("ipc command={command} route={route}", request.Command, request.Target);
        var result = runtime.InvokeRoute(request.Target, context: "ipc");
        return MapRouteResult(result);
    }

    private static AppCommandResponse MapRouteResult(RouteInvocationResult result)
    {
        return result.Status switch
        {
            RouteInvocationStatus.Success => new AppCommandResponse(AppExitCodes.Success, result.Message),
            RouteInvocationStatus.MissingTrigger => new AppCommandResponse(AppExitCodes.RouteNotFound, result.Message),
            RouteInvocationStatus.NoMatch => new AppCommandResponse(AppExitCodes.RouteNoMatch, result.Message),
            RouteInvocationStatus.ExecutionFailed => new AppCommandResponse(AppExitCodes.RouteExecutionFailed, result.Message),
            _ => new AppCommandResponse(AppExitCodes.InternalError, result.Message),
        };
    }

    private static int Exit(int exitCode, string? text, bool isError)
    {
        WriteConsole(text, isError);
        return exitCode;
    }

    private static void WriteConsole(string? text, bool isError)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            if (isError)
            {
                Console.Error.WriteLine(text);
                return;
            }

            Console.WriteLine(text);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
