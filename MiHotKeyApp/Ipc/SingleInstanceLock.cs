namespace MiHotKeyApp.Ipc;

internal sealed class SingleInstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstanceLock(Mutex mutex, bool isPrimary, int loopbackPort, string authToken)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        LoopbackPort = loopbackPort;
        AuthToken = authToken;
    }

    public bool IsPrimary { get; }

    public int LoopbackPort { get; }

    public string AuthToken { get; }

    public static SingleInstanceLock Create(string baseDir)
    {
        var mutex = new Mutex(initiallyOwned: true, AppIpcNames.GetMutexName(baseDir), out var createdNew);
        return new SingleInstanceLock(
            mutex,
            createdNew,
            AppIpcNames.GetLoopbackPort(baseDir),
            AppIpcNames.GetAuthToken(baseDir));
    }

    public void Dispose()
    {
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }
}
