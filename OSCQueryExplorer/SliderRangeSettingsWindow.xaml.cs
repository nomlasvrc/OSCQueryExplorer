using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;
using OSCQueryExplorer.Core.Tree;
using OSCQueryExplorer.ViewModels;
using Wpf.Ui.Controls;

namespace OSCQueryExplorer;

public partial class SliderRangeSettingsWindow : FluentWindow
{
    private readonly Dictionary<string, SliderRangeTreeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<SliderRangeTreeNode> RootNodes { get; } = [];
    public ObservableCollection<SliderRangeTreeNode> ConfiguredRules { get; } = [];
    public IReadOnlyList<SliderRangeRule> Rules { get; private set; } = [];

    public SliderRangeSettingsWindow(OscNode root, IEnumerable<SliderRangeRule> rules)
    {
        var rootNode = AddOscNode(root);
        RootNodes.Add(rootNode);
        foreach (var rule in rules)
        {
            var node = EnsurePath(rule.PathPrefix);
            node.HasRule = true;
            node.Minimum = rule.Minimum;
            node.Maximum = rule.Maximum;
            node.SetTypeTags(rule.TypeTags);
        }
        SortChildren(rootNode);
        rootNode.IsExpanded = true;
        rootNode.IsSelected = true;
        UpdateRuleColors();

        InitializeComponent();
        DataContext = this;
        EditorPanel.DataContext = rootNode;
    }

    private SliderRangeTreeNode AddOscNode(OscNode node)
    {
        var result = CreateNode(node.FullPath, node.TypeTag);
        _nodes[result.FullPath] = result;
        foreach (var child in node.Children) result.Children.Add(AddOscNode(child));
        return result;
    }

    private SliderRangeTreeNode EnsurePath(string path)
    {
        path = NodeTree.Normalize(path);
        if (_nodes.TryGetValue(path, out var existing)) return existing;
        var current = _nodes["/"];
        var currentPath = string.Empty;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath += "/" + segment;
            if (!_nodes.TryGetValue(currentPath, out var next))
            {
                next = CreateNode(currentPath, null);
                _nodes[currentPath] = next;
                current.Children.Add(next);
            }
            current.IsExpanded = true;
            current = next;
        }
        return current;
    }

    private SliderRangeTreeNode CreateNode(string fullPath, string? typeTag)
    {
        var node = new SliderRangeTreeNode(fullPath, typeTag);
        node.RuleChanged += (_, _) => UpdateRuleColors();
        return node;
    }

    private void UpdateRuleColors()
    {
        if (!_nodes.TryGetValue("/", out var root)) return;

        var colorDepths = _nodes.Values
            .Where(node => node.HasRule)
            .GroupBy(RuleKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(NodeDepth), StringComparer.Ordinal);

        ApplyRuleColor(root, null, colorDepths);
        ConfiguredRules.Clear();
        foreach (var node in _nodes.Values.Where(node => node.HasRule).OrderBy(node => node.FullPath, StringComparer.OrdinalIgnoreCase))
            ConfiguredRules.Add(node);
    }

    private static void ApplyRuleColor(SliderRangeTreeNode node, SliderRangeTreeNode? inheritedRule, IReadOnlyDictionary<string, int> colorDepths)
    {
        var effectiveRule = node.HasRule ? node : inheritedRule;
        if (effectiveRule is null)
        {
            node.SetRuleDisplay(false, "Transparent", "Transparent", string.Empty);
        }
        else
        {
            var key = RuleKey(effectiveRule);
            var color = RuleColor(key, colorDepths[key]);
            node.SetRuleDisplay(true, color, (node.HasRule ? "#38" : "#18") + color[1..], node.HasRule ? "設定" : "継承");
        }

        foreach (var child in node.Children) ApplyRuleColor(child, effectiveRule, colorDepths);
    }

    private static string RuleKey(SliderRangeTreeNode node) => string.Join('|',
        node.Minimum.ToString("R", CultureInfo.InvariantCulture),
        node.Maximum.ToString("R", CultureInfo.InvariantCulture),
        node.GetTypeTags());

    private static int NodeDepth(SliderRangeTreeNode node) => node.FullPath.Count(character => character == '/');

    private static string RuleColor(string key, int depth)
    {
        string[][] palette =
        [
            ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6"],
            ["#60A5FA", "#3B82F6", "#2563EB", "#1D4ED8", "#1E40AF"],
            ["#5EEAD4", "#2DD4BF", "#14B8A6", "#0D9488", "#0F766E"],
            ["#86EFAC", "#4ADE80", "#22C55E", "#16A34A", "#15803D"],
            ["#FCD34D", "#FBBF24", "#F59E0B", "#D97706", "#B45309"],
            ["#FDA4AF", "#FB7185", "#F43F5E", "#E11D48", "#BE123C"]
        ];
        var hash = 2166136261u;
        foreach (var character in key) hash = (hash ^ character) * 16777619u;
        return palette[hash % (uint)palette.Length][Math.Clamp(depth, 0, palette[0].Length - 1)];
    }

    private static void SortChildren(SliderRangeTreeNode node)
    {
        var children = node.Children.OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        node.Children.Clear();
        foreach (var child in children)
        {
            SortChildren(child);
            node.Children.Add(child);
        }
    }

    private void RulesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        EditorPanel.DataContext = e.NewValue as SliderRangeTreeNode;

    private void ConfiguredRules_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if ((sender as System.Windows.Controls.ListBox)?.SelectedItem is not SliderRangeTreeNode node) return;
        foreach (var ancestor in _nodes.Values.Where(candidate => node.FullPath.StartsWith(candidate.FullPath == "/" ? "/" : candidate.FullPath + "/", StringComparison.OrdinalIgnoreCase)))
            ancestor.IsExpanded = true;
        node.IsSelected = true;
        EditorPanel.DataContext = node;
    }

    private void AddPath_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewPathBox.Text))
        {
            ShowValidationError("追加するOSCパスを入力してください。");
            return;
        }
        var node = EnsurePath(NewPathBox.Text);
        node.HasRule = true;
        node.IsSelected = true;
        EditorPanel.DataContext = node;
        NewPathBox.Clear();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var rules = new List<SliderRangeRule>();
        foreach (var node in _nodes.Values.Where(node => node.HasRule).OrderBy(node => node.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(node.Minimum) || !double.IsFinite(node.Maximum) || node.Maximum <= node.Minimum)
            {
                ShowValidationError($"{node.FullPath}: 最大値は最小値より大きい有限値にしてください。");
                return;
            }
            var typeTags = node.GetTypeTags();
            if (typeTags.Length == 0)
            {
                ShowValidationError($"{node.FullPath}: 適用するOSC型を1つ以上選択してください。");
                return;
            }
            rules.Add(new SliderRangeRule { PathPrefix = node.FullPath, Minimum = node.Minimum, Maximum = node.Maximum, TypeTags = typeTags });
        }
        Rules = rules;
        DialogResult = true;
    }

    private void ShowValidationError(string message) =>
        System.Windows.MessageBox.Show(this, message, "スライダー範囲設定",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class SliderRangeTreeNode : ObservableObject
{
    private bool _hasRule;
    private bool _isExpanded;
    private bool _isSelected;
    private double _minimum;
    private double _maximum = 1;
    private bool _appliesInt32;
    private bool _appliesFloat32;
    private bool _appliesInt64;
    private bool _appliesFloat64;
    private bool _hasEffectiveRule;
    private string _ruleColor = "Transparent";
    private string _ruleBackground = "Transparent";
    private string _ruleBadgeText = string.Empty;

    public event EventHandler? RuleChanged;

    public SliderRangeTreeNode(string fullPath, string? typeTag)
    {
        FullPath = fullPath;
        TypeTag = typeTag;
    }

    public string FullPath { get; }
    public string Name => FullPath == "/" ? "/" : FullPath.TrimEnd('/').Split('/').Last();
    public string? TypeTag { get; }
    public ObservableCollection<SliderRangeTreeNode> Children { get; } = [];
    public bool HasRule { get => _hasRule; set => SetRuleValue(ref _hasRule, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public double Minimum { get => _minimum; set => SetRuleValue(ref _minimum, value); }
    public double Maximum { get => _maximum; set => SetRuleValue(ref _maximum, value); }
    public bool AppliesInt32 { get => _appliesInt32; set => SetRuleValue(ref _appliesInt32, value); }
    public bool AppliesFloat32 { get => _appliesFloat32; set => SetRuleValue(ref _appliesFloat32, value); }
    public bool AppliesInt64 { get => _appliesInt64; set => SetRuleValue(ref _appliesInt64, value); }
    public bool AppliesFloat64 { get => _appliesFloat64; set => SetRuleValue(ref _appliesFloat64, value); }
    public bool HasEffectiveRule { get => _hasEffectiveRule; private set => Set(ref _hasEffectiveRule, value); }
    public string RuleColor { get => _ruleColor; private set => Set(ref _ruleColor, value); }
    public string RuleBackground { get => _ruleBackground; private set => Set(ref _ruleBackground, value); }
    public string RuleBadgeText { get => _ruleBadgeText; private set => Set(ref _ruleBadgeText, value); }
    public string RangeLabel => $"{Minimum.ToString("G6", CultureInfo.InvariantCulture)} ～ {Maximum.ToString("G6", CultureInfo.InvariantCulture)}";
    public string TypeTagsLabel => string.Join(" / ", GetTypeTags().Select(tag => tag.ToString()));

    public void SetTypeTags(string? typeTags)
    {
        typeTags ??= string.Empty;
        AppliesInt32 = typeTags.Contains('i');
        AppliesFloat32 = typeTags.Contains('f');
        AppliesInt64 = typeTags.Contains('h');
        AppliesFloat64 = typeTags.Contains('d');
    }

    public string GetTypeTags() => string.Concat(
        AppliesInt32 ? "i" : string.Empty,
        AppliesFloat32 ? "f" : string.Empty,
        AppliesInt64 ? "h" : string.Empty,
        AppliesFloat64 ? "d" : string.Empty);

    public void SetRuleDisplay(bool hasEffectiveRule, string color, string background, string badgeText)
    {
        HasEffectiveRule = hasEffectiveRule;
        RuleColor = color;
        RuleBackground = background;
        RuleBadgeText = badgeText;
    }

    private void SetRuleValue<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        Set(ref field, value, propertyName);
        Raise(nameof(RangeLabel));
        Raise(nameof(TypeTagsLabel));
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }
}
