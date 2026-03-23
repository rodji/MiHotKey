namespace MiHotKeyApp.CommandLine;

using MiHotKeyApp.Ipc;

internal enum AppLaunchCommandKind
{
    RunApp = 0,
    CallRoute = 1,
    Invalid = 2,
}

internal sealed record AppLaunchCommand(AppLaunchCommandKind Kind, string? RouteName, int ExitCode, string? Message)
{
    public static AppLaunchCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new AppLaunchCommand(AppLaunchCommandKind.RunApp, RouteName: null, AppExitCodes.Success, Message: null);
        }

        if (args.Length == 3
            && string.Equals(args[0], "call", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], "route", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(args[2]))
        {
            return new AppLaunchCommand(AppLaunchCommandKind.CallRoute, args[2].Trim(), AppExitCodes.Success, Message: null);
        }

        return new AppLaunchCommand(
            AppLaunchCommandKind.Invalid,
            RouteName: null,
            AppExitCodes.InvalidArguments,
            "Usage: MiHotKeyApp.exe call route <name>");
    }

    public AppCommandRequest ToIpcRequest()
    {
        return new AppCommandRequest(AppCommandRequest.CallRouteCommand, RouteName ?? "");
    }
}
