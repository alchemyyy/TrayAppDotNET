namespace TrayAppDotNETCommon.TestSupport;

/// <summary>A fresh folder under %TEMP% that is deleted, with everything in it, on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "TrayAppDotNET.Tests")
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>Returns the path of <paramref name="relativePath"/> inside the folder; nothing is created.</summary>
    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;

        // Read-only files (a test may mark one) would stop the recursive delete
        foreach (string file in Directory.EnumerateFiles(Path, searchPattern: "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }
}
