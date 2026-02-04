namespace MiHotKeyApp.Config;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class ConfigLoader
{
    private readonly string _baseDir;

    public ConfigLoader(string baseDir)
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

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var config = JsonSerializer.Deserialize<AppConfig>(json, options)
            ?? throw new InvalidDataException("Failed to deserialize config.json");

        ConfigValidator.Validate(config);

        return config;
    }
}

