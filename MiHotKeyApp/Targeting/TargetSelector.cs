namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Native;

internal sealed class TargetSelector
{
    private readonly ForegroundTracker _tracker;

    public TargetSelector(ForegroundTracker tracker)
    {
        _tracker = tracker;
    }

    public nint[] GetCandidates(int depth)
    {
        if (depth < 1)
        {
            return [];
        }

        var list = new List<nint>(depth);
        var seen = new HashSet<nint>();

        var fg = User32.GetForegroundWindow();
        if (fg != 0 && User32.IsWindow(fg) && seen.Add(fg))
        {
            list.Add(fg);
            if (list.Count >= depth)
            {
                return list.ToArray();
            }
        }

        foreach (var hwnd in _tracker.GetHistorySnapshot())
        {
            if (hwnd == 0 || !User32.IsWindow(hwnd) || !seen.Add(hwnd))
            {
                continue;
            }

            list.Add(hwnd);
            if (list.Count >= depth)
            {
                break;
            }
        }

        return list.ToArray();
    }
}
