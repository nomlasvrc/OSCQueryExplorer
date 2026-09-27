using OSCQueryExplorer.Core.History;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void RingBuffer_RemovesOldest()
    {
        var store = new HistoryStore(2);
        for (var i = 0; i < 3; i++) store.Add(id => new(id, DateTimeOffset.Now, HistoryKind.System, $"item{i}", Level: SystemLevel.Info));
        Assert.Equal(["item1", "item2"], store.Snapshot().Select(x => x.Message));
    }

    [Fact]
    public void RecordOff_KeepsSystemAndSkipsOsc()
    {
        var store = new HistoryStore { RecordOsc = false };
        store.Add(id => new(id, DateTimeOffset.Now, HistoryKind.Osc, "", Address: "/a"));
        store.Add(id => new(id, DateTimeOffset.Now, HistoryKind.System, "error", Level: SystemLevel.Error));
        var entry = Assert.Single(store.Snapshot());
        Assert.Equal(1, entry.Id);
    }

    [Fact]
    public void SystemWarning_UsesCompactWarnLabel()
    {
        var item = new HistoryEntry(1, DateTimeOffset.Now, HistoryKind.System, "warning", Level: SystemLevel.Warning);
        Assert.Contains("[SYSTEM][WARN]", item.DisplayText);
    }

    [Fact]
    public void OscLog_TypeDisplayFollowsSelectedFormat()
    {
        var item = new HistoryEntry(1, DateTimeOffset.Now, HistoryKind.Osc, "", OscDirection.Received, 12, 9001, "/value", ",fd");

        Assert.Contains("  fd  /value", item.FormatDisplayText(TypeDisplayFormat.TypeTag));
        Assert.Contains("  float32, float64  /value", item.FormatDisplayText(TypeDisplayFormat.TypeName));
        Assert.True(LogFilter.Parse("type:float32").IsMatch(item, TypeDisplayFormat.TypeName));
    }

    [Theory]
    [InlineData("-Velocity", false)]
    [InlineData("Gesture -Velocity", false)]
    [InlineData("port:9001", true)]
    [InlineData("-port:9001", false)]
    [InlineData("src:12 addr:/input/ type:f", true)]
    [InlineData("level:ERROR", false)]
    public void Filter_UsesFieldsAndExclusions(string expression, bool expected)
    {
        var item = new HistoryEntry(1, DateTimeOffset.Now, HistoryKind.Osc, "", OscDirection.Received, 12, 9001, "/input/Velocity", ",f", [new(OscValueKind.Float32, 1f)]);
        Assert.Equal(expected, LogFilter.Parse(expression).IsMatch(item));
    }
}
