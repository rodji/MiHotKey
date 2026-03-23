namespace MiHotKeyApp.Ipc;

internal sealed record AppCommandRequest(string Command, string Target)
{
    public const string CallRouteCommand = "call-route";
}

internal sealed record AppCommandResponse(int ExitCode, string? Output);
