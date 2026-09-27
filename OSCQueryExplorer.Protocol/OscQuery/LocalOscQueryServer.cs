using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.OscQuery;

public sealed class LocalOscQueryServer : IAsyncDisposable
{
    private readonly Func<int> _oscPortProvider;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private IReadOnlyDictionary<string, PublishedNode> _nodes = new Dictionary<string, PublishedNode>(StringComparer.Ordinal);
    private WebApplication? _app;
    public int Port { get; private set; }
    public IPAddress ListenAddress { get; private set; } = IPAddress.Loopback;

    public LocalOscQueryServer(Func<int> oscPortProvider) => _oscPortProvider = oscPortProvider;

    public void UpdateTree(OscNode root)
    {
        var nodes = new Dictionary<string, PublishedNode>(StringComparer.Ordinal);
        BuildSnapshot(root, nodes);
        _nodes = nodes;
    }

    public async Task StartAsync(bool publishToLan, CancellationToken cancellationToken)
    {
        if (_app is not null) return;
        ListenAddress = publishToLan ? IPAddress.Any : IPAddress.Loopback;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(ListenAddress, 0));
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(HandleRequestAsync);
        await app.StartAsync(cancellationToken);
        Port = app.Urls.Select(x => new Uri(x).Port).FirstOrDefault();
        _app = app;
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var id = Guid.NewGuid(); _sockets[id] = socket;
            try { await DrainSocketAsync(socket, context.RequestAborted); }
            finally { _sockets.TryRemove(id, out _); socket.Dispose(); }
            return;
        }
        context.Response.ContentType = "application/json; charset=utf-8";
        if (context.Request.Query.ContainsKey("HOST_INFO"))
        {
            var hostInfo = new JsonObject
            {
                ["NAME"] = "OSCQuery Explorer",
                ["OSC_IP"] = ListenAddress.Equals(IPAddress.Loopback) ? "127.0.0.1" : null,
                ["OSC_PORT"] = _oscPortProvider(),
                ["OSC_TRANSPORT"] = "UDP",
                ["EXTENSIONS"] = new JsonObject
                {
                    ["ACCESS"] = true,
                    ["VALUE"] = true,
                    ["RANGE"] = true,
                    ["DESCRIPTION"] = true,
                    ["UNIT"] = true,
                    ["PATH_CHANGED"] = true
                }
            };
            await context.Response.WriteAsync(hostInfo.ToJsonString());
            return;
        }
        var path = context.Request.Path.Value ?? "/";
        var nodes = _nodes;
        var node = nodes.GetValueOrDefault(path);
        if (node is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        await context.Response.WriteAsync(SerializeNode(node).ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static PublishedNode? BuildSnapshot(OscNode node, IDictionary<string, PublishedNode> nodes)
    {
        var children = node.Children.Select(child => BuildSnapshot(child, nodes)).Where(child => child is not null).Cast<PublishedNode>().ToArray();
        if (!node.IsPublished && children.Length == 0) return null;

        var snapshot = new PublishedNode(
            node.FullPath,
            node.IsPublished,
            node.TypeTag,
            node.Access,
            node.Description,
            node.Unit?.DeepClone(),
            node.RangeMetadata?.DeepClone(),
            node.Observed?.Values.ToArray(),
            node.AdditionalAttributes.ToDictionary(attribute => attribute.Key, attribute => attribute.Value?.DeepClone(), StringComparer.Ordinal),
            children);
        nodes[snapshot.FullPath] = snapshot;
        return snapshot;
    }

    private static JsonObject SerializeNode(PublishedNode node)
    {
        var json = new JsonObject { ["FULL_PATH"] = node.FullPath };
        if (node.TypeTag is not null && node.IsPublished) json["TYPE"] = node.TypeTag;
        if (node.Access is not null && node.IsPublished) json["ACCESS"] = (int)node.Access.Value;
        if (node.Description is not null && node.IsPublished) json["DESCRIPTION"] = node.Description;
        if (node.Unit is not null && node.IsPublished) json["UNIT"] = node.Unit.DeepClone();
        if (node.RangeMetadata is not null && node.IsPublished) json["RANGE"] = node.RangeMetadata.DeepClone();
        if (node.Values is not null && node.IsPublished) json["VALUE"] = new JsonArray(node.Values.Select(ValueToJson).ToArray());
        var children = new JsonObject();
        foreach (var child in node.Children) children[NameOf(child.FullPath)] = SerializeNode(child);
        if (children.Count > 0) json["CONTENTS"] = children;
        if (node.IsPublished)
            foreach (var extra in node.AdditionalAttributes.Where(x => !json.ContainsKey(x.Key))) json[extra.Key] = extra.Value?.DeepClone();
        return json;
    }

    private static JsonNode? ValueToJson(OscValue value) => value.Kind switch
    {
        OscValueKind.Int32 => JsonValue.Create(Convert.ToInt32(value.Value)),
        OscValueKind.Int64 => JsonValue.Create(Convert.ToInt64(value.Value)),
        OscValueKind.Float32 => JsonValue.Create(Convert.ToSingle(value.Value)),
        OscValueKind.Float64 => JsonValue.Create(Convert.ToDouble(value.Value)),
        OscValueKind.True => JsonValue.Create(true),
        OscValueKind.False => JsonValue.Create(false),
        OscValueKind.Nil or OscValueKind.Impulse => null,
        OscValueKind.Blob => JsonValue.Create(Convert.ToBase64String((byte[])value.Value!)),
        OscValueKind.Array => new JsonArray(((IEnumerable<OscValue>)value.Value!).Select(ValueToJson).ToArray()),
        _ => JsonValue.Create(Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture))
    };
    private static string NameOf(string path) => path == "/" ? "/" : path.TrimEnd('/').Split('/').Last();

    public async Task NotifyPathChangedAsync(string path, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { COMMAND = "PATH_CHANGED", DATA = path }));
        foreach (var pair in _sockets.ToArray())
        {
            try { if (pair.Value.State == WebSocketState.Open) await pair.Value.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken); }
            catch { _sockets.TryRemove(pair.Key, out _); }
        }
    }

    private static async Task DrainSocketAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        { var result = await socket.ReceiveAsync(buffer, token); if (result.MessageType == WebSocketMessageType.Close) break; }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var socket in _sockets.Values)
        {
            socket.Abort();
            socket.Dispose();
        }
        _sockets.Clear();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); _app = null; }
    }

    private sealed record PublishedNode(
        string FullPath,
        bool IsPublished,
        string? TypeTag,
        OscAccess? Access,
        string? Description,
        JsonNode? Unit,
        JsonNode? RangeMetadata,
        IReadOnlyList<OscValue>? Values,
        IReadOnlyDictionary<string, JsonNode?> AdditionalAttributes,
        IReadOnlyList<PublishedNode> Children);
}
