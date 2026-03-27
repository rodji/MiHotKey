namespace MiHotKeyApp.Ipc;

internal enum AppCommandClientStatus
{
    Success = 0,
    TransportFailure = 1,
    ProtocolFailure = 2,
}

internal readonly record struct AppCommandClientResult(AppCommandClientStatus Status, AppCommandResponse Response)
{
    public bool IsTransportFailure => Status == AppCommandClientStatus.TransportFailure;
}
