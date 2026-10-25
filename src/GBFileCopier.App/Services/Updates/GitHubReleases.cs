using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GBFileCopier.App.Services.Updates;

/// <summary>A file of a release.</summary>
public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>A published release: its version, its page and its files.</summary>
public sealed record ReleaseInfo(UpdateVersion Version, Uri Page, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Find(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads the latest release from the GitHub API. Only this request and the downloads go to the
/// network, and only when the user asked for updates (see UpdateViewModel).
/// </summary>
public sealed class GitHubReleases(HttpClient http, string repository = GitHubReleases.Repository)
{
    public const string Repository = "giuliobanterla/GBFileCopier";

    /// <summary>The latest published release (GitHub leaves out drafts and prereleases); null if there is none.</summary>
    /// <exception cref="HttpRequestException">The network or GitHub failed.</exception>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{repository}/releases/latest"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GitHubRelease? release;
        try
        {
            release = await JsonSerializer.DeserializeAsync(stream, GitHubJsonContext.Default.GitHubRelease, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Unexpected answer from GitHub", ex);
        }

        return release is null ? null : ToReleaseInfo(release);
    }

    private ReleaseInfo? ToReleaseInfo(GitHubRelease release)
    {
        if (release.Draft || release.Prerelease || UpdateVersion.Parse(release.TagName) is not { } version
            || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page))
        {
            return null;
        }

        // Only files of this repository's releases, over HTTPS: never follow a link to anywhere else
        var prefix = $"https://github.com/{repository}/releases/download/";
        var assets = release.Assets
            .Where(a => a.Name is { Length: > 0 } && a.DownloadUrl is not null
                        && a.DownloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(a => new ReleaseAsset(a.Name!, new Uri(a.DownloadUrl!), a.Size))
            .ToList();
        return new ReleaseInfo(version, page, assets);
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>Source-generated serialization: no reflection, works with Native AOT.</summary>
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
