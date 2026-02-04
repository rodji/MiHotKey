namespace MiHotKeyApp.Targeting;

using MiHotKeyApp.Config;

internal sealed class WindowRuleMatcher
{
    private TargetRuleConfig[] _rules = [];

    public void SetRules(IEnumerable<TargetRuleConfig> rules)
    {
        _rules = rules
            .OrderByDescending(r => r.Prio)
            .ToArray();
    }

    public TargetRuleConfig? Match(WindowInfo info)
    {
        if (info.Hwnd == 0)
        {
            return null;
        }

        var proc = NormalizeProc(info.ProcessName);
        var title = info.Title ?? "";

        foreach (var rule in _rules)
        {
            if (!rule.Proc.Select(NormalizeProc).Contains(proc, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.TitleHas.Length > 0)
            {
                var ok = false;
                foreach (var needle in rule.TitleHas)
                {
                    if (!string.IsNullOrEmpty(needle) && title.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        ok = true;
                        break;
                    }
                }

                if (!ok)
                {
                    continue;
                }
            }

            return rule;
        }

        return null;
    }

    private static string NormalizeProc(string processName)
    {
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return processName[..^4];
        }

        return processName;
    }
}

