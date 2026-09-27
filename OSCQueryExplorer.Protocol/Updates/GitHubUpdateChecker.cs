using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSCQueryExplorer.Protocol.Updates;

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? Notes);

public sealed partial class GitHubUpdateChecker : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public GitHubUpdateChecker(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsHttpClient = httpClient is null;
    }

    public async Task<ReleaseInfo?> CheckAsync(string? repositoryUrl, Version currentVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl)) return null;
        var match = RepositoryRegex().Match(repositoryUrl.TrimEnd('/'));
        if (!match.Success) throw new ArgumentException("GitHub repository URL must be https://github.com/{owner}/{repository}.", nameof(repositoryUrl));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{match.Groups[1].Value}/{match.Groups[2].Value}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OSCQueryExplorer", currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= currentVersion) return null;
        var pageUrl = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out _)) throw new FormatException("GitHub release response does not contain a valid page URL.");
        return new(version, tag, pageUrl, root.TryGetProperty("body", out var body) ? body.GetString() : null);
    }

    [GeneratedRegex("^https://github\\.com/([^/]+)/([^/]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryRegex();

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
