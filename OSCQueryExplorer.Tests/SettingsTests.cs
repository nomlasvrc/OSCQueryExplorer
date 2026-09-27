using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.Tests;

public sealed class SettingsTests
{
    [Fact]
    public async Task SettingsStore_PersistsPinnedPathsAndTypeDisplay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oscquery-explorer-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings { TypeDisplay = TypeDisplayFormat.TypeName };
            settings.Services["HTTP://127.0.0.1:9001"] = new ServiceState
            {
                PinnedPaths = ["/avatar/parameters/GestureLeft"]
            };

            var store = new SettingsStore(path);
            await store.SaveAsync(settings, TestContext.Current.CancellationToken);
            var restored = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(TypeDisplayFormat.TypeName, restored.TypeDisplay);
            Assert.Equal(["/avatar/parameters/GestureLeft"], restored.Services["HTTP://127.0.0.1:9001"].PinnedPaths);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(",f", TypeDisplayFormat.TypeTag, "f")]
    [InlineData("f", TypeDisplayFormat.TypeName, "float32")]
    [InlineData("ih", TypeDisplayFormat.TypeName, "int32, int64")]
    public void TypeFormatter_UsesSelectedDisplay(string tags, TypeDisplayFormat format, string expected) =>
        Assert.Equal(expected, OscTypeFormatter.Format(tags, format));

    [Fact]
    public async Task SettingsStore_NormalizesInvalidUserValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oscquery-explorer-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Theme":99,"TypeDisplay":99,"LogFontSize":100,"PollingIntervalSeconds":0,"Panes":null,"Services":null}""",
                TestContext.Current.CancellationToken);

            var settings = await new SettingsStore(path).LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AppTheme.System, settings.Theme);
            Assert.Equal(TypeDisplayFormat.TypeTag, settings.TypeDisplay);
            Assert.Equal(30, settings.LogFontSize);
            Assert.Equal(1, settings.PollingIntervalSeconds);
            Assert.NotNull(settings.Panes);
            Assert.Empty(settings.Services);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
