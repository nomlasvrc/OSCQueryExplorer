using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.Core.History;

public enum HistoryKind { Osc, System }
public enum OscDirection { Received, Sent }
public enum SystemLevel { Info, Warning, Error }

public sealed record HistoryEntry(
    long Id,
    DateTimeOffset Timestamp,
    HistoryKind Kind,
    string Message,
    OscDirection? Direction = null,
    int? SourcePort = null,
    int? DestinationPort = null,
    string? Address = null,
    string? TypeTag = null,
    IReadOnlyList<OscValue>? Values = null,
    SystemLevel? Level = null,
    string? SourceAddress = null,
    string? DestinationAddress = null)
{
    public string Route => Kind == HistoryKind.System ? string.Empty : $"{(SourcePort?.ToString() ?? "?")}→{(DestinationPort?.ToString() ?? "?")}";
    public string ValueText => Values is null ? string.Empty : string.Join(" ", Values.Select(x => x.ToDisplayString()));
    public string DisplayText => FormatDisplayText(TypeDisplayFormat.TypeTag);
    public string FormatDisplayText(TypeDisplayFormat typeDisplay) => Kind == HistoryKind.System
        ? $"{Timestamp:HH:mm:ss.fff}  [SYSTEM][{Level switch { SystemLevel.Warning => "WARN", SystemLevel.Error => "ERROR", _ => "INFO" }}]  {Message}"
        : $"{Timestamp:HH:mm:ss.fff}  {Route}  {FormatType(typeDisplay)}  {Address}  {ValueText}";

    private string FormatType(TypeDisplayFormat typeDisplay) => string.IsNullOrWhiteSpace(TypeTag)
        ? "?"
        : OscTypeFormatter.Format(TypeTag, typeDisplay);
}
