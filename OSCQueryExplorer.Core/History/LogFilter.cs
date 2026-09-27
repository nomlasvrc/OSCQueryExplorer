using System.Globalization;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.Core.History;

public sealed class LogFilter
{
    private readonly IReadOnlyList<Term> _terms;
    private LogFilter(IReadOnlyList<Term> terms) => _terms = terms;

    public static LogFilter Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new LogFilter([]);
        var terms = Tokenize(expression).Select(ParseTerm).ToArray();
        return new LogFilter(terms);
    }

    public bool IsMatch(HistoryEntry entry, TypeDisplayFormat typeDisplay = TypeDisplayFormat.TypeTag) =>
        _terms.All(term => term.Excluded != Match(term, entry, typeDisplay));

    private static bool Match(Term term, HistoryEntry entry, TypeDisplayFormat typeDisplay)
    {
        if (term.Field is null) return entry.FormatDisplayText(typeDisplay).Contains(term.Value, StringComparison.OrdinalIgnoreCase);
        return term.Field switch
        {
            "port" => PortMatches(entry.SourcePort, term.Value) || PortMatches(entry.DestinationPort, term.Value),
            "src" => PortMatches(entry.SourcePort, term.Value),
            "dst" => PortMatches(entry.DestinationPort, term.Value),
            "addr" => entry.Address?.Contains(term.Value, StringComparison.OrdinalIgnoreCase) == true,
            "type" => entry.TypeTag?.Contains(term.Value, StringComparison.OrdinalIgnoreCase) == true ||
                      OscTypeFormatter.Format(entry.TypeTag, typeDisplay).Contains(term.Value, StringComparison.OrdinalIgnoreCase),
            "level" => entry.Level?.ToString().Equals(term.Value, StringComparison.OrdinalIgnoreCase) == true ||
                       (term.Value.Equals("WARN", StringComparison.OrdinalIgnoreCase) && entry.Level == SystemLevel.Warning),
            _ => entry.FormatDisplayText(typeDisplay).Contains($"{term.Field}:{term.Value}", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool PortMatches(int? port, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && port == parsed;

    private static Term ParseTerm(string token)
    {
        var excluded = token.StartsWith('-');
        if (excluded) token = token[1..];
        var separator = token.IndexOf(':');
        return separator > 0
            ? new Term(token[..separator].ToLowerInvariant(), token[(separator + 1)..], excluded)
            : new Term(null, token, excluded);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var buffer = new System.Text.StringBuilder();
        var quoted = false;
        var escaping = false;
        foreach (var c in value)
        {
            if (escaping) { buffer.Append(c); escaping = false; continue; }
            if (c == '\\') { escaping = true; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
                continue;
            }
            buffer.Append(c);
        }
        if (escaping) buffer.Append('\\');
        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private sealed record Term(string? Field, string Value, bool Excluded);
}
