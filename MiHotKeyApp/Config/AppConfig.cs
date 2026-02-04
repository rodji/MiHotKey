namespace MiHotKeyApp.Config;

using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

internal sealed class AppConfig
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("app")]
    public AppSection App { get; init; } = new();

    [JsonPropertyName("tray")]
    public TraySection Tray { get; init; } = new();

    [JsonPropertyName("logging")]
    public LoggingSection Logging { get; init; } = new();

    [JsonPropertyName("inputs")]
    public InputsSection Inputs { get; init; } = new();

    [JsonPropertyName("bindings")]
    public Dictionary<string, string[]> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("targets")]
    public TargetsSection Targets { get; init; } = new();

    [JsonPropertyName("shortcuts")]
    public Dictionary<string, ShortcutConfig> Shortcuts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("routesByTrigger")]
    public Dictionary<string, RouteConfig[]> RoutesByTrigger { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class AppSection
{
    [JsonPropertyName("configPath")]
    public string ConfigPath { get; init; } = @".\config.json";

    [JsonPropertyName("altConfigPathHint")]
    public string AltConfigPathHint { get; init; } = @"%AppData%\MiHotKey\config.json";

    [JsonPropertyName("logBufferSize")]
    public int LogBufferSize { get; init; } = 100;

    [JsonPropertyName("foregroundTrackingEnabled")]
    public bool ForegroundTrackingEnabled { get; init; } = true;

    [JsonPropertyName("foregroundHistorySize")]
    public int ForegroundHistorySize { get; init; } = 10;

    [JsonPropertyName("targetSelectionMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetSelectionMode TargetSelectionMode { get; init; } = TargetSelectionMode.ForegroundThenPrevious;

    [JsonPropertyName("focusPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FocusPolicy FocusPolicy { get; init; } = FocusPolicy.ActivateTargetTemporarily;

    [JsonPropertyName("sendTimingMs")]
    public SendTimingMsSection SendTimingMs { get; init; } = new();
}

internal sealed class SendTimingMsSection
{
    [JsonPropertyName("modDownToKeyDown")]
    public int ModDownToKeyDown { get; init; } = 5;

    [JsonPropertyName("keyDownToKeyUp")]
    public int KeyDownToKeyUp { get; init; } = 2;

    [JsonPropertyName("keyUpToModUp")]
    public int KeyUpToModUp { get; init; } = 2;
}

internal enum TargetSelectionMode
{
    ForegroundThenPrevious,
    ForegroundOnly,
    AlwaysPrevious,
}

internal enum FocusPolicy
{
    ActivateTargetTemporarily,
    NoFocusChange,
}

internal sealed class TraySection
{
    [JsonPropertyName("reloadConfig")]
    public bool ReloadConfig { get; init; } = true;

    [JsonPropertyName("showLog")]
    public bool ShowLog { get; init; } = true;

    [JsonPropertyName("exit")]
    public bool Exit { get; init; } = true;

    [JsonPropertyName("toggleForegroundTracking")]
    public bool ToggleForegroundTracking { get; init; } = true;
}

internal sealed class LoggingSection
{
    [JsonPropertyName("level")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Level { get; init; } = LogLevel.Information;

    [JsonPropertyName("overrides")]
    public Dictionary<string, LogLevel> Overrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("includeScopes")]
    public bool IncludeScopes { get; init; } = false;

    [JsonPropertyName("maxMessageLength")]
    public int MaxMessageLength { get; init; } = 300;

    [JsonPropertyName("showConfigPathsInLog")]
    public bool ShowConfigPathsInLog { get; init; } = true;
}

internal sealed class InputsSection
{
    [JsonPropertyName("hotkeys")]
    public HotkeyInputConfig[] Hotkeys { get; init; } = [];

    [JsonPropertyName("wmi")]
    public WmiInputConfig[] Wmi { get; init; } = [];
}

internal sealed class HotkeyInputConfig
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("keys")]
    public string Keys { get; init; } = "";
}

internal sealed class WmiInputConfig
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = @"root\wmi";

    [JsonPropertyName("query")]
    public string Query { get; init; } = "";

    [JsonPropertyName("where")]
    public Dictionary<string, string> Where { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("extract")]
    public WmiExtractConfig Extract { get; init; } = new();

    [JsonPropertyName("map")]
    public Dictionary<string, string> Map { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("repeatHandling")]
    public string RepeatHandling { get; init; } = "firstDownOnlyUntilUp";

    [JsonPropertyName("debounceMs")]
    public int DebounceMs { get; init; } = 40;
}

internal sealed class WmiExtractConfig
{
    [JsonPropertyName("prop")]
    public string Prop { get; init; } = "EventDetail";

    [JsonPropertyName("index")]
    public int Index { get; init; } = 2;
}

internal sealed class TargetsSection
{
    [JsonPropertyName("rules")]
    public TargetRuleConfig[] Rules { get; init; } = [];
}

internal sealed class TargetRuleConfig
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("prio")]
    public int Prio { get; init; } = 0;

    [JsonPropertyName("proc")]
    public string[] Proc { get; init; } = [];

    [JsonPropertyName("titleHas")]
    public string[] TitleHas { get; init; } = [];
}

internal sealed class ShortcutConfig
{
    [JsonPropertyName("keys")]
    public string Keys { get; init; } = "";

    [JsonPropertyName("send")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SendMode Send { get; init; } = SendMode.Scan;
}

internal enum SendMode
{
    Scan,
    Vk,
}

internal sealed class RouteConfig
{
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = "";

    [JsonPropertyName("shortcut")]
    public string Shortcut { get; init; } = "";
}
