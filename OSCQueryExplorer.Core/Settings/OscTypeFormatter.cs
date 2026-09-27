namespace OSCQueryExplorer.Core.Settings;

public static class OscTypeFormatter
{
    public static string Format(string? typeTags, TypeDisplayFormat format)
    {
        var tags = typeTags?.TrimStart(',');
        if (string.IsNullOrEmpty(tags)) return string.Empty;
        if (format == TypeDisplayFormat.TypeTag) return tags;

        return string.Join(", ", tags.Select(TypeName));
    }

    private static string TypeName(char typeTag) => typeTag switch
    {
        'i' => "int32",
        'f' => "float32",
        's' => "string",
        'b' => "blob",
        'h' => "int64",
        't' => "timetag",
        'd' => "float64",
        'c' => "char",
        'r' => "rgba",
        'm' => "midi",
        'T' => "true",
        'F' => "false",
        'N' => "nil",
        'I' => "impulse",
        '[' => "array [",
        ']' => "]",
        _ => typeTag.ToString()
    };
}
