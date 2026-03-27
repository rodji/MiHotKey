namespace MiHotKeyApp.Ipc;

internal sealed record AppCommandRequest(string Command, string Target, string Token)
{
    public const string CallRouteCommand = "call-route";
    public const string PingCommand = "ping";
}

internal sealed record AppCommandResponse(int ExitCode, string? Output);
