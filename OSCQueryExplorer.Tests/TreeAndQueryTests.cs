using System.Text.Json.Nodes;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;
using OSCQueryExplorer.Core.Tree;
using OSCQueryExplorer.Protocol.OscQuery;

namespace OSCQueryExplorer.Tests;

public sealed class TreeAndQueryTests
{
    [Theory]
    [InlineData("VRChat-Client-a1B2c3", true)]
    [InlineData("vrchat-client-ABC123", true)]
    [InlineData("VRChat-Client-ABC12", false)]
    [InlineData("VRChat-Client-ABC1234", false)]
    [InlineData("VRChat-Client-ABC-12", false)]
    [InlineData("Other-Client-ABC123", false)]
    public void VrChatServiceName_RequiresSixAlphanumericCharacters(string name, bool expected)
    {
        Assert.Equal(expected, VrChatNodeMetadata.IsVrChatServiceName(name));
    }

    [Fact]
    public void VrChatDescriptions_AnnotateKnownNodesWithoutOverwritingRemoteMetadata()
    {
        var root = new OscNode { FullPath = "/" };
        var chatbox = new OscNode { FullPath = "/chatbox" };
        chatbox.Children.Add(new OscNode { FullPath = "/chatbox/input", Description = "remote description" });
        chatbox.Children.Add(new OscNode { FullPath = "/chatbox/typing" });
        var parameters = new OscNode { FullPath = "/avatar/parameters" };
        parameters.Children.Add(new OscNode { FullPath = "/avatar/parameters/MyToggle" });
        root.Children.Add(chatbox);
        root.Children.Add(parameters);

        VrChatNodeMetadata.ApplyDescriptions(root);

        Assert.Equal("remote description", chatbox.Children[0].Description);
        Assert.Contains("入力中インジケーター", chatbox.Children[1].Description);
        Assert.Contains("MyToggle", parameters.Children[0].Description);
    }

    [Fact]
    public void VrChatArgumentDescriptions_DescribeEachChatboxInputController()
    {
        Assert.Contains("テキスト", VrChatNodeMetadata.GetArgumentDescription("/chatbox/input", 0));
        Assert.Contains("即時送信", VrChatNodeMetadata.GetArgumentDescription("/chatbox/input", 1));
        Assert.Contains("通知音", VrChatNodeMetadata.GetArgumentDescription("/chatbox/input", 2));
        Assert.Null(VrChatNodeMetadata.GetArgumentDescription("/chatbox/input", 3));
    }

    [Fact]
    public void CustomNode_ReturnsAfterRemoteNodeDisappears()
    {
        var tree = new NodeTree();
        tree.SetCustomNodes([new("/custom/value", "f")]);
        var remote = new OscNode { FullPath = "/" };
        remote.Children.Add(new OscNode { FullPath = "/custom" });
        remote.Children[0].Children.Add(new OscNode { FullPath = "/custom/value", TypeTag = "i" });
        tree.ReplaceRemoteTree(remote);
        Assert.False(tree.Find("/custom/value")!.IsCustom);
        tree.ReplaceRemoteTree(new OscNode { FullPath = "/" });
        Assert.True(tree.Find("/custom/value")!.IsCustom);
        Assert.Equal("f", tree.Find("/custom/value")!.TypeTag);
    }

    [Fact]
    public void SetCustomNodes_RemovesDefinitionsThatNoLongerExist()
    {
        var tree = new NodeTree();
        tree.SetCustomNodes([new("/custom/value", "f")]);

        tree.SetCustomNodes([]);

        Assert.Null(tree.Find("/custom/value"));
        Assert.Null(tree.Find("/custom"));
    }

    [Fact]
    public void FindIndex_TracksRemoteAndCustomStructureChanges()
    {
        var tree = new NodeTree();
        tree.SetCustomNodes([new("/custom/value", "f")]);
        Assert.NotNull(tree.Find("/custom/value"));

        tree.SetCustomNodes([]);
        var remote = new OscNode { FullPath = "/" };
        remote.Children.Add(new OscNode { FullPath = "/remote" });
        tree.ReplaceRemoteTree(remote);

        Assert.Null(tree.Find("/custom/value"));
        Assert.NotNull(tree.Find("/remote"));
    }

    [Fact]
    public void PublicationState_CanBeCapturedAndRestored()
    {
        var tree = new NodeTree();
        var remote = new OscNode { FullPath = "/" };
        remote.Children.Add(new OscNode { FullPath = "/public" });
        remote.Children.Add(new OscNode { FullPath = "/private" });
        tree.ReplaceRemoteTree(remote);
        tree.Find("/private")!.IsPublished = false;

        var unpublished = tree.GetUnpublishedPaths();
        tree.Find("/private")!.IsPublished = true;
        tree.ApplyUnpublishedPaths(unpublished);

        Assert.True(tree.Find("/public")!.IsPublished);
        Assert.False(tree.Find("/private")!.IsPublished);
    }

    [Fact]
    public void UdpValue_IsNotRewoundByAutomaticHttpValue()
    {
        var tree = new NodeTree();
        var udpReceivedAt = DateTimeOffset.UtcNow;
        var old = new OscNode { FullPath = "/" };
        old.Children.Add(new OscNode { FullPath = "/x", Observed = new() { Origin = ValueOrigin.UdpReceived, UpdatedAt = udpReceivedAt, Values = [new(OscValueKind.Int32, 2)] } });
        tree.ReplaceRemoteTree(old);
        var refreshed = new OscNode { FullPath = "/" };
        refreshed.Children.Add(new OscNode { FullPath = "/x", Observed = new() { Origin = ValueOrigin.OscQuery, UpdatedAt = udpReceivedAt.AddSeconds(1), Values = [new(OscValueKind.Int32, 1)] } });
        tree.ReplaceRemoteTree(refreshed);
        Assert.Equal(2, tree.Find("/x")!.Observed!.Values[0].Value);
    }

    [Fact]
    public void Parser_PreservesUnknownMetadata()
    {
        var root = OscQueryDocumentParser.ParseTree("""{"FULL_PATH":"/","CONTENTS":{"x":{"FULL_PATH":"/x","TYPE":"f","VENDOR_DATA":42}}}""");
        Assert.Equal(42, root.Children[0].AdditionalAttributes["VENDOR_DATA"]!.GetValue<int>());
    }

    [Fact]
    public void Parser_PreservesStructuredRangeAndUnitMetadata()
    {
        var root = OscQueryDocumentParser.ParseTree("""{"FULL_PATH":"/","CONTENTS":{"x":{"FULL_PATH":"/x","TYPE":"ff","RANGE":[{"MIN":0,"MAX":1},{"VALS":[0.25,0.5]}],"UNIT":["normalized","ratio"]}}}""");
        var node = root.Children[0];

        Assert.Equal(2, node.RangeMetadata!.AsArray().Count);
        Assert.Equal(0.5, node.RangeMetadata.AsArray()[1]!["VALS"]![1]!.GetValue<double>());
        Assert.Equal("ratio", node.Unit!.AsArray()[1]!.GetValue<string>());
    }

    [Fact]
    public async Task LocalServer_ReturnsFlatHostInfo()
    {
        var root = new OscNode { FullPath = "/" };
        await using var server = new LocalOscQueryServer(() => 19001);
        server.UpdateTree(root);
        await server.StartAsync(false, TestContext.Current.CancellationToken);
        using var client = new HttpClient();

        var json = JsonNode.Parse(await client.GetStringAsync(
            $"http://127.0.0.1:{server.Port}/?HOST_INFO", TestContext.Current.CancellationToken))!.AsObject();

        Assert.Equal("OSCQuery Explorer", json["NAME"]!.GetValue<string>());
        Assert.Equal(19001, json["OSC_PORT"]!.GetValue<int>());
        Assert.False(json.ContainsKey("HOST_INFO"));
    }

    [Fact]
    public async Task LocalServer_PreservesAccessAndHidesUnpublishedMetadata()
    {
        var root = new OscNode { FullPath = "/" };
        var parent = new OscNode { FullPath = "/parent", IsPublished = false, Description = "private" };
        parent.Children.Add(new OscNode { FullPath = "/parent/value", TypeTag = "f", Access = OscAccess.Read });
        root.Children.Add(parent);
        await using var server = new LocalOscQueryServer(() => 19001);
        server.UpdateTree(root);
        await server.StartAsync(false, TestContext.Current.CancellationToken);
        using var client = new HttpClient();

        var parentJson = JsonNode.Parse(await client.GetStringAsync($"http://127.0.0.1:{server.Port}/parent", TestContext.Current.CancellationToken))!.AsObject();
        var valueJson = JsonNode.Parse(await client.GetStringAsync($"http://127.0.0.1:{server.Port}/parent/value", TestContext.Current.CancellationToken))!.AsObject();

        Assert.False(parentJson.ContainsKey("DESCRIPTION"));
        Assert.Equal((int)OscAccess.Read, valueJson["ACCESS"]!.GetValue<int>());
    }

    [Fact]
    public void SliderRange_MostSpecificPathWins()
    {
        SliderRangeRule[] rules =
        [
            new() { PathPrefix = "/avatar/parameters", Minimum = 0, Maximum = 1, TypeTags = "ifhd" },
            new() { PathPrefix = "/avatar/parameters/eyeheight", Minimum = 0.01, Maximum = 10, TypeTags = "f" }
        ];

        Assert.True(SliderRangeResolver.TryResolve(rules, "/avatar/parameters/EyeHeight", 'f', out var minimum, out var maximum));
        Assert.Equal(0.01, minimum);
        Assert.Equal(10, maximum);
        Assert.True(SliderRangeResolver.TryResolve(rules, "/avatar/parameters/GestureLeft", 'f', out minimum, out maximum));
        Assert.Equal(0, minimum);
        Assert.Equal(1, maximum);
        Assert.True(SliderRangeResolver.TryResolve(rules, "/avatar/parameters/EyeHeight", 'i', out minimum, out maximum));
        Assert.Equal(0, minimum);
        Assert.Equal(1, maximum);
    }

    [Fact]
    public void SliderRange_WithoutSelectedTypesDoesNotApply()
    {
        SliderRangeRule[] rules = [new() { PathPrefix = "/avatar", Minimum = -1, Maximum = 1 }];

        Assert.False(SliderRangeResolver.TryResolve(rules, "/avatar/value", 'f', out _, out _));
    }

    [Fact]
    public void RemoteRefresh_PreservesExpansionAndSelection()
    {
        var tree = new NodeTree();
        var initial = new OscNode { FullPath = "/" };
        initial.Children.Add(new OscNode { FullPath = "/avatar", IsExpanded = true, IsSelected = true });
        tree.ReplaceRemoteTree(initial);

        var refreshed = new OscNode { FullPath = "/" };
        refreshed.Children.Add(new OscNode { FullPath = "/avatar" });
        tree.ReplaceRemoteTree(refreshed);

        Assert.True(tree.Find("/avatar")!.IsExpanded);
        Assert.True(tree.Find("/avatar")!.IsSelected);
    }

    [Fact]
    public void RemoteRefresh_ReusesNodesWhenStructureIsUnchanged()
    {
        var tree = new NodeTree();
        var initial = new OscNode { FullPath = "/" };
        initial.Children.Add(new OscNode { FullPath = "/avatar", TypeTag = "f", Description = "before" });
        Assert.True(tree.ReplaceRemoteTree(initial));
        var originalNode = tree.Find("/avatar")!;

        var refreshed = new OscNode { FullPath = "/" };
        refreshed.Children.Add(new OscNode { FullPath = "/avatar", TypeTag = "i", Description = "after" });

        Assert.False(tree.ReplaceRemoteTree(refreshed));
        Assert.Same(originalNode, tree.Find("/avatar"));
        Assert.Equal("i", originalNode.TypeTag);
        Assert.Equal("after", originalNode.Description);
        Assert.Equal(NodeChangeKind.TypeChanged, originalNode.ChangeKind);
    }

    [Fact]
    public void Search_PreservesHierarchyAndRestoresExpansion()
    {
        var tree = new NodeTree();
        var root = new OscNode { FullPath = "/" };
        var avatar = new OscNode { FullPath = "/avatar", IsExpanded = false };
        avatar.Children.Add(new OscNode { FullPath = "/avatar/parameters" });
        avatar.Children[0].Children.Add(new OscNode { FullPath = "/avatar/parameters/VelocityX", TypeTag = "f" });
        avatar.Children[0].Children.Add(new OscNode { FullPath = "/avatar/parameters/GestureLeft", TypeTag = "f" });
        root.Children.Add(avatar);
        tree.ReplaceRemoteTree(root);

        var results = tree.Search("velocity");

        Assert.Single(results);
        Assert.Equal("/avatar", results[0].FullPath);
        Assert.Single(results[0].DisplayChildren[0].DisplayChildren);
        Assert.Equal("/avatar/parameters/VelocityX", results[0].DisplayChildren[0].DisplayChildren[0].FullPath);
        Assert.True(results[0].IsExpanded);

        tree.Search("");
        Assert.False(tree.Find("/avatar")!.IsExpanded);
        Assert.Equal(2, tree.Find("/avatar/parameters")!.DisplayChildren.Count);
    }
}
