namespace TrayAppDotNETInstaller;

/// <summary>Per-process scratch locations under %TEMP%\TrayAppDotNETInstaller.</summary>
internal static class InstallerTempPaths
{
    private const string RootDirectoryName = "TrayAppDotNETInstaller";
    private const string PayloadDirectoryPrefix = "payload-";
    private const string StagingDirectoryPrefix = "staging-";

    public static string Root => Path.Combine(Path.GetTempPath(), RootDirectoryName);

    /// <summary>Returns a fresh payload-&lt;pid&gt;-&lt;guid&gt; path under the root. The caller creates it.</summary>
    public static string NewPayloadDirectory() => NewScratchDirectory(PayloadDirectoryPrefix);

    /// <summary>Returns a fresh staging-&lt;pid&gt;-&lt;guid&gt; path for the factory to pack payloads into.</summary>
    public static string NewStagingDirectory() => NewScratchDirectory(StagingDirectoryPrefix);

    private static string NewScratchDirectory(string prefix) =>
        Path.Combine(Root, $"{prefix}{FrameworkCompatibility.ProcessID}-{Guid.NewGuid():N}");
}
