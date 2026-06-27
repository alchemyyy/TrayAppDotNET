namespace TrayAppDotNETCommon.Services;

public static class GitHubReleaseUrls
{
    public const string VersionsManifestFileName = "versions.xml";
    public const string ReleaseProfile = "release";

    public static Uri LatestVersionsManifestUrl(string owner, string repositoryName) =>
        LatestReleaseAssetUrl(owner, repositoryName, VersionsManifestFileName);

    public static string ReleaseAssetName(string applicationName, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));

        return $"{applicationName}_{version}.zip";
    }

    public static Uri LatestAppReleaseAssetUrl(string owner, string repositoryName, string applicationName, int version) =>
        LatestReleaseAssetUrl(owner, repositoryName, ReleaseAssetName(applicationName, version));

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
