namespace MiHotKeyApp.Input;

internal interface ITriggerSource : IDisposable
{
    void Start();
    void Stop();
}

