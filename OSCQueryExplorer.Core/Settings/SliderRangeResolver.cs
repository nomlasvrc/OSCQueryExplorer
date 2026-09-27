using OSCQueryExplorer.Core.Tree;

namespace OSCQueryExplorer.Core.Settings;

public static class SliderRangeResolver
{
    public static bool TryResolve(IEnumerable<SliderRangeRule> rules, string path, char typeTag, out double minimum, out double maximum)
    {
        foreach (var rule in rules
                     .Where(rule => IsValid(rule) && (rule.TypeTags ?? string.Empty).Contains(typeTag))
                     .Select(rule => (Rule: rule, Prefix: NodeTree.Normalize(rule.PathPrefix)))
                     .OrderByDescending(item => item.Prefix.Length))
        {
            if (!Matches(path, rule.Prefix)) continue;
            minimum = rule.Rule.Minimum;
            maximum = rule.Rule.Maximum;
            return true;
        }
        minimum = 0;
        maximum = 1;
        return false;
    }

    private static bool IsValid(SliderRangeRule rule) =>
        !string.IsNullOrWhiteSpace(rule.PathPrefix)
        && double.IsFinite(rule.Minimum)
        && double.IsFinite(rule.Maximum)
        && rule.Maximum > rule.Minimum;

    private static bool Matches(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
}
