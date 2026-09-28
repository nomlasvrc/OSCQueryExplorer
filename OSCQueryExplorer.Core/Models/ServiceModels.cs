namespace OSCQueryExplorer.Core.Models;

public sealed record ServiceIdentity(string Name, string Host, string? UserAlias = null)
{
    public string StableKey => $"{Name.Trim().ToUpperInvariant()}@{Host.Trim().ToUpperInvariant()}#{UserAlias?.Trim().ToUpperInvariant() ?? string.Empty}";
}

public sealed record DiscoveredService(ServiceIdentity Identity, Uri HttpEndpoint, DateTimeOffset SeenAt, bool IsManual = false);

public sealed record CustomNodeDefinition(string FullPath, string TypeTag);

public sealed class ServiceState
{
    public List<string> PinnedPaths { get; set; } = [];
    public List<CustomNodeDefinition> CustomNodes { get; set; } = [];
    public List<string> UnpublishedPaths { get; set; } = [];
    public HashSet<string> ExpandedPaths { get; set; } = new(StringComparer.Ordinal);
    public string? SelectedPath { get; set; }
}
