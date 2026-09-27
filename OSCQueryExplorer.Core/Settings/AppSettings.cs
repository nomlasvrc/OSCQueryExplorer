using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Core.Settings;

public enum AppTheme { System, Light, Dark }
public enum TypeDisplayFormat { TypeTag, TypeName }

public sealed class AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public double LogFontSize { get; set; } = 13;
    public TypeDisplayFormat TypeDisplay { get; set; } = TypeDisplayFormat.TypeTag;
    public string LogFilter { get; set; } = string.Empty;
    public string? LastServiceKey { get; set; }
    public string? LastManualEndpoint { get; set; }
    public int PollingIntervalSeconds { get; set; } = 5;
    public bool PublishToLan { get; set; }
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public PaneSettings Panes { get; set; } = new();
    public List<SliderRangeRule> SliderRanges { get; set; } = [];
    public Dictionary<string, ServiceState> Services { get; set; } = new(StringComparer.Ordinal);
}

public sealed record AppPreferences(
    AppTheme Theme,
    double LogFontSize,
    int PollingIntervalSeconds,
    bool PublishToLan,
    bool CheckUpdatesAutomatically);

public sealed class SliderRangeRule
{
    public string PathPrefix { get; set; } = "/";
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 1;
    public string TypeTags { get; set; } = string.Empty;
}

public sealed class PaneSettings
{
    public double ExplorerWidth { get; set; } = 270;
    public double InspectorWidth { get; set; } = 370;
    public double PinnedWidth { get; set; } = 300;
    public double LogHeight { get; set; } = 230;
}
