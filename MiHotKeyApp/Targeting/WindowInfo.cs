namespace MiHotKeyApp.Targeting;

internal sealed record WindowInfo(nint Hwnd, uint Pid, string ProcessName, string Title);

