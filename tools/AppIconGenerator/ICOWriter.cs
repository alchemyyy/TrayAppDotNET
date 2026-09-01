using System.Text;

namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>One PNG-encoded image stored in a Windows icon container.</summary>
internal readonly record struct IconImage(int Size, byte[] PNGBytes);

/// <summary>Writes PNG-backed, multi-resolution Windows ICO files.</summary>
internal static class ICOWriter
{
    private const int HeaderSize = 6;
    private const int DirectoryEntrySize = 16;
    private const int MaximumIconSize = 256;

    /// <summary>Atomically replaces an ICO file with the supplied image set.</summary>
    public static void WriteFile(string outputPath, IReadOnlyList<IconImage> images)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrEmpty(outputDirectory))
            throw new ArgumentException("The ICO output path has no parent directory.", nameof(outputPath));

        Directory.CreateDirectory(outputDirectory);
        string temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                Write(stream, images);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Writes an ICO container without taking ownership of the destination stream.</summary>
    public static void Write(Stream stream, IReadOnlyList<IconImage> images)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(images);
        if (!stream.CanWrite) throw new ArgumentException("The ICO destination stream is not writable.", nameof(stream));
        if (images.Count is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(images));

        ValidateImages(images);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        long imageOffset = HeaderSize + (long)DirectoryEntrySize * images.Count;
        foreach (IconImage image in images)
        {
            byte encodedDimension = image.Size == MaximumIconSize ? (byte)0 : (byte)image.Size;
            writer.Write(encodedDimension);
            writer.Write(encodedDimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(checked((uint)image.PNGBytes.Length));
            writer.Write(checked((uint)imageOffset));
            imageOffset = checked(imageOffset + image.PNGBytes.Length);
        }

        foreach (IconImage image in images)
            writer.Write(image.PNGBytes);
        writer.Flush();
    }

    private static void ValidateImages(IReadOnlyList<IconImage> images)
    {
        HashSet<int> sizes = new(images.Count);
        foreach (IconImage image in images)
        {
            if (image.Size is < 1 or > MaximumIconSize)
                throw new ArgumentOutOfRangeException(nameof(images), $"Invalid icon size {image.Size}.");
            if (!sizes.Add(image.Size))
                throw new ArgumentException($"Duplicate icon size {image.Size}.", nameof(images));
            if (image.PNGBytes == null || image.PNGBytes.Length == 0)
                throw new ArgumentException($"Icon size {image.Size} has no PNG data.", nameof(images));
        }
    }
}
