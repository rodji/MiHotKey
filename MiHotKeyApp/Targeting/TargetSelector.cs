namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Config;

internal sealed class TargetSelector
{
    private readonly ForegroundTracker _tracker;

    public TargetSelector(ForegroundTracker tracker)
    {
        _tracker = tracker;
    }

    public nint[] GetCandidates(TargetSelectionMode mode)
    {
        var (fg, prev) = _tracker.GetForegroundAndPrevious();

        return mode switch
        {
            TargetSelectionMode.ForegroundOnly => fg != 0 ? [fg] : [],
            TargetSelectionMode.AlwaysPrevious => prev != 0 ? [prev] : [],
            TargetSelectionMode.ForegroundThenPrevious => prev != 0 ? [fg, prev] : fg != 0 ? [fg] : [],
            _ => fg != 0 ? [fg] : [],
        };
    }
}

