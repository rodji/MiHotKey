namespace MiHotKeyApp.Config;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

internal sealed class ConfigStore
{
    private readonly string _baseDir;

    public ConfigStore(string baseDir)
    {
        _baseDir = baseDir;
    }

    public string ResolveConfigPath(string configPath)
    {
        if (Path.IsPathRooted(configPath))
        {
            return configPath;
        }

        return Path.GetFullPath(Path.Combine(_baseDir, configPath));
    }

    public AppConfig LoadFromPath(string path)
    {
        var json = File.ReadAllText(path);

        var options = CreateLoadOptions();
        var config = JsonSerializer.Deserialize<AppConfig>(json, options)
            ?? throw new InvalidDataException("Failed to deserialize config.json");

        ConfigValidator.Validate(config);
        return config;
    }

    public void SaveToPath(string path, AppConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, CreateSaveOptions());
        File.WriteAllText(path, json + Environment.NewLine);
    }

    public bool TrySetAutostartEnabled(string path, bool enabled, out string? error)
    {
        error = null;
        try
        {
            var text = File.ReadAllText(path);

            if (TryPatchAutostartEnabledInText(text, enabled, out var patched))
            {
                if (!string.Equals(text, patched, StringComparison.Ordinal))
                {
                    File.WriteAllText(path, patched);
                }

                return true;
            }

            // Fallback: parse + pretty-write.
            var opts = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };

            var node = JsonNode.Parse(text, documentOptions: opts);
            if (node is not JsonObject root)
            {
                error = "Config root must be an object";
                return false;
            }

            var app = root["app"] as JsonObject ?? new JsonObject();
            root["app"] = app;

            var autostart = app["autostart"] as JsonObject ?? new JsonObject();
            app["autostart"] = autostart;
            autostart["enabled"] = enabled;

            var outJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, outJson + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static JsonSerializerOptions CreateLoadOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private static JsonSerializerOptions CreateSaveOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private static bool TryPatchAutostartEnabledInText(string text, bool enabled, out string patched)
    {
        patched = text;

        // Strategy:
        // - Find "app": { ... } block
        // - Find "autostart": { ... } inside it
        // - Find "enabled": true/false inside it and replace only the literal
        // This keeps comments/whitespace/ordering intact for the common case.

        if (!TryFindObjectBlock(text, "\"app\"", out var appStart, out var appEnd))
        {
            return false;
        }

        var appText = text.AsSpan(appStart, appEnd - appStart + 1).ToString();
        if (!TryFindObjectBlock(appText, "\"autostart\"", out var autoStartRel, out var autoEndRel))
        {
            return false;
        }

        var autoStart = appStart + autoStartRel;
        var autoEnd = appStart + autoEndRel;

        var autoBlock = text.AsSpan(autoStart, autoEnd - autoStart + 1);
        var idx = IndexOfEnabledProperty(autoBlock);
        if (idx < 0)
        {
            return false;
        }

        var valueStart = autoStart + idx;
        var valueEnd = valueStart;
        while (valueEnd < text.Length && char.IsWhiteSpace(text[valueEnd]))
        {
            valueEnd++;
        }

        if (valueEnd + 4 <= text.Length && text.AsSpan(valueEnd, 4).Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            patched = string.Concat(text.AsSpan(0, valueEnd), enabled ? "true" : "false", text.AsSpan(valueEnd + 4));
            return true;
        }

        if (valueEnd + 5 <= text.Length && text.AsSpan(valueEnd, 5).Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            patched = string.Concat(text.AsSpan(0, valueEnd), enabled ? "true" : "false", text.AsSpan(valueEnd + 5));
            return true;
        }

        return false;
    }

    private static int IndexOfEnabledProperty(ReadOnlySpan<char> objBlock)
    {
        // Find `"enabled"` then the colon and return index right after colon.
        var key = "\"enabled\"".AsSpan();
        var i = objBlock.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return -1;
        }

        i += key.Length;
        while (i < objBlock.Length && char.IsWhiteSpace(objBlock[i]))
        {
            i++;
        }

        if (i >= objBlock.Length || objBlock[i] != ':')
        {
            return -1;
        }

        i++;
        return i;
    }

    private static bool TryFindObjectBlock(string text, string propertyNameWithQuotes, out int objStart, out int objEnd)
    {
        objStart = 0;
        objEnd = 0;

        var nameIdx = text.IndexOf(propertyNameWithQuotes, StringComparison.OrdinalIgnoreCase);
        if (nameIdx < 0)
        {
            return false;
        }

        var i = nameIdx + propertyNameWithQuotes.Length;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length || text[i] != ':')
        {
            return false;
        }

        i++;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length || text[i] != '{')
        {
            return false;
        }

        objStart = i;
        objEnd = FindMatchingBrace(text, i);
        return objEnd >= objStart;
    }

    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        var depth = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escape = false;

        for (var i = openBraceIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '/' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}

