using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Releases Avalonia text layouts when rendered text blocks leave the visual tree.
/// </summary>
public sealed class TextBlockLayoutLifetime : AvaloniaObject
{
    private static readonly Size EmptyMeasureConstraint = new(width: 0, height: 0);

    public static readonly AttachedProperty<bool> ReleaseOnDetachProperty =
        AvaloniaProperty.RegisterAttached<TextBlockLayoutLifetime, TextBlock, bool>(
            "ReleaseOnDetach");

    private static readonly AttachedProperty<bool> IsReleasedProperty =
        AvaloniaProperty.RegisterAttached<TextBlockLayoutLifetime, TextBlock, bool>(
            "IsReleased");

    private static readonly IDisposable ReleaseOnDetachSubscription =
        ReleaseOnDetachProperty.Changed.AddClassHandler<TextBlock>(static (textBlock, _) =>
            UpdateEventHandlers(textBlock));

    private static readonly Style ReleaseOnDetachStyle =
        new(static selector => selector.OfType<TextBlock>())
        {
            Setters = { new Setter(ReleaseOnDetachProperty, value: true) }
        };

    private TextBlockLayoutLifetime()
    {
    }

    /// <summary>Installs process-wide TextBlock retirement handling for an application.</summary>
    public static void Install(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!application.Styles.Contains(ReleaseOnDetachStyle))
            application.Styles.Add(ReleaseOnDetachStyle);
    }

    /// <summary>Releases every realized TextBlock layout in a retiring visual subtree.</summary>
    public static void ReleaseForRetirement(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<Visual> pending = [root];
        while (pending.Count > 0)
        {
            int lastIndex = pending.Count - 1;
            Visual visual = pending[lastIndex];
            pending.RemoveAt(lastIndex);

            foreach (Visual child in visual.GetVisualChildren())
                pending.Add(child);

            if (visual is TextBlock textBlock)
                ReleaseTextLayout(textBlock);
        }
    }

    private static void UpdateEventHandlers(TextBlock textBlock)
    {
        textBlock.AttachedToVisualTree -= OnAttachedToVisualTree;
        textBlock.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        if (!textBlock.GetValue(ReleaseOnDetachProperty)) return;

        textBlock.AttachedToVisualTree += OnAttachedToVisualTree;
        textBlock.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is TextBlock textBlock)
            textBlock.SetValue(IsReleasedProperty, value: false);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is TextBlock textBlock)
            ReleaseTextLayout(textBlock);
    }

    private static void ReleaseTextLayout(TextBlock textBlock)
    {
        if (textBlock.GetValue(IsReleasedProperty)) return;

        try
        {
            // InvalidateMeasure is a no-op while measure is already invalid. Force one valid
            // measure so TextBlock.OnMeasureInvalidated deterministically disposes its layout
            if (!textBlock.IsMeasureValid)
                textBlock.Measure(EmptyMeasureConstraint);

            textBlock.InvalidateMeasure();
            textBlock.SetValue(IsReleasedProperty, value: true);
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"TextBlock layout release failed: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
