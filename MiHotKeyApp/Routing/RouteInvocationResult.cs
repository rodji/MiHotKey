namespace MiHotKeyApp.Routing;

internal enum RouteInvocationStatus
{
    Success = 0,
    MissingTrigger = 1,
    NoMatch = 2,
    ExecutionFailed = 3,
}

internal readonly record struct RouteInvocationResult(RouteInvocationStatus Status, string Message)
{
    public bool IsSuccess => Status == RouteInvocationStatus.Success;

    public static RouteInvocationResult Success(string message) => new(RouteInvocationStatus.Success, message);

    public static RouteInvocationResult MissingTrigger(string triggerId) =>
        new(RouteInvocationStatus.MissingTrigger, $"route not found: {triggerId}");

    public static RouteInvocationResult NoMatch(string triggerId) =>
        new(RouteInvocationStatus.NoMatch, $"route matched no target: {triggerId}");

    public static RouteInvocationResult ExecutionFailed(string message) =>
        new(RouteInvocationStatus.ExecutionFailed, message);
}
