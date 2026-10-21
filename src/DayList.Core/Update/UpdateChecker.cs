using System.Net.Http;

namespace DayList.Core;

/// <summary>A newer GitHub release that ships a DayList installer.</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string PageUrl);

/// <summary>
/// Reads the newest release by following <c>/releases/latest</c> (no GitHub API rate limit).
/// Releases must be tagged <c>vX.Y.Z</c> and carry a <c>DayList-Setup-X.Y.Z.exe</c> asset.
/// </summary>
public static class UpdateChecker
{
    public const string Owner = "wengqian66";
    public const string Repo = "DayList";

    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        try
        {
            // The redirect carries the newest tag; GitHub's REST API is rate-limited, this is not.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DayList");

            using var response = await client.GetAsync(
                $"https://github.com/{Owner}/{Repo}/releases/latest",
                cancellationToken).ConfigureAwait(false);

            var location = response.Headers.Location;
            if (location is null)
                return null;

            var tag = location.Segments.LastOrDefault()?.Trim('/');
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var latest = ParseVersion(tag);
            if (latest is null || latest <= current)
                return null;

            var version = latest.ToString();
            return new UpdateInfo(
                version,
                $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/DayList-Setup-{version}.exe",
                $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}");
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }
}
