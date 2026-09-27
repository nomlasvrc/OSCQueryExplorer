using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Core.Tree;

public sealed class NodeTree
{
    public OscNode Root { get; private set; } = new() { FullPath = "/" };
    private readonly Dictionary<string, CustomNodeDefinition> _custom = new(StringComparer.Ordinal);
    private HashSet<string>? _expansionBeforeSearch;

    public OscNode? Find(string path) => Root.SelfAndDescendants().FirstOrDefault(x => x.FullPath == Normalize(path));

    public void SetCustomNodes(IEnumerable<CustomNodeDefinition> definitions)
    {
        _custom.Clear();
        foreach (var definition in definitions) _custom[Normalize(definition.FullPath)] = definition with { FullPath = Normalize(definition.FullPath) };
        RemoveCustomNodes(Root);
        ApplyCustomNodes(Root);
        Sort(Root);
    }

    public bool ReplaceRemoteTree(OscNode remoteRoot)
    {
        ApplyCustomNodes(remoteRoot);
        Sort(remoteRoot);

        if (HasSameStructure(Root, remoteRoot))
        {
            UpdateInPlace(Root, remoteRoot);
            return false;
        }

        var old = Root.SelfAndDescendants().ToDictionary(x => x.FullPath, StringComparer.Ordinal);
        foreach (var node in remoteRoot.SelfAndDescendants())
        {
            if (!old.TryGetValue(node.FullPath, out var previous)) node.ChangeKind = NodeChangeKind.Added;
            else
            {
                node.IsPinned = previous.IsPinned;
                node.IsPublished = previous.IsPublished;
                node.IsExpanded = previous.IsExpanded;
                node.IsSelected = previous.IsSelected;
                node.Observed = PreferObserved(previous.Observed, node.Observed);
                node.ChangeKind = previous.TypeTag == node.TypeTag ? NodeChangeKind.None : NodeChangeKind.TypeChanged;
            }
        }
        Root = remoteRoot;
        return true;
    }

    public IReadOnlyList<OscNode> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Root.ApplyVisibilityFilter(null);
            if (_expansionBeforeSearch is not null)
            {
                foreach (var node in Root.SelfAndDescendants()) node.IsExpanded = _expansionBeforeSearch.Contains(node.FullPath);
                _expansionBeforeSearch = null;
            }
            return Root.DisplayChildren;
        }
        _expansionBeforeSearch ??= Root.SelfAndDescendants().Where(x => x.IsExpanded).Select(x => x.FullPath).ToHashSet(StringComparer.Ordinal);
        var matches = Root.SelfAndDescendants().Where(x => x.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToHashSet();
        var visible = new HashSet<OscNode>(matches);
        foreach (var match in matches)
        {
            foreach (var child in match.SelfAndDescendants()) visible.Add(child);
            var path = match.FullPath;
            while (path != "/")
            {
                path = path[..Math.Max(1, path.LastIndexOf('/'))];
                var ancestor = Find(path);
                if (ancestor is not null) visible.Add(ancestor);
            }
        }
        Root.ApplyVisibilityFilter(visible);
        foreach (var node in visible.Where(x => x.DisplayChildren.Count > 0)) node.IsExpanded = true;
        return Root.DisplayChildren;
    }

    private void ApplyCustomNodes(OscNode root)
    {
        var paths = root.SelfAndDescendants().Select(x => x.FullPath).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in _custom.Values.Where(x => !paths.Contains(x.FullPath))) EnsureCustomNode(root, definition);
    }

    private static void RemoveCustomNodes(OscNode node)
    {
        for (var index = node.Children.Count - 1; index >= 0; index--)
        {
            var child = node.Children[index];
            if (child.IsCustom)
                node.Children.RemoveAt(index);
            else
                RemoveCustomNodes(child);
        }
    }

    private static void EnsureCustomNode(OscNode root, CustomNodeDefinition definition)
    {
        var current = root;
        var currentPath = string.Empty;
        foreach (var segment in definition.FullPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath += "/" + segment;
            var next = current.Children.FirstOrDefault(x => x.FullPath == currentPath);
            if (next is null)
            {
                next = new OscNode { FullPath = currentPath, IsCustom = true, TypeTag = currentPath == definition.FullPath ? definition.TypeTag : null };
                current.Children.Add(next);
            }
            current = next;
        }
    }

    private static ObservedValue? PreferObserved(ObservedValue? oldValue, ObservedValue? incoming) =>
        oldValue?.Origin == ValueOrigin.UdpReceived && incoming?.Origin == ValueOrigin.OscQuery
            ? oldValue
            : incoming ?? oldValue;

    private static bool HasSameStructure(OscNode current, OscNode incoming)
    {
        if (!string.Equals(current.FullPath, incoming.FullPath, StringComparison.Ordinal) || current.Children.Count != incoming.Children.Count)
            return false;

        for (var index = 0; index < current.Children.Count; index++)
            if (!HasSameStructure(current.Children[index], incoming.Children[index])) return false;

        return true;
    }

    private static void UpdateInPlace(OscNode current, OscNode incoming)
    {
        var previousType = current.TypeTag;
        current.TypeTag = incoming.TypeTag;
        current.Access = incoming.Access;
        current.Description = incoming.Description;
        current.Unit = incoming.Unit;
        current.IsCustom = incoming.IsCustom;
        current.IsAvailable = incoming.IsAvailable;
        current.Observed = PreferObserved(current.Observed, incoming.Observed);
        current.ChangeKind = previousType == incoming.TypeTag ? NodeChangeKind.None : NodeChangeKind.TypeChanged;
        current.Ranges = incoming.Ranges;
        current.RangeMetadata = incoming.RangeMetadata;
        current.AdditionalAttributes.Clear();
        foreach (var attribute in incoming.AdditionalAttributes)
            current.AdditionalAttributes[attribute.Key] = attribute.Value?.DeepClone();

        for (var index = 0; index < current.Children.Count; index++)
            UpdateInPlace(current.Children[index], incoming.Children[index]);
    }

    private static void Sort(OscNode node)
    {
        var sorted = node.Children.OrderByDescending(x => x.Children.Count > 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        node.Children.Clear();
        foreach (var child in sorted) { Sort(child); node.Children.Add(child); }
    }

    public static string Normalize(string path) => "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
}
