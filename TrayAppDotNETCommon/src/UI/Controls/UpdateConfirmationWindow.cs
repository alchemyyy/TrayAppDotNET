using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class UpdateConfirmationLayout
{
    private static UpdateConfirmationWindowResources AXAMLResources => UpdateConfirmationWindowResources.Current;

    public static double WindowWidth => AXAMLResources.AxamlUpdateConfirmation.WindowWidth;
    public static double WindowMinWidth => AXAMLResources.AxamlUpdateConfirmation.WindowMinWidth;
    public static Thickness RootBorderThickness => AXAMLResources.AxamlUpdateConfirmation.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlUpdateConfirmation.RootCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlUpdateConfirmation.ZeroCornerRadius;
    public static double TitleBarHeight => AXAMLResources.AxamlUpdateConfirmation.TitleBarHeight;
    public static Thickness TitleMargin => AXAMLResources.AxamlUpdateConfirmation.TitleMargin;
    public static Thickness BodyMargin => AXAMLResources.AxamlUpdateConfirmation.BodyMargin;
    public static Thickness ActionButtonPadding => AXAMLResources.AxamlUpdateConfirmation.ActionButtonPadding;
    public static Thickness SecondaryButtonMargin => AXAMLResources.AxamlUpdateConfirmation.SecondaryButtonMargin;
    public static Thickness ActionButtonsMargin => AXAMLResources.AxamlUpdateConfirmation.ActionButtonsMargin;
}

public enum TrayAppDotNETUpdatePromptResult
{
    Cancelled,
    Confirmed,
    Alternate
}

public sealed class TrayAppDotNETUpdateConfirmationWindow : Window, IDisposable
{
    private readonly string _dialogTitle;
    private readonly string _description;
    private readonly string _confirmText;
    private readonly string? _alternateText;
    private readonly string? _cancelText;
    private readonly SettingsPalette _palette;
    private readonly bool _rounded;
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private int _disposeState;
    private bool _closed;

    public TrayAppDotNETUpdateConfirmationWindow(UpdateInfo info, SettingsPalette palette, bool rounded)
        : this(
            string.Format(CultureInfo.CurrentCulture, L(nameof(CommonStrings.UpdateDialog_TitleFormat)),
                info.ReleaseName),
            L(nameof(CommonStrings.UpdateDialog_DefaultDescription)),
            L(nameof(CommonStrings.UpdateDialog_Install)),
            palette,
            rounded,
            L(nameof(CommonStrings.UpdateDialog_SkipRelease)))
    {
    }

    public TrayAppDotNETUpdateConfirmationWindow(
        string title,
        string description,
        string confirmText,
        SettingsPalette palette,
        bool rounded,
        string? alternateText = null,
        string? cancelText = null)
    {
        _dialogTitle = title;
        _description = description;
        _confirmText = confirmText;
        _alternateText = alternateText;
        _cancelText = cancelText;
        _palette = palette;
        _rounded = rounded;
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETUpdateConfirmationWindow));
        Title = title;
        ApplyWindowLayout();
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        ShowInTaskbar = false;
        CanResize = false;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        KeyDown += OnWindowKeyDown;
        _windowResources.Add(() => KeyDown -= OnWindowKeyDown);
        Closed += OnWindowClosed;
        _windowResources.Add(() => Closed -= OnWindowClosed);
        UpdateConfirmationWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        _windowResources.Add(() =>
            UpdateConfirmationWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded);

        try
        {
            RebuildContent();
        }
        catch
        {
            DisposeCore();
            throw;
        }
    }

    private void OnAXAMLResourcesReloaded()
    {
        if (_closed) return;

        ApplyWindowLayout();
        RebuildContent();
    }

    private void ApplyWindowLayout()
    {
        MinWidth = UpdateConfirmationLayout.WindowMinWidth;
        Width = UpdateConfirmationLayout.WindowWidth;
    }

    private void RebuildContent()
    {
        UIResourceScope contentResources = new(nameof(TrayAppDotNETUpdateConfirmationWindow) + ".Content");
        UIContentGeneration replacement;
        Border root;
        try
        {
            root = new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
                BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
                BorderThickness = UpdateConfirmationLayout.RootBorderThickness,
                CornerRadius = _rounded
                    ? UpdateConfirmationLayout.RootCornerRadius
                    : UpdateConfirmationLayout.ZeroCornerRadius,
                Child = BuildContent(
                    _dialogTitle,
                    _description,
                    _confirmText,
                    _alternateText,
                    _cancelText,
                    _palette,
                    contentResources)
            };
            replacement = new UIContentGeneration(
                nameof(TrayAppDotNETUpdateConfirmationWindow),
                root,
                contentResources);
            ControlNameScope.For(this).AssignLogicalSubtree(root, this);
        }
        catch
        {
            contentResources.Dispose();
            throw;
        }

        UIContentGeneration? previous = _contentGeneration;
        _contentGeneration = replacement;
        try
        {
            Content = root;
        }
        catch
        {
            _contentGeneration = previous;
            replacement.Dispose();
            throw;
        }

        previous?.Dispose();
    }

    private Grid BuildContent(
        string title,
        string description,
        string confirmText,
        string? alternateText,
        string? cancelText,
        SettingsPalette palette,
        UIResourceScope resources)
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(UpdateConfirmationLayout.TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildTitleBar(title, palette, resources));

        Grid body = new() { Margin = UpdateConfirmationLayout.BodyMargin };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(body, 1);

        TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
        Grid.SetRow(descriptionText, 0);
        body.Children.Add(descriptionText);

        SettingsButton install = TrayAppDotNETSettingsUI.Button(confirmText, palette);
        install.Padding = UpdateConfirmationLayout.ActionButtonPadding;
        install.Click += OnConfirmClick;
        resources.Add(() => install.Click -= OnConfirmClick);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (!string.IsNullOrWhiteSpace(alternateText))
        {
            SettingsButton alternate = TrayAppDotNETSettingsUI.Button(alternateText, palette);
            alternate.Padding = UpdateConfirmationLayout.ActionButtonPadding;
            alternate.Margin = UpdateConfirmationLayout.SecondaryButtonMargin;
            alternate.Click += OnAlternateClick;
            resources.Add(() => alternate.Click -= OnAlternateClick);
            buttons.Children.Add(alternate);
        }

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            SettingsButton cancel = TrayAppDotNETSettingsUI.Button(cancelText, palette);
            cancel.Padding = UpdateConfirmationLayout.ActionButtonPadding;
            cancel.Margin = UpdateConfirmationLayout.SecondaryButtonMargin;
            cancel.Click += OnCancelClick;
            resources.Add(() => cancel.Click -= OnCancelClick);
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(install);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = UpdateConfirmationLayout.ActionButtonsMargin;
        Grid.SetRow(buttons, 1);
        body.Children.Add(buttons);

        root.Children.Add(body);
        return root;
    }

    private Grid BuildTitleBar(string title, SettingsPalette palette, UIResourceScope resources)
    {
        Grid bar = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(title, palette, 13);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = UpdateConfirmationLayout.TitleMargin;
        bar.Children.Add(titleText);

        TrayAppDotNETCaptionCloseButton close = new(palette);
        TrayAppDotNETToolTip.SetTip(close, L(nameof(CommonStrings.UpdateDialog_CaptionClose_Tooltip)));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += OnCancelClick;
        resources.Add(() => close.Click -= OnCancelClick);
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);

        bar.PointerPressed += OnTitleBarPointerPressed;
        resources.Add(() => bar.PointerPressed -= OnTitleBarPointerPressed);

        return bar;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_closed || e.Key != Key.Escape) return;

        Complete(TrayAppDotNETUpdatePromptResult.Cancelled);
        e.Handled = true;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_closed || sender is not Control titleBar) return;
        if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnConfirmClick(object? sender, EventArgs e) => Complete(TrayAppDotNETUpdatePromptResult.Confirmed);

    private void OnCancelClick(object? sender, EventArgs e) => Complete(TrayAppDotNETUpdatePromptResult.Cancelled);

    private void OnAlternateClick(object? sender, EventArgs e) => Complete(TrayAppDotNETUpdatePromptResult.Alternate);

    private void Complete(TrayAppDotNETUpdatePromptResult result)
    {
        if (_closed) return;
        Close(result);
    }

    private void OnWindowClosed(object? sender, EventArgs e) => DisposeCore();

    /// <summary>Closes the prompt when necessary and releases all owned UI resources.</summary>
    public void Dispose()
    {
        if (!_closed && IsVisible)
        {
            try
            {
                Close(TrayAppDotNETUpdatePromptResult.Cancelled);
            }
            finally
            {
                DisposeCore();
            }

            return;
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        _closed = true;
        _windowResources.Dispose();
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, null);
        if (contentGeneration == null) return;

        try
        {
            if (!contentGeneration.IsDisposed && ReferenceEquals(Content, contentGeneration.Root))
                Content = null;
        }
        finally
        {
            contentGeneration.Dispose();
        }
    }

    private static string L(string key) => LocalizationManager.Instance[key];
}
