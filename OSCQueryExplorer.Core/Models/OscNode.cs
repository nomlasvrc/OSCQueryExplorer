using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace OSCQueryExplorer.Core.Models;

[Flags]
public enum OscAccess { None = 0, Read = 1, Write = 2 }
public enum NodeChangeKind { None, Added, TypeChanged }
public enum ValueOrigin { Unknown, OscQuery, UdpReceived }

public sealed class OscRange
{
    public object? Min { get; init; }
    public object? Max { get; init; }
    public IReadOnlyList<object?> Values { get; init; } = [];
}

public sealed class ObservedValue
{
    public IReadOnlyList<OscValue> Values { get; init; } = [];
    public ValueOrigin Origin { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class OscNode : INotifyPropertyChanged
{
    private bool _isPublished = true;
    private bool _isPinned;
    private bool _isAvailable = true;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservedValue? _observed;
    private string? _typeTag;
    private OscAccess? _access;
    public event PropertyChangedEventHandler? PropertyChanged;
    public required string FullPath { get; init; }
    public string Name => FullPath == "/" ? "/" : FullPath.TrimEnd('/').Split('/').Last();
    public string? TypeTag { get => _typeTag; set => Set(ref _typeTag, value); }
    public OscAccess? Access { get => _access; set => Set(ref _access, value); }
    public string? Description { get; set; }
    public JsonNode? Unit { get; set; }
    public bool IsCustom { get; set; }
    public bool IsPublished { get => _isPublished; set => Set(ref _isPublished, value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public bool IsAvailable { get => _isAvailable; set => Set(ref _isAvailable, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public NodeChangeKind ChangeKind { get; set; }
    public ObservedValue? Observed { get => _observed; set => Set(ref _observed, value); }
    public IReadOnlyList<OscRange> Ranges { get; set; } = [];
    public JsonNode? RangeMetadata { get; set; }
    public JsonObject AdditionalAttributes { get; } = [];
    public ObservableCollection<OscNode> Children { get; } = [];
    public ObservableCollection<OscNode> DisplayChildren { get; } = [];
    private bool _filterActive;

    public OscNode()
    {
        Children.CollectionChanged += Children_CollectionChanged;
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_filterActive) return;
        DisplayChildren.Clear();
        foreach (var child in Children) DisplayChildren.Add(child);
    }

    public void ApplyVisibilityFilter(IReadOnlySet<OscNode>? visible)
    {
        _filterActive = visible is not null;
        DisplayChildren.Clear();
        foreach (var child in Children.Where(child => visible is null || visible.Contains(child)))
            DisplayChildren.Add(child);
        foreach (var child in Children) child.ApplyVisibilityFilter(visible);
    }

    public IEnumerable<OscNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.SelfAndDescendants())
                yield return descendant;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
