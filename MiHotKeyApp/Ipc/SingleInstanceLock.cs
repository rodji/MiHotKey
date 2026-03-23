namespace MiHotKeyApp.Ipc;

internal sealed class SingleInstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstanceLock(Mutex mutex, bool isPrimary, string pipeName)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        PipeName = pipeName;
    }

    public bool IsPrimary { get; }

    public string PipeName { get; }

    public static SingleInstanceLock Create(string baseDir)
    {
        var mutex = new Mutex(initiallyOwned: true, AppIpcNames.GetMutexName(baseDir), out var createdNew);
        return new SingleInstanceLock(mutex, createdNew, AppIpcNames.GetPipeName(baseDir));
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
