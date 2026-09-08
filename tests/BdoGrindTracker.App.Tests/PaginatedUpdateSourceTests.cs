using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Updates;
using Velopack.Logging;
using Velopack.Sources;

namespace BdoGrindTracker.App.Tests;

public sealed class PaginatedUpdateSourceTests
{
    private const string Repository = "https://github.com/example/grindcrest";
    private static readonly NullVelopackLogger Log = new();

    [Fact]
    public async Task StableUpdateRemainsVisibleAfterAFullPageOfBetaReleases()
    {
        var downloader = new FakeDownloader();
        downloader.AddPage(1, Enumerable.Range(1, 100)
            .Select(index => Release($"beta-{index}", true, "win-beta")).ToArray());
        downloader.AddPage(2, Release("stable", false, "win"));
        downloader.AddFeed("stable", "win", "1.1.0");
        var source = new PaginatedGithubSource(Repository, false, downloader);

        var feed = await source.GetReleaseFeed(Log, "Grindcrest", "win");

        Assert.Equal("1.1.0", Assert.Single(feed.Assets).Version.ToString());
        Assert.Equal(2, downloader.ApiCalls.Count);
        Assert.Equal([FeedUrl("stable", "win")], downloader.FeedCalls);
        Assert.All(downloader.Headers, headers => Assert.DoesNotContain("Authorization", headers.Keys));
        Assert.All(downloader.MetadataTimeouts, minutes => Assert.Equal(0.5, minutes));
    }

    [Fact]
    public async Task BetaChannelSkipsNewerStableReleasesWithoutItsFeed()
    {
        var downloader = new FakeDownloader();
        downloader.AddPage(1, Release("stable", false, "win"), Release("beta", true, "win-beta"));
        downloader.AddFeed("beta", "win-beta", "1.2.0-beta.1");
        var source = new PaginatedGithubSource(Repository, true, downloader);

        var feed = await source.GetReleaseFeed(Log, "Grindcrest", "win-beta");

        Assert.Equal("1.2.0-beta.1", Assert.Single(feed.Assets).Version.ToString());
        Assert.Equal([FeedUrl("beta", "win-beta")], downloader.FeedCalls);
    }

    [Fact]
    public async Task AggregatedPackagesDownloadFromTheirOwnRelease()
    {
        var downloader = new FakeDownloader();
        downloader.AddPage(1, Release("recent", false, "win"), Release("older", false, "win"));
        downloader.AddFeed("recent", "win", "1.1.0");
        downloader.AddFeed("older", "win", "2.0.0");
        var source = new PaginatedGithubSource(Repository, false, downloader);

        var feed = await source.GetReleaseFeed(Log, "Grindcrest", "win");
        var highest = feed.Assets.MaxBy(asset => asset.Version)!;
        await source.DownloadReleaseEntry(Log, highest, "unused.nupkg", _ => { }, CancellationToken.None);

        Assert.Equal(2, feed.Assets.Length);
        Assert.Equal("2.0.0", highest.Version.ToString());
        Assert.Equal($"{Repository}/releases/download/older/Grindcrest-2.0.0-full.nupkg",
            Assert.Single(downloader.PackageCalls));
    }

    [Fact]
    public async Task HttpFailureOnLaterPageIsNotMisreportedAsNoUpdate()
    {
        var downloader = new FakeDownloader();
        downloader.AddPage(1, Enumerable.Range(1, 100).Select(index => Release($"beta-{index}", true, "win-beta")).ToArray());
        downloader.Failure = new HttpRequestException("GitHub rate limit");
        var source = new PaginatedGithubSource(Repository, false, downloader);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetReleaseFeed(Log, "Grindcrest", "win"));
        Assert.Equal(2, downloader.ApiCalls.Count);
    }

    [Fact]
    public async Task RepeatedFullPagesHaveBoundedRequestsAndAnExplicitError()
    {
        var downloader = new FakeDownloader();
        var page = Enumerable.Range(1, 100).Select(index => Release($"beta-{index}", true, "win-beta")).ToArray();
        for (var index = 1; index <= 10; index++) downloader.AddPage(index, page);
        var source = new PaginatedGithubSource(Repository, false, downloader);

        await Assert.ThrowsAsync<InvalidDataException>(() => source.GetReleaseFeed(Log, "Grindcrest", "win"));
        Assert.Equal(10, downloader.ApiCalls.Count);
        Assert.Empty(downloader.FeedCalls);
    }

    private static GithubRelease Release(string name, bool prerelease, string channel) => new()
    {
        Name = name,
        Prerelease = prerelease,
        PublishedAt = DateTime.UtcNow,
        Assets =
        [
            new GithubReleaseAsset { Name = $"releases.{channel}.json", BrowserDownloadUrl = FeedUrl(name, channel) },
            new GithubReleaseAsset { Name = "Grindcrest-1.1.0-full.nupkg", BrowserDownloadUrl = $"{Repository}/releases/download/{name}/Grindcrest-1.1.0-full.nupkg" },
            new GithubReleaseAsset { Name = "Grindcrest-2.0.0-full.nupkg", BrowserDownloadUrl = $"{Repository}/releases/download/{name}/Grindcrest-2.0.0-full.nupkg" }
        ]
    };

    private static string FeedUrl(string release, string channel) =>
        $"{Repository}/releases/download/{release}/releases.{channel}.json";

    private sealed class FakeDownloader : IFileDownloader
    {
        private readonly Dictionary<string, string> _pages = new();
        private readonly Dictionary<string, byte[]> _feeds = new();
        public List<string> ApiCalls { get; } = [];
        public List<string> FeedCalls { get; } = [];
        public List<string> PackageCalls { get; } = [];
        public List<IDictionary<string, string>> Headers { get; } = [];
        public List<double> MetadataTimeouts { get; } = [];
        public Exception? Failure { get; set; }

        public void AddPage(int page, params GithubRelease[] releases) =>
            _pages[$"https://api.github.com/repos/example/grindcrest/releases?per_page=100&page={page}"] =
                JsonSerializer.Serialize(releases);

        public void AddFeed(string release, string channel, string version) =>
            _feeds[FeedUrl(release, channel)] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                Assets = new[] { new { PackageId = "Grindcrest", Version = version, Type = "Full",
                    FileName = $"Grindcrest-{version}-full.nupkg", SHA1 = "ABC", SHA256 = "ABC", Size = 123 } }
            }));

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            ApiCalls.Add(url);
            MetadataTimeouts.Add(timeout);
            Headers.Add(headers ?? new Dictionary<string, string>());
            return _pages.TryGetValue(url, out var page) ? Task.FromResult(page)
                : Task.FromException<string>(Failure ?? new InvalidOperationException($"Unexpected URL: {url}"));
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            FeedCalls.Add(url);
            MetadataTimeouts.Add(timeout);
            Headers.Add(headers ?? new Dictionary<string, string>());
            return Task.FromResult(_feeds[url]);
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            PackageCalls.Add(url);
            Headers.Add(headers ?? new Dictionary<string, string>());
            return Task.CompletedTask;
        }
    }
}
