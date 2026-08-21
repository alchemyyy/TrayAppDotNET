namespace TrayAppDotNETCommon.Services;

public static class GitHubReleaseUrls
{
    public const string VersionsManifestFileName = "versions.xml";
    public const string VersionsManifestEndpoint = "https://version.trayapp.net/versions.xml";
    public const string ReleaseProfile = "release";

    public static Uri LatestVersionsManifestUrl() => new(VersionsManifestEndpoint);

    public static string ReleaseAssetName(string applicationName, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return version <= 0 ? throw new ArgumentOutOfRangeException(nameof(version)) : $"{applicationName}_{version}.zip";
    }

    public static Uri LatestAppReleaseAssetUrl(string owner, string repositoryName, string applicationName, int version) =>
        LatestReleaseAssetUrl(owner, repositoryName, ReleaseAssetName(applicationName, version));

    /// <summary>Builds a paged GitHub releases API URL.</summary>
    public static Uri ReleasesApiUrl(string owner, string repositoryName, int page, int releasesPerPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (releasesPerPage is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(releasesPerPage));

        return new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repositoryName)}"
            + $"/releases?per_page={releasesPerPage}&page={page}");
    }

    /// <summary>Builds a download URL for an asset pinned to one release tag.</summary>
    public static Uri ReleaseAssetUrl(string owner, string repositoryName, string tagName, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        return new Uri(
            $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repositoryName)}"
            + $"/releases/download/{Uri.EscapeDataString(tagName)}/{Uri.EscapeDataString(assetName)}");
    }

    public static Uri LatestReleaseAssetUrl(string owner, string repositoryName, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        return new Uri(
            $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repositoryName)}"
            + $"/releases/latest/download/{Uri.EscapeDataString(assetName)}");
    }
}
