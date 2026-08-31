using System.Net;
using System.Text;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UpdateCheckServiceTests : IDisposable
{
    private const string ApplicationName = "TestTrayApp";
    private const string LatestReleaseTag = "TrayAppDotNET_110";
    private const string PreviousReleaseTag = "TrayAppDotNET_109";
    private const int CurrentBuild = 100;

    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        nameof(UpdateCheckServiceTests),
        Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_testDirectory, GitHubReleaseUrls.VersionsManifestFileName);

    [Fact]
    public async Task CheckNowAsync_IgnoresOnlyThePersistedSkippedRelease()
    {
        int skippedUpdateVersion = 200;
        using ManifestMessageHandler messageHandler = new(200, 201);
        using UpdateCheckService service = CreateService(
            messageHandler,
            () => skippedUpdateVersion,
            version => skippedUpdateVersion = version);

        UpdateInfo? skippedUpdate = await service.CheckNowAsync();
        UpdateInfo? nextUpdate = await service.CheckNowAsync();

        Assert.Null(skippedUpdate);
        Assert.NotNull(nextUpdate);
        Assert.Equal(expected: 201, nextUpdate.Version);
        Assert.Equal(expected: 200, service.SkippedUpdateVersion);
        Assert.Equal(expected: 2, messageHandler.RequestCount);
    }

    [Fact]
    public async Task ScheduledCheck_UsesFreshSharedManifestAndWaitsOnlyUntilItExpires()
    {
        const int cachedVersion = 200;
        DateTime currentTimeUTC = new(year: 2026, month: 8, day: 20, hour: 12, minute: 0, second: 0, DateTimeKind.Utc);
        TimeSpan pollInterval = TimeSpan.FromHours(1);
        WriteCachedManifest(cachedVersion, currentTimeUTC - TimeSpan.FromMinutes(45));
        using ManifestMessageHandler messageHandler = new(999);
        using UpdateCheckService service = CreateService(
            messageHandler,
            static () => 0,
            static _ => { },
            pollInterval: () => pollInterval,
            getCurrentUTCTime: () => currentTimeUTC);

        await service.PollOnceAsync(CancellationToken.None, bypassLatestManifestCache: false);
        UpdateInfo? update = service.AvailableUpdate;
        TimeSpan nextPollInterval = service.NextPollInterval();

        Assert.NotNull(update);
        Assert.Equal(cachedVersion, update.Version);
        Assert.Equal(expected: 0, messageHandler.RequestCount);
        Assert.Equal(TimeSpan.FromMinutes(15), nextPollInterval);
    }

    [Fact]
    public async Task ScheduledCheck_RefreshesStaleSharedManifestAndCachesTheResponse()
    {
        const int cachedVersion = 150;
        const int receivedVersion = 200;
        DateTime staleWriteTimeUTC = DateTime.UtcNow - TimeSpan.FromHours(2);
        WriteCachedManifest(cachedVersion, staleWriteTimeUTC);
        using ManifestMessageHandler messageHandler = new(receivedVersion);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        await service.PollOnceAsync(CancellationToken.None, bypassLatestManifestCache: false);
        UpdateInfo? update = service.AvailableUpdate;
        string cachedManifest = await File.ReadAllTextAsync(CachePath);

        Assert.NotNull(update);
        Assert.Equal(receivedVersion, update.Version);
        Assert.Equal(expected: 1, messageHandler.RequestCount);
        Assert.Contains($"version=\"{receivedVersion}\"", cachedManifest);
        Assert.True(File.GetLastWriteTimeUtc(CachePath) > staleWriteTimeUTC);
        Assert.Equal(TimeSpan.FromHours(1), service.NextPollInterval());
    }

    [Fact]
    public async Task CheckNowAsync_BypassesFreshSharedManifestAndCachesTheResponse()
    {
        const int cachedVersion = 150;
        const int receivedVersion = 200;
        WriteCachedManifest(cachedVersion, DateTime.UtcNow);
        using ManifestMessageHandler messageHandler = new(receivedVersion);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? update = await service.CheckNowAsync();
        string cachedManifest = await File.ReadAllTextAsync(CachePath);

        Assert.NotNull(update);
        Assert.Equal(receivedVersion, update.Version);
        Assert.Equal(expected: 1, messageHandler.RequestCount);
        Assert.Contains($"version=\"{receivedVersion}\"", cachedManifest);
        Assert.Equal(TimeSpan.FromHours(1), service.NextPollInterval());
    }

    [Fact]
    public async Task CheckNowAsync_UsesTheAppVersionAndPinsTheAssetToTheManifestRelease()
    {
        const int aggregateVersion = 110;
        const int appVersion = 200;
        using AggregateManifestMessageHandler messageHandler = new(aggregateVersion, appVersion, includeApp: true);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? update = await service.CheckNowAsync();

        Assert.NotNull(update);
        Assert.Equal(appVersion, update.Version);
        Assert.Equal($"TrayAppDotNET_{aggregateVersion}", update.TagName);
        Assert.Equal($"{ApplicationName} {appVersion}", update.ReleaseName);
        Assert.Equal(
            $"https://github.com/test-owner/test-repository/releases/download/TrayAppDotNET_{aggregateVersion}/"
            + $"{ApplicationName}_{appVersion}.zip",
            update.AssetUrl);
    }

    [Fact]
    public async Task CheckNowAsync_UsesTheArtifactReleaseTagFromTheVersionEndpoint()
    {
        const int aggregateVersion = 110;
        const int appVersion = 200;
        const string appReleaseTag = "TrayAppDotNET_108";
        using AggregateManifestMessageHandler messageHandler = new(
            aggregateVersion,
            appVersion,
            includeApp: true,
            appReleaseTag);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? update = await service.CheckNowAsync();

        Assert.NotNull(update);
        Assert.Equal(appReleaseTag, update.TagName);
        Assert.Equal(
            $"https://github.com/test-owner/test-repository/releases/download/{appReleaseTag}/"
            + $"{ApplicationName}_{appVersion}.zip",
            update.AssetUrl);
    }

    [Fact]
    public async Task CheckNowAsync_DoesNotOfferTheAggregateVersionWhenTheAppIsCurrent()
    {
        const int aggregateVersion = 999;
        using AggregateManifestMessageHandler messageHandler = new(
            aggregateVersion,
            CurrentBuild,
            includeApp: true);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? update = await service.CheckNowAsync();

        Assert.Null(update);
        Assert.Equal(UpdateCheckResult.Success, service.LastResult);
    }

    [Fact]
    public async Task CheckNowAsync_DoesNotFallBackToGitHubWhenTheVersionEndpointOmitsTheApp()
    {
        const int aggregateVersion = 110;
        const int appVersion = 200;
        using AggregateManifestMessageHandler messageHandler = new(aggregateVersion, appVersion, includeApp: false);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? update = await service.CheckNowAsync();

        Assert.Null(update);
        Assert.Equal(["https://updates.test/versions.xml"], messageHandler.RequestUrls);
    }

    [Fact]
    public async Task SkipReleaseAsync_PersistsAndClearsTheAvailableRelease()
    {
        int skippedUpdateVersion = 0;
        int persistCount = 0;
        using ManifestMessageHandler messageHandler = new(200, 200);
        using UpdateCheckService service = CreateService(
            messageHandler,
            () => skippedUpdateVersion,
            version =>
            {
                skippedUpdateVersion = version;
                persistCount++;
            });

        UpdateInfo? availableUpdate = await service.CheckNowAsync();
        Assert.NotNull(availableUpdate);

        int stateChangeCount = 0;
        service.StateChanged += () => stateChangeCount++;
        await service.SkipReleaseAsync(availableUpdate);

        Assert.Equal(expected: 200, skippedUpdateVersion);
        Assert.Equal(expected: 1, persistCount);
        Assert.Equal(expected: 1, stateChangeCount);
        Assert.Null(service.AvailableUpdate);
        Assert.Equal(expected: 200, service.SkippedUpdateVersion);

        UpdateInfo? repeatedUpdate = await service.CheckNowAsync();
        Assert.Null(repeatedUpdate);
    }

    [Fact]
    public async Task GetPreviousReleaseAsync_UsesThePreviousIndexedTagWithoutListingReleases()
    {
        using PreviousReleaseMessageHandler messageHandler = new(true);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();
        UpdateInfo? cachedPreviousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Same(previousRelease, cachedPreviousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal(expected: "TrayAppDotNET_109", previousRelease.TagName);
        Assert.Equal(
            $"https://github.com/test-owner/test-repository/releases/download/TrayAppDotNET_109/"
            + $"{ApplicationName}_{CurrentBuild - 1}.zip",
            previousRelease.AssetUrl);
        Assert.Equal(
            [
                "https://updates.test/versions.xml",
                "https://github.com/test-owner/test-repository/releases/download/TrayAppDotNET_109/versions.xml"
            ],
            messageHandler.RequestUrls);
    }

    [Fact]
    public async Task GetPreviousReleaseAsync_ListsReleasesOnlyWhenThePreviousIndexedTagIsUnavailable()
    {
        using PreviousReleaseMessageHandler messageHandler = new(false);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal(expected: "TrayAppDotNET_109", previousRelease.TagName);
        Assert.Equal(expected: 123L, previousRelease.AssetSize);
        Assert.Equal(expected: 3, messageHandler.RequestUrls.Count);
        Assert.Equal(
            expected: "https://api.github.com/repos/test-owner/test-repository/releases?per_page=10&page=1",
            messageHandler.RequestUrls[2]);
        Assert.Equal(expected: "2026-03-10", messageHandler.GitHubApiVersion);
        Assert.True(messageHandler.RequestedGitHubJSON);
    }

    [Fact]
    public async Task GetPreviousReleaseAsync_DerivesThePinnedTagWhenTheRunningBuildIsBehindLatest()
    {
        using PreviousReleaseMessageHandler messageHandler = new(
            directManifestAvailable: true,
            CurrentBuild + 1,
            latestTag: "TrayAppDotNET_110",
            directTag: "TrayAppDotNET_108");
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal(expected: "TrayAppDotNET_108", previousRelease.TagName);
        Assert.Equal(expected: 2, messageHandler.RequestUrls.Count);
        Assert.Equal(
            expected: "https://github.com/test-owner/test-repository/releases/download/TrayAppDotNET_108/versions.xml",
            messageHandler.RequestUrls[1]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SetCurrentVersionSkippedAsync_ControlsWhetherTheCurrentReleaseIsOfferedAfterBackdating(
        bool skipCurrentVersion,
        bool expectUpdate)
    {
        int skippedUpdateVersion = 0;
        using (ManifestMessageHandler choiceHandler = new(CurrentBuild))
        using (UpdateCheckService currentService = CreateService(
                   choiceHandler,
                   () => skippedUpdateVersion,
                   version => skippedUpdateVersion = version))
            await currentService.SetCurrentVersionSkippedAsync(skipCurrentVersion);

        using ManifestMessageHandler backdatedHandler = new(CurrentBuild);
        using UpdateCheckService backdatedService = CreateService(
            backdatedHandler,
            () => skippedUpdateVersion,
            version => skippedUpdateVersion = version,
            CurrentBuild - 1);

        UpdateInfo? availableUpdate = await backdatedService.CheckNowAsync();

        Assert.Equal(skipCurrentVersion ? CurrentBuild : 0, skippedUpdateVersion);
        Assert.Equal(expectUpdate, availableUpdate != null);
    }

    [Fact]
    public async Task DownloadAndStageAsync_RetriesAndRemovesPartialDownloads()
    {
        byte[] partialAsset = "partial zip response"u8.ToArray();
        using FixedAssetMessageHandler messageHandler = new(partialAsset);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });
        UpdateInfo update = new(
            CurrentBuild + 1,
            LatestReleaseTag,
            ReleaseName: "Partial asset test",
            string.Empty,
            AssetUrl: "https://downloads.test/TestTrayApp.zip",
            AssetName: "TestTrayApp.zip",
            partialAsset.Length + 1);

        bool staged = await service.DownloadAndStageAsync(update);

        Assert.False(staged);
        Assert.Equal(expected: 3, messageHandler.RequestCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_testDirectory, searchPattern: "*_update_*"));
    }

    [Fact]
    public async Task DownloadAndStageAsync_RetriesAndRemovesCorruptArchives()
    {
        byte[] corruptArchive = "not a zip archive"u8.ToArray();
        using FixedAssetMessageHandler messageHandler = new(corruptArchive);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });
        UpdateInfo update = new(
            CurrentBuild + 1,
            LatestReleaseTag,
            ReleaseName: "Corrupt asset test",
            string.Empty,
            AssetUrl: "https://downloads.test/TestTrayApp.zip",
            AssetName: "TestTrayApp.zip",
            corruptArchive.Length);

        bool staged = await service.DownloadAndStageAsync(update);

        Assert.False(staged);
        Assert.Equal(expected: 3, messageHandler.RequestCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_testDirectory, searchPattern: "*_update_*"));
    }

    private UpdateCheckService CreateService(
        HttpMessageHandler messageHandler,
        Func<int> getSkippedUpdateVersion,
        Action<int> persistSkippedUpdateVersion,
        int currentBuild = CurrentBuild,
        Func<TimeSpan>? pollInterval = null,
        Func<DateTime>? getCurrentUTCTime = null) =>
        new(
            new UpdateCheckOptions
            {
                VersionsManifestUrl = new Uri("https://updates.test/versions.xml"),
                VersionsManifestCachePath = CachePath,
                RepositoryOwner = "test-owner",
                RepositoryName = "test-repository",
                ApplicationName = ApplicationName,
                CurrentBuild = currentBuild,
                UserAgent = ApplicationName + "-Updater",
                StagingDirectory = () => _testDirectory,
                IsEnabled = static () => true,
                PollInterval = pollInterval ?? (static () => TimeSpan.FromHours(1)),
                GetSkippedUpdateVersion = getSkippedUpdateVersion,
                PersistSkippedUpdateVersion = persistSkippedUpdateVersion,
                InvokeOnUIThread = static action =>
                {
                    action();
                    return Task.CompletedTask;
                },
                AssetDownloadMaxAttempts = 3,
                AssetDownloadInitialBackoff = TimeSpan.FromMilliseconds(1),
                GetCurrentUTCTime = getCurrentUTCTime ?? (static () => DateTime.UtcNow)
            },
            messageHandler);

    private void WriteCachedManifest(int version, DateTime writeTimeUTC)
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(CachePath, CreateManifest(version), Encoding.UTF8);
        File.SetLastWriteTimeUtc(CachePath, writeTimeUTC);
    }

    private static string CreateManifest(int version) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <versions>
           <release tag="v{version}" name="Release {version}" />
           <artifacts>
             <artifact profile="release" kind="app" appId="{ApplicationName}" version="{version}"
                       fileName="{ApplicationName}_{version}.zip" size="0" />
           </artifacts>
         </versions>
         """;

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class ManifestMessageHandler(params int[] versions) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(RequestCount, versions.Length - 1);
            RequestCount++;
            int version = versions[index];
            string manifest = CreateManifest(version);
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, mediaType: "application/xml"),
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FixedAssetMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content), RequestMessage = request
            });
        }
    }

    private sealed class AggregateManifestMessageHandler(
        int aggregateVersion,
        int appVersion,
        bool includeApp,
        string appReleaseTag = "") : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri requestUri = request.RequestUri
                             ?? throw new InvalidOperationException("The test request did not have a URL.");
            RequestUrls.Add(requestUri.AbsoluteUri);

            HttpResponseMessage response = requestUri.Host switch
            {
                "updates.test" => ManifestResponse(request),
                "api.github.com" => ReleasesResponse(request),
                _ => throw new InvalidOperationException($"Unexpected test request: {requestUri}")
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage ManifestResponse(HttpRequestMessage request)
        {
            string releaseTagAttribute = string.IsNullOrWhiteSpace(appReleaseTag)
                ? string.Empty
                : $" releaseTag=\"{appReleaseTag}\"";
            string appArtifact = includeApp
                ? $"""
                       <artifact profile="release" kind="app" appId="{ApplicationName}" version="{appVersion}"
                                 fileName="{ApplicationName}_{appVersion}.zip"{releaseTagAttribute} size="0" />
                   """
                : string.Empty;
            string manifest = $"""
                               <?xml version="1.0" encoding="utf-8"?>
                               <versions>
                                 <release tag="TrayAppDotNET_{aggregateVersion}" name="TrayAppDotNET {aggregateVersion}" />
                                 <artifacts>
                                   <artifact profile="release" kind="aggregate" appId="TrayAppDotNET" version="{aggregateVersion}"
                                             fileName="TrayAppDotNET_{aggregateVersion}.zip" size="0" />
                               {appArtifact}
                                 </artifacts>
                               </versions>
                               """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, mediaType: "application/xml"),
                RequestMessage = request
            };
        }

        private HttpResponseMessage ReleasesResponse(HttpRequestMessage request)
        {
            string releases = $$"""
                                [
                                  {
                                    "tag_name": "TrayAppDotNET_{{aggregateVersion - 1}}",
                                    "body": "App release notes",
                                    "draft": false,
                                    "prerelease": false,
                                    "assets": [
                                      {
                                        "name": "{{ApplicationName}}_{{appVersion}}.zip",
                                        "size": 123
                                      }
                                    ]
                                  }
                                ]
                                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releases, Encoding.UTF8, mediaType: "application/json"),
                RequestMessage = request
            };
        }
    }

    private sealed class PreviousReleaseMessageHandler(
        bool directManifestAvailable,
        int latestVersion = CurrentBuild,
        string latestTag = LatestReleaseTag,
        string directTag = PreviousReleaseTag) : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];
        public string GitHubApiVersion { get; private set; } = string.Empty;
        public bool RequestedGitHubJSON { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri requestUri = request.RequestUri
                             ?? throw new InvalidOperationException("The test request did not have a URL.");
            RequestUrls.Add(requestUri.AbsoluteUri);

            HttpResponseMessage response;
            switch (requestUri.Host)
            {
                case "updates.test":
                    response = ManifestResponse(request, latestVersion, latestTag);
                    break;
                case "github.com" when directManifestAvailable:
                    response = ManifestResponse(request, CurrentBuild - 1, directTag);
                    break;
                case "github.com":
                    response = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
                    break;
                case "api.github.com":
                    GitHubApiVersion = request.Headers.TryGetValues(
                        name: "X-GitHub-Api-Version",
                        out IEnumerable<string>? apiVersions)
                        ? apiVersions.Single()
                        : string.Empty;
                    RequestedGitHubJSON = request.Headers.Accept.Any(value =>
                        string.Equals(value.MediaType, b: "application/vnd.github+json", StringComparison.Ordinal));
                    response = ReleasesResponse(request, directTag);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected test request: {requestUri}");
            }

            return Task.FromResult(response);
        }

        private static HttpResponseMessage ManifestResponse(
            HttpRequestMessage request,
            int version,
            string tagName)
        {
            string manifest = $"""
                               <?xml version="1.0" encoding="utf-8"?>
                               <versions>
                                 <release tag="{tagName}" name="Release {version}" />
                                 <artifacts>
                                   <artifact profile="release" kind="app" appId="{ApplicationName}" version="{version}"
                                             fileName="{ApplicationName}_{version}.zip" size="0" />
                                 </artifacts>
                               </versions>
                               """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, mediaType: "application/xml"),
                RequestMessage = request
            };
        }

        private static HttpResponseMessage ReleasesResponse(HttpRequestMessage request, string tagName)
        {
            string releases = $$"""
                                [
                                  {
                                    "tag_name": "{{tagName}}",
                                    "body": "Previous release notes",
                                    "draft": false,
                                    "prerelease": false,
                                    "assets": [
                                      {
                                        "name": "{{ApplicationName}}_{{CurrentBuild - 1}}.zip",
                                        "size": 123
                                      }
                                    ]
                                  }
                                ]
                                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releases, Encoding.UTF8, mediaType: "application/json"),
                RequestMessage = request
            };
        }
    }
}
