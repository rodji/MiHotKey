namespace MiHotKeyApp.CommandLine;

internal static class AppExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 2;

    public const int ResidentUnavailable = 10;
    public const int IpcUnavailable = 11;
    public const int InternalError = 12;

    public const int RouteNotFound = 20;
    public const int RouteNoMatch = 21;
    public const int RouteExecutionFailed = 22;
}
