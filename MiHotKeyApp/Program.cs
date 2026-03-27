namespace MiHotKeyApp;

using MiHotKeyApp.CommandLine;
using MiHotKeyApp.Ipc;
using MiHotKeyApp.Logging;
using MiHotKeyApp.Routing;
using MiHotKeyApp.UI;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

internal static class Program
{
    private const int BootstrapTimeoutMs = 10_000;
    private const int BootstrapRetryDelayMs = 100;

    [STAThread]
    private static int Main(string[] args)
    {
        var launchCommand = AppLaunchCommand.Parse(args);
        if (launchCommand.Kind == AppLaunchCommandKind.Invalid)
        {
            return Exit(launchCommand.ExitCode, launchCommand.Message, isError: true);
        }

        if (launchCommand.Kind == AppLaunchCommandKind.CallRoute)
        {
            return ExecuteCallRoute(launchCommand);
        }

        using var instanceLock = SingleInstanceLock.Create(AppContext.BaseDirectory);
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
            instanceLock.LoopbackPort,
            instanceLock.AuthToken,
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
        if (string.Equals(request.Command, AppCommandRequest.PingCommand, StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandResponse(AppExitCodes.Success, "ready");
        }

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

    private static int ExecuteCallRoute(AppLaunchCommand launchCommand)
    {
        var loopbackPort = AppIpcNames.GetLoopbackPort(AppContext.BaseDirectory);
        var authToken = AppIpcNames.GetAuthToken(AppContext.BaseDirectory);
        var request = launchCommand.ToIpcRequest(authToken);

        var sendResult = AppCommandClient.Send(loopbackPort, request);
        if (!sendResult.IsTransportFailure)
        {
            return Exit(sendResult.Response.ExitCode, sendResult.Response.Output, isError: sendResult.Response.ExitCode != AppExitCodes.Success);
        }

        using var bootstrapProcess = TryStartResidentProcess();
        if (!WaitForResidentReady(loopbackPort, authToken, bootstrapProcess))
        {
            return Exit(
                AppExitCodes.ResidentUnavailable,
                "MiHotKey resident is unavailable after bootstrap attempt.",
                isError: true);
        }

        var finalResult = AppCommandClient.Send(loopbackPort, request);
        return Exit(finalResult.Response.ExitCode, finalResult.Response.Output, isError: finalResult.Response.ExitCode != AppExitCodes.Success);
    }

    private static Process? TryStartResidentProcess()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            };

            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    private static bool WaitForResidentReady(int loopbackPort, string authToken, Process? bootstrapProcess)
    {
        var startedAt = Stopwatch.StartNew();
        while (startedAt.ElapsedMilliseconds < BootstrapTimeoutMs)
        {
            var pingResult = AppCommandClient.Ping(loopbackPort, authToken);
            if (pingResult.Response.ExitCode == AppExitCodes.Success)
            {
                return true;
            }

            if (bootstrapProcess is not null)
            {
                try
                {
                    _ = bootstrapProcess.HasExited;
                }
                catch
                {
                }
            }

            Thread.Sleep(BootstrapRetryDelayMs);
        }

        return false;
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
