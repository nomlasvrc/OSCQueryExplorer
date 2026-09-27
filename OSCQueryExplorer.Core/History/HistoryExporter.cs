using System.Globalization;
using System.Text;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.Core.History;

public static class HistoryExporter
{
    public static async Task ExportTextAsync(string path, IEnumerable<HistoryEntry> entries, TypeDisplayFormat typeDisplay = TypeDisplayFormat.TypeTag, CancellationToken cancellationToken = default) =>
        await File.WriteAllLinesAsync(path, entries.Select(x => x.FormatDisplayText(typeDisplay)), new UTF8Encoding(true), cancellationToken);

    public static async Task ExportCsvAsync(string path, IEnumerable<HistoryEntry> entries, TypeDisplayFormat typeDisplay = TypeDisplayFormat.TypeTag, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Time,Kind,Direction,SourceAddress,SourcePort,DestinationAddress,DestinationPort,Route,Type,Address,Values,Level,Message");
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new[] { item.Timestamp.ToString("O", CultureInfo.InvariantCulture), item.Kind.ToString(), item.Direction?.ToString(), item.SourceAddress,
                item.SourcePort?.ToString(CultureInfo.InvariantCulture), item.DestinationAddress, item.DestinationPort?.ToString(CultureInfo.InvariantCulture),
                item.Route, OscTypeFormatter.Format(item.TypeTag, typeDisplay), item.Address, item.ValueText, item.Level?.ToString(), item.Message };
            await writer.WriteLineAsync(string.Join(',', fields.Select(Escape)));
        }
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
