namespace MiHotKeyApp.Targeting;

using System.Text;
using System.Text.RegularExpressions;
using MiHotKeyApp.Config;

internal sealed class WindowRuleMatcher
{
    private CompiledRule[] _rules = [];
    private Dictionary<string, CompiledRule> _rulesById = new(StringComparer.OrdinalIgnoreCase);

    public void SetRules(IEnumerable<TargetRuleConfig> rules)
    {
        _rules = rules
            .OrderByDescending(r => r.Prio)
            .Select(Compile)
            .ToArray();

        _rulesById = _rules.ToDictionary(rule => rule.Raw.Id, StringComparer.OrdinalIgnoreCase);
    }

    public TargetRuleConfig[] GetRulesInPriorityOrder()
    {
        return _rules
            .Select(static rule => rule.Raw)
            .ToArray();
    }

    public TargetRuleConfig[] GetMatches(WindowInfo info)
    {
        if (info.Hwnd == 0)
        {
            return [];
        }

        return _rules
            .Where(rule => IsMatch(rule, info))
            .Select(static rule => rule.Raw)
            .ToArray();
    }

    public bool IsMatch(WindowInfo info, TargetRuleConfig rule)
    {
        if (info.Hwnd == 0)
        {
            return false;
        }

        if (!_rulesById.TryGetValue(rule.Id, out var compiled))
        {
            return false;
        }

        return IsMatch(compiled, info);
    }

    public TargetRuleConfig? Match(WindowInfo info)
    {
        foreach (var rule in _rules)
        {
            if (IsMatch(rule, info))
            {
                return rule.Raw;
            }
        }

        return null;
    }

    private static bool IsMatch(CompiledRule rule, WindowInfo info)
    {
        if (info.Hwnd == 0)
        {
            return false;
        }

        var proc = NormalizeProc(info.ProcessName);
        var title = info.Title ?? "";
        var cls = info.ClassName ?? "";

        if (rule.Procs.Count > 0 && !rule.Procs.Contains(proc))
        {
            return false;
        }

        if (rule.Classes.Count > 0 && !rule.Classes.Contains(cls))
        {
            return false;
        }

        if (rule.TitlePatterns.Length == 0)
        {
            return true;
        }

        foreach (var re in rule.TitlePatterns)
        {
            if (re.IsMatch(title))
            {
                return true;
            }
        }

        return false;
    }

    private static CompiledRule Compile(TargetRuleConfig rule)
    {
        var procs = rule.Proc
            .Select(NormalizeProc)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var classes = rule.ClassIs
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var titlePatterns = BuildTitlePatterns(rule)
            .Select(p => CreateTitleRegex(p))
            .ToArray();

        return new CompiledRule(rule, procs, classes, titlePatterns);
    }

    private static IEnumerable<string> BuildTitlePatterns(TargetRuleConfig rule)
    {
        if (rule.Title.Length > 0)
        {
            foreach (var p in rule.Title)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    yield return p;
                }
            }

            yield break;
        }

        // Backward compatible fields.
        foreach (var s in rule.TitleHas)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                yield return $"*{s}*";
            }
        }

        foreach (var s in rule.TitleEndsWith)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                yield return $"*{s}";
            }
        }
    }

    private static Regex CreateTitleRegex(string pattern)
    {
        var p = pattern ?? "";

        // Convenience: "=Exact Title" matches the whole title.
        if (p.StartsWith("=", StringComparison.Ordinal))
        {
            var exact = p[1..];
            return new Regex(
                $"^{Regex.Escape(exact)}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        // If there are no glob wildcards, treat as "contains" to avoid having to write "*text*".
        if (!p.Contains('*') && !p.Contains('?'))
        {
            return new Regex(
                Regex.Escape(p),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        var rx = GlobToRegex(p);
        return new Regex(
            $"^{rx}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder(glob.Length + 8);

        for (var i = 0; i < glob.Length; i++)
        {
            var ch = glob[i];
            if (ch == '\\' && i + 1 < glob.Length)
            {
                // Escape next character literally (e.g. \* or \?).
                i++;
                AppendRegexEscaped(sb, glob[i]);
                continue;
            }

            if (ch == '*')
            {
                sb.Append(".*");
                continue;
            }

            if (ch == '?')
            {
                sb.Append('.');
                continue;
            }

            AppendRegexEscaped(sb, ch);
        }

        return sb.ToString();
    }

    private static void AppendRegexEscaped(StringBuilder sb, char ch)
    {
        // Escape regex metacharacters.
        if ("\\.^$|()[]{}+?".IndexOf(ch) >= 0)
        {
            sb.Append('\\');
        }

        sb.Append(ch);
    }

    private static string NormalizeProc(string processName)
    {
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return processName[..^4];
        }

        return processName;
    }

    private sealed record CompiledRule(
        TargetRuleConfig Raw,
        HashSet<string> Procs,
        HashSet<string> Classes,
        Regex[] TitlePatterns);
}
