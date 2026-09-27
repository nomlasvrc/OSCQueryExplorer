using System.Text.Json;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Core.Settings;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSCQueryExplorer", "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new AppSettings();
            return Normalize(settings);
        }
        catch (JsonException) { return new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
        catch (UnauthorizedAccessException) { return new AppSettings(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            File.Move(temp, _path, true);
        }
        finally { _saveGate.Release(); }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.System;
        if (!Enum.IsDefined(settings.TypeDisplay)) settings.TypeDisplay = TypeDisplayFormat.TypeTag;
        settings.LogFontSize = double.IsFinite(settings.LogFontSize) ? Math.Clamp(settings.LogFontSize, 9, 30) : 13;
        settings.PollingIntervalSeconds = Math.Clamp(settings.PollingIntervalSeconds, 1, 300);
        settings.Panes ??= new PaneSettings();
        settings.SliderRanges = settings.SliderRanges?.Where(rule => rule is not null).ToList() ?? [];
        var services = new Dictionary<string, ServiceState>(StringComparer.Ordinal);
        if (settings.Services is not null)
        {
            foreach (var (key, state) in settings.Services)
            {
                if (string.IsNullOrWhiteSpace(key) || state is null) continue;
                state.PinnedPaths = state.PinnedPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToList() ?? [];
                state.CustomNodes = state.CustomNodes?.Where(node => node is not null && !string.IsNullOrWhiteSpace(node.FullPath) && !string.IsNullOrWhiteSpace(node.TypeTag)).ToList() ?? [];
                state.ExpandedPaths = state.ExpandedPaths is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(state.ExpandedPaths.Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.Ordinal);
                services[key] = state;
            }
        }
        settings.Services = services;
        return settings;
    }
}
