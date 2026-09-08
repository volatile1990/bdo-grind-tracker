using System.Text.Json;
using Velopack.Sources;

namespace BdoGrindTracker.App.Updates;

/// <summary>
/// Velopack 1.2.0 only reads ten GitHub releases before filtering prereleases.
/// Walk the release pages so a run of beta releases cannot hide stable updates.
/// GitBase still selects the channel feeds and associates packages with their release.
/// </summary>
internal sealed class PaginatedGithubSource(
    string repositoryUrl,
    bool prerelease,
    IFileDownloader? downloader = null) : GithubSource(repositoryUrl, null, prerelease,
        new BoundedFileDownloader(downloader ?? new HttpClientFileDownloader()))
{
    private const int PageSize = 100;
    private const int MaximumPages = 10;

    protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
    {
        var releases = new List<GithubRelease>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            var path = $"repos{RepoUri.AbsolutePath}/releases?per_page={PageSize}&page={page}";
            var uri = new Uri(GetApiBaseUrl(RepoUri), path);
            var json = await Downloader.DownloadString(uri.AbsoluteUri,
                GetRequestHeaders("application/vnd.github+json"), timeout: 0.5).ConfigureAwait(false);
            var batch = JsonSerializer.Deserialize<GithubRelease[]>(json)
                ?? throw new InvalidDataException("GitHub hat keine gültige Versionsliste geliefert.");
            releases.AddRange(batch.Where(release => includePrereleases || !release.Prerelease));
            if (batch.Length < PageSize)
                return releases.OrderByDescending(release => release.PublishedAt).ToArray();
        }

        // Do not silently report 'up to date' from an incomplete release history.
        // Limit unauthenticated API calls even if the server repeats a full page.
        throw new InvalidDataException("Die GitHub-Versionsliste ist zu groß. Bitte die Downloadseite prüfen.");
    }

    // Velopack's default metadata timeout is thirty minutes. Bound both the
    // GitHub API and the inherited channel-feed requests for offline clients.
    private sealed class BoundedFileDownloader(IFileDownloader inner) : IFileDownloader
    {
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            inner.DownloadString(url, headers, Math.Min(timeout, 0.5));

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            inner.DownloadBytes(url, headers, Math.Min(timeout, 0.5));

        public async Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            var minutes = Math.Min(timeout, 15);
            lifetime.CancelAfter(TimeSpan.FromMinutes(minutes));
            await inner.DownloadFile(url, targetFile, progress, headers, minutes, lifetime.Token).ConfigureAwait(false);
        }
    }
}
