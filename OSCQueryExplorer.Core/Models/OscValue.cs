using System.Collections.Immutable;

namespace OSCQueryExplorer.Core.Models;

public enum OscValueKind { Int32, Float32, String, Blob, Int64, Timetag, Float64, Char, Rgba, Midi, True, False, Nil, Impulse, Array }

public sealed record OscValue(OscValueKind Kind, object? Value)
{
    public static OscValue Array(IEnumerable<OscValue> values) => new(OscValueKind.Array, values.ToImmutableArray());

    public string ToDisplayString() => Kind switch
    {
        OscValueKind.String => $"\"{Value?.ToString()?.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"")}\"",
        OscValueKind.Blob when Value is byte[] bytes => $"blob[{bytes.Length}] {Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length)))}{(bytes.Length > 16 ? "…" : string.Empty)}",
        OscValueKind.Array when Value is IEnumerable<OscValue> values => $"[{string.Join(", ", values.Select(x => x.ToDisplayString()))}]",
        OscValueKind.True => "true",
        OscValueKind.False => "false",
        OscValueKind.Nil => "nil",
        OscValueKind.Impulse => "impulse",
        _ => Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture) ?? Kind.ToString()
    };
}
