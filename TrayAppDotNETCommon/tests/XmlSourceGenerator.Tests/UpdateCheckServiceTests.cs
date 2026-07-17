using System.Net;
using System.Text;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UpdateCheckServiceTests
{
    private const string ApplicationName = "TestTrayApp";
    private const string LatestReleaseTag = "TrayAppDotNET_110";
    private const string PreviousReleaseTag = "TrayAppDotNET_109";
    private const int CurrentBuild = 100;

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
        Assert.Equal(201, nextUpdate.Version);
        Assert.Equal(200, service.SkippedUpdateVersion);
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

        Assert.Equal(200, skippedUpdateVersion);
        Assert.Equal(1, persistCount);
        Assert.Equal(1, stateChangeCount);
        Assert.Null(service.AvailableUpdate);
        Assert.Equal(200, service.SkippedUpdateVersion);

        UpdateInfo? repeatedUpdate = await service.CheckNowAsync();
        Assert.Null(repeatedUpdate);
    }

    [Fact]
    public async Task GetPreviousReleaseAsync_UsesThePreviousIndexedTagWithoutListingReleases()
    {
        using PreviousReleaseMessageHandler messageHandler = new(directManifestAvailable: true);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();
        UpdateInfo? cachedPreviousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Same(previousRelease, cachedPreviousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal("TrayAppDotNET_109", previousRelease.TagName);
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
        using PreviousReleaseMessageHandler messageHandler = new(directManifestAvailable: false);
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal("TrayAppDotNET_109", previousRelease.TagName);
        Assert.Equal(new string('a', 64), previousRelease.AssetSha256);
        Assert.Equal(123L, previousRelease.AssetSize);
        Assert.Equal(3, messageHandler.RequestUrls.Count);
        Assert.Equal(
            "https://api.github.com/repos/test-owner/test-repository/releases?per_page=10&page=1",
            messageHandler.RequestUrls[2]);
        Assert.Equal("2026-03-10", messageHandler.GitHubApiVersion);
        Assert.True(messageHandler.RequestedGitHubJSON);
    }

    [Fact]
    public async Task GetPreviousReleaseAsync_DerivesThePinnedTagWhenTheRunningBuildIsBehindLatest()
    {
        using PreviousReleaseMessageHandler messageHandler = new(
            directManifestAvailable: true,
            latestVersion: CurrentBuild + 1,
            latestTag: "TrayAppDotNET_110",
            directTag: "TrayAppDotNET_108");
        using UpdateCheckService service = CreateService(messageHandler, static () => 0, static _ => { });

        UpdateInfo? previousRelease = await service.GetPreviousReleaseAsync();

        Assert.NotNull(previousRelease);
        Assert.Equal(CurrentBuild - 1, previousRelease.Version);
        Assert.Equal("TrayAppDotNET_108", previousRelease.TagName);
        Assert.Equal(2, messageHandler.RequestUrls.Count);
        Assert.Equal(
            "https://github.com/test-owner/test-repository/releases/download/TrayAppDotNET_108/versions.xml",
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
        {
            await currentService.SetCurrentVersionSkippedAsync(skipCurrentVersion);
        }

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

    private static UpdateCheckService CreateService(
        HttpMessageHandler messageHandler,
        Func<int> getSkippedUpdateVersion,
        Action<int> persistSkippedUpdateVersion,
        int currentBuild = CurrentBuild) =>
        new(
            new UpdateCheckOptions
            {
                VersionsManifestUrl = new Uri("https://updates.test/versions.xml"),
                RepositoryOwner = "test-owner",
                RepositoryName = "test-repository",
                ApplicationName = ApplicationName,
                CurrentBuild = currentBuild,
                UserAgent = ApplicationName + "-Updater",
                StagingDirectory = Path.GetTempPath,
                IsEnabled = static () => true,
                PollInterval = static () => TimeSpan.FromHours(1),
                GetSkippedUpdateVersion = getSkippedUpdateVersion,
                PersistSkippedUpdateVersion = persistSkippedUpdateVersion,
                InvokeOnUIThread = static action =>
                {
                    action();
                    return Task.CompletedTask;
                }
            },
            messageHandler);

    private sealed class ManifestMessageHandler(params int[] versions) : HttpMessageHandler
    {
        private int _requestIndex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(_requestIndex, versions.Length - 1);
            _requestIndex++;
            int version = versions[index];
            string manifest = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <versions>
                  <release tag="v{version}" name="Release {version}" />
                  <artifacts>
                    <artifact profile="release" kind="app" appId="{ApplicationName}" version="{version}"
                              fileName="{ApplicationName}_{version}.zip" sha256="" size="0" />
                  </artifacts>
                </versions>
                """;
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, "application/xml"),
                RequestMessage = request
            };
            return Task.FromResult(response);
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
                            "X-GitHub-Api-Version",
                            out IEnumerable<string>? apiVersions)
                        ? apiVersions.Single()
                        : string.Empty;
                    RequestedGitHubJSON = request.Headers.Accept.Any(value =>
                        string.Equals(value.MediaType, "application/vnd.github+json", StringComparison.Ordinal));
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
                              fileName="{ApplicationName}_{version}.zip" sha256="" size="0" />
                  </artifacts>
                </versions>
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, "application/xml"),
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
                        "size": 123,
                        "digest": "sha256:{{new string('a', 64)}}"
                      }
                    ]
                  }
                ]
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releases, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
