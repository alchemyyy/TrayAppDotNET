using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class AppThemeStoreTestCollection
{
    public const string CollectionName = "App theme store tests";
}

[Collection(AppThemeStoreTestCollection.CollectionName)]
public sealed class AppThemeStoreTests
{
    private static readonly byte[] PNGSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void GeneratedApplicationIconLoadsAsBitmap() => AvaloniaTestHost.Run(static () =>
    {
        byte[] ICOBytes = Assert.IsType<byte[]>(AppServices.AppIcon.LoadIcoBytes());
        byte[] PNGBytes = NativeIcon.ExtractICOImage(ICOBytes, desiredSize: 256);
        Assert.True(PNGBytes.AsSpan(0, PNGSignature.Length).SequenceEqual(PNGSignature));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(PNGBytes.AsSpan(16, 4)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(PNGBytes.AsSpan(20, 4)));

        // Avalonia Headless reports decoded bitmaps as 1x1, so validate the PNG header above
        using Bitmap? bitmap = AppServices.AppIcon.LoadBitmap(Constants.ApplicationBitmapSize);
        Assert.NotNull(bitmap);
    });
}
