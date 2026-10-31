using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// Checks that retiring a TextBlock drops its cached text layout. TextBlock.TextLayout returns the cached layout
/// while one exists and builds a new one only after the cache was released, so a different instance afterwards is
/// the observable proof of release (no reflection on Avalonia internals).
/// </summary>
public sealed class TextBlockLayoutLifetimeTests
{
    [Fact]
    public void RetirementReleasesValidAndInvalidTextLayouts()
    {
        AvaloniaTestHost.Run(() =>
        {
            Application application = Application.Current
                                      ?? throw new InvalidOperationException("Avalonia application is unavailable.");
            TextBlockLayoutLifetime.Install(application);

            VerifyGenerationReleasesAlreadyInvalidLayout();
            VerifyDetachLayoutRelease();
        });
    }

    private static void VerifyGenerationReleasesAlreadyInvalidLayout()
    {
        TextBlock textBlock = new() { Text = "Initial text" };
        textBlock.Measure(new Size(width: 200, height: 40));
        Assert.True(textBlock.IsMeasureValid);

        textBlock.Text = "Replacement text";
        Assert.False(textBlock.IsMeasureValid);

        TextLayout materializedWhileInvalid = textBlock.TextLayout;
        Assert.Same(materializedWhileInvalid, textBlock.TextLayout);

        UIContentGeneration generation = new(nameof(TextBlockLayoutLifetimeTests), textBlock);
        generation.Dispose();

        Assert.False(textBlock.IsMeasureValid);
        Assert.NotSame(materializedWhileInvalid, textBlock.TextLayout);
    }

    private static void VerifyDetachLayoutRelease()
    {
        TextBlock textBlock = new() { Text = "Attached text" };
        Border root = new() { Child = textBlock };
        Window window = new() { Width = 240, Height = 80, Content = root };

        try
        {
            window.Show();
            window.UpdateLayout();
            TextLayout attachedLayout = textBlock.TextLayout;
            Assert.Same(attachedLayout, textBlock.TextLayout);

            window.Content = null;

            Assert.Same(textBlock, root.Child);
            Assert.False(textBlock.IsMeasureValid);
            Assert.NotSame(attachedLayout, textBlock.TextLayout);
        }
        finally
        {
            window.Close();
        }
    }
}
