using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class TextBlockLayoutLifetimeTests
{
    private static readonly FieldInfo TextLayoutField =
        typeof(TextBlock).GetField("_textLayout", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Avalonia TextBlock._textLayout was not found.");

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
        textBlock.Measure(new Size(200, 40));
        Assert.True(textBlock.IsMeasureValid);

        textBlock.Text = "Replacement text";
        Assert.False(textBlock.IsMeasureValid);

        TextLayout materializedWhileInvalid = textBlock.TextLayout;
        Assert.Same(materializedWhileInvalid, TextLayoutField.GetValue(textBlock));

        UIContentGeneration generation = new(nameof(TextBlockLayoutLifetimeTests), textBlock);
        generation.Dispose();

        Assert.Null(TextLayoutField.GetValue(textBlock));
        Assert.False(textBlock.IsMeasureValid);
    }

    private static void VerifyDetachLayoutRelease()
    {
        TextBlock textBlock = new() { Text = "Attached text" };
        Border root = new() { Child = textBlock };
        Window window = new()
        {
            Width = 240,
            Height = 80,
            Content = root
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            TextLayout attachedLayout = textBlock.TextLayout;
            Assert.Same(attachedLayout, TextLayoutField.GetValue(textBlock));

            window.Content = null;

            Assert.Same(textBlock, root.Child);
            Assert.Null(TextLayoutField.GetValue(textBlock));
            Assert.False(textBlock.IsMeasureValid);
        }
        finally
        {
            window.Close();
        }
    }
}
