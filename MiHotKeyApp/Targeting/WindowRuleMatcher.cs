namespace MiHotKeyApp.Targeting;

using System.Text;
using System.Text.RegularExpressions;
using MiHotKeyApp.Config;

internal sealed class WindowRuleMatcher
{
    private CompiledRule[] _rules = [];

    public void SetRules(IEnumerable<TargetRuleConfig> rules)
    {
        _rules = rules
            .OrderByDescending(r => r.Prio)
            .Select(Compile)
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
        var cls = info.ClassName ?? "";

        foreach (var rule in _rules)
        {
            if (rule.Procs.Count > 0 && !rule.Procs.Contains(proc))
            {
                continue;
            }

            if (rule.Classes.Count > 0)
            {
                if (!rule.Classes.Contains(cls))
                {
                    continue;
                }
            }

            if (rule.TitlePatterns.Length > 0)
            {
                var ok = false;
                foreach (var re in rule.TitlePatterns)
                {
                    if (re.IsMatch(title))
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

            return rule.Raw;
        }

        return null;
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
