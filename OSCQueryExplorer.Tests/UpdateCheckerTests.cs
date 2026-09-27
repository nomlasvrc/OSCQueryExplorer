using System.Net;
using OSCQueryExplorer.Protocol.Updates;

namespace OSCQueryExplorer.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_ReturnsOnlyNewerRelease()
    {
        using var httpClient = new HttpClient(new StubHandler("""{"tag_name":"v0.2.0","html_url":"https://github.com/nomlasvrc/OSCQueryExplorer/releases/tag/v0.2.0","body":"notes"}"""));
        using var checker = new GitHubUpdateChecker(httpClient);

        var release = await checker.CheckAsync(
            "https://github.com/nomlasvrc/OSCQueryExplorer",
            new Version(0, 1, 0),
            TestContext.Current.CancellationToken);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("notes", release.Notes);
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("OSCQueryExplorer", request.Headers.UserAgent.Single().Product?.Name);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }
}
