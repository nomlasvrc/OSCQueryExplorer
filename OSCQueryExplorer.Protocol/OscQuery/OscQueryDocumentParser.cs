using System.Globalization;
using System.Text.Json.Nodes;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.OscQuery;

public sealed class OscQueryHostInfo
{
    public string? Name { get; init; }
    public string? OscIp { get; init; }
    public int? OscPort { get; init; }
    public string OscTransport { get; init; } = "UDP";
    public string? WebSocketIp { get; init; }
    public int? WebSocketPort { get; init; }
    public IReadOnlyDictionary<string, bool> Extensions { get; init; } = new Dictionary<string, bool>();
}

public static class OscQueryDocumentParser
{
    private static readonly HashSet<string> KnownAttributes = new(StringComparer.Ordinal)
    { "FULL_PATH", "CONTENTS", "TYPE", "VALUE", "ACCESS", "RANGE", "DESCRIPTION", "UNIT" };

    public static OscNode ParseTree(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("OSCQuery response is not a JSON object.");
        return ParseNode(root, "/");
    }

    public static OscQueryHostInfo ParseHostInfo(string json)
    {
        var document = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("HOST_INFO response is not a JSON object.");
        var info = document["HOST_INFO"]?.AsObject() ?? document;
        var extensions = info["EXTENSIONS"]?.AsObject()?.ToDictionary(x => x.Key, x => x.Value?.GetValue<bool>() == true, StringComparer.Ordinal)
            ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        return new OscQueryHostInfo
        {
            Name = StringValue(info["NAME"]),
            OscIp = StringValue(info["OSC_IP"]),
            OscPort = IntValue(info["OSC_PORT"]),
            OscTransport = StringValue(info["OSC_TRANSPORT"]) ?? "UDP",
            WebSocketIp = StringValue(info["WS_IP"]),
            WebSocketPort = IntValue(info["WS_PORT"]),
            Extensions = extensions
        };
    }

    private static OscNode ParseNode(JsonObject json, string fallbackPath)
    {
        var path = StringValue(json["FULL_PATH"]) ?? fallbackPath;
        var node = new OscNode
        {
            FullPath = path,
            TypeTag = StringValue(json["TYPE"])?.TrimStart(','),
            Access = IntValue(json["ACCESS"]) is int access ? (OscAccess)access : null,
            Description = StringValue(json["DESCRIPTION"]),
            Unit = json["UNIT"]?.DeepClone(),
            Observed = ParseValues(json["VALUE"]),
            Ranges = ParseRanges(json["RANGE"]),
            RangeMetadata = json["RANGE"]?.DeepClone()
        };
        foreach (var property in json.Where(x => !KnownAttributes.Contains(x.Key))) node.AdditionalAttributes[property.Key] = property.Value?.DeepClone();
        if (json["CONTENTS"] is JsonObject contents)
        {
            foreach (var child in contents)
            {
                if (child.Value is not JsonObject childObject) continue;
                var childPath = path == "/" ? "/" + child.Key : path.TrimEnd('/') + "/" + child.Key;
                node.Children.Add(ParseNode(childObject, childPath));
            }
        }
        return node;
    }

    private static ObservedValue? ParseValues(JsonNode? value)
    {
        if (value is not JsonArray array) return null;
        return new ObservedValue { Values = array.Select(ParseJsonValue).ToArray(), Origin = ValueOrigin.OscQuery, UpdatedAt = DateTimeOffset.UtcNow };
    }

    private static IReadOnlyList<OscRange> ParseRanges(JsonNode? value)
    {
        if (value is not JsonArray array) return [];
        return array.Select(x => x as JsonObject).Where(x => x is not null).Select(x => new OscRange
        {
            Min = PrimitiveValue(x!["MIN"]),
            Max = PrimitiveValue(x["MAX"]),
            Values = x["VALS"] is JsonArray vals ? vals.Select(PrimitiveValue).ToArray() : []
        }).ToArray();
    }

    private static OscValue ParseJsonValue(JsonNode? value) => PrimitiveValue(value) switch
    {
        int i => new(OscValueKind.Int32, i),
        long l => new(OscValueKind.Int64, l),
        float f => new(OscValueKind.Float32, f),
        double d => new(OscValueKind.Float64, d),
        true => new(OscValueKind.True, true),
        false => new(OscValueKind.False, false),
        string s => new(OscValueKind.String, s),
        null => new(OscValueKind.Nil, null),
        object o => new(OscValueKind.String, Convert.ToString(o, CultureInfo.InvariantCulture))
    };

    private static object? PrimitiveValue(JsonNode? node)
    {
        if (node is not JsonValue value) return node?.ToJsonString();
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<float>(out var f)) return f;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<string>(out var s)) return s;
        return value.ToJsonString();
    }
    private static string? StringValue(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static int? IntValue(JsonNode? value) => value is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
