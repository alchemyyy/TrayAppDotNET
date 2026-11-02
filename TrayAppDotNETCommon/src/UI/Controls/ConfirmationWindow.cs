using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class ConfirmationWindowLayout
{
    private static ConfirmationWindowResources AXAMLResources => ConfirmationWindowResources.Current;

    public static double WindowWidth => AXAMLResources.AxamlConfirmationWindow.WindowWidth;
    public static double WindowMinWidth => AXAMLResources.AxamlConfirmationWindow.WindowMinWidth;

    public static double FlyoutVerticalAnchorRatio =>
        AXAMLResources.AxamlConfirmationWindow.FlyoutVerticalAnchorRatio;

    public static Thickness RootBorderThickness => AXAMLResources.AxamlConfirmationWindow.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlConfirmationWindow.RootCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlConfirmationWindow.ZeroCornerRadius;
    public static double TitleBarHeight => AXAMLResources.AxamlConfirmationWindow.TitleBarHeight;
    public static Thickness TitleMargin => AXAMLResources.AxamlConfirmationWindow.TitleMargin;
    public static Thickness BodyMargin => AXAMLResources.AxamlConfirmationWindow.BodyMargin;
    public static Thickness ModalBodyMargin => AXAMLResources.AxamlConfirmationWindow.ModalBodyMargin;
    public static double ModalTitleFontSize => AXAMLResources.AxamlConfirmationWindow.ModalTitleFontSize;

    public static Thickness ModalDescriptionMargin =>
        AXAMLResources.AxamlConfirmationWindow.ModalDescriptionMargin;

    public static double VersionLineHeightPadding =>
        AXAMLResources.AxamlConfirmationWindow.VersionLineHeightPadding;

    public static double ModalLinkSpacing =>
        AXAMLResources.AxamlConfirmationWindow.ModalLinkSpacing;

    public static Thickness ModalLinksMargin =>
        AXAMLResources.AxamlConfirmationWindow.ModalLinksMargin;

    public static Thickness ModalRestartNoticeMargin =>
        AXAMLResources.AxamlConfirmationWindow.ModalRestartNoticeMargin;

    public static Thickness ModalActionButtonsMargin =>
        AXAMLResources.AxamlConfirmationWindow.ModalActionButtonsMargin;

    public static double ModalActionButtonSpacing =>
        AXAMLResources.AxamlConfirmationWindow.ModalActionButtonSpacing;

    public static Thickness ActionButtonPadding => AXAMLResources.AxamlConfirmationWindow.ActionButtonPadding;
    public static Thickness SecondaryButtonMargin => AXAMLResources.AxamlConfirmationWindow.SecondaryButtonMargin;
    public static Thickness ActionButtonsMargin => AXAMLResources.AxamlConfirmationWindow.ActionButtonsMargin;
}

internal static class ConfirmationWindowPositioning
{
    private const double PromptCenterRatio = 0.5;

    /// <summary>Centers a prompt horizontally and places its center at the requested owner-height ratio.</summary>
    public static PixelPoint ResolveOwnerPosition(
        PixelRect ownerBounds,
        PixelSize promptSize,
        PixelRect workArea,
        double verticalAnchorRatio)
    {
        if (!double.IsFinite(verticalAnchorRatio) || verticalAnchorRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(verticalAnchorRatio));

        int promptWidth = Math.Max(val1: 1, promptSize.Width);
        int promptHeight = Math.Max(val1: 1, promptSize.Height);
        int targetLeft = ownerBounds.X + (ownerBounds.Width - promptWidth) / 2;
        int targetTop = (int)Math.Round(
            ownerBounds.Y
            + ownerBounds.Height * verticalAnchorRatio
            - promptHeight * PromptCenterRatio,
            MidpointRounding.AwayFromZero);
        int maximumLeft = Math.Max(workArea.X, workArea.Right - promptWidth);
        int maximumTop = Math.Max(workArea.Y, workArea.Bottom - promptHeight);
        return new PixelPoint(
            Math.Clamp(targetLeft, workArea.X, maximumLeft),
            Math.Clamp(targetTop, workArea.Y, maximumTop));
    }
}

public enum TrayAppDotNETConfirmationResult
{
    Cancelled,
    Confirmed,
    Alternate
}

/// <summary>Structured version rows and links for the update modal body.</summary>
public sealed record TrayAppDotNETUpdateModalDetails(
    string NewVersionText,
    string CurrentVersionText,
    string ReleasesLinkText,
    Uri ReleasesPageURI,
    string WebsiteLinkText);

public sealed class TrayAppDotNETConfirmationWindow : Window, IDisposable
{
    private static readonly Uri TrayAppDotNETWebsitePageURI = new("https://trayapp.net/");

    private readonly string _dialogTitle;
    private readonly string _description;
    private readonly string _confirmText;
    private readonly string? _alternateText;
    private readonly string? _cancelText;
    private readonly TrayAppDotNETUpdateModalDetails? _modalDetails;
    private readonly string? _modalFooterText;
    private readonly bool _useModalContentLayout;
    private readonly SettingsPalette _palette;
    private readonly bool _rounded;
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private Window? _upperThirdPlacementOwner;
    private int _disposeState;
    private bool _closed;

    public TrayAppDotNETConfirmationWindow(
        string title,
        string description,
        string confirmText,
        SettingsPalette palette,
        bool rounded,
        string? alternateText = null,
        string? cancelText = null,
        TrayAppDotNETUpdateModalDetails? modalDetails = null,
        string? modalFooterText = null,
        bool useModalContentLayout = false)
    {
        _dialogTitle = title;
        _description = description;
        _confirmText = confirmText;
        _alternateText = alternateText;
        _cancelText = cancelText;
        _modalDetails = modalDetails;
        _modalFooterText = modalFooterText;
        _useModalContentLayout = useModalContentLayout;
        _palette = palette;
        _rounded = rounded;
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETConfirmationWindow));
        Title = title;
        ApplyWindowLayout();
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        ShowInTaskbar = false;
        CanResize = false;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        // An app whose map instantiates the template more than once tags each prompt with its surface id
        this.MapTo(ControlMap.Confirmation.ID);
        this.MapCommand(
            ControlMap.Confirmation.Dismiss,
            () => Complete(TrayAppDotNETConfirmationResult.Cancelled));
        Closed += OnWindowClosed;
        _windowResources.Add(() => Closed -= OnWindowClosed);
#if DEBUG
        ConfirmationWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        _windowResources.Add(() =>
            ConfirmationWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded);
#endif

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

#if DEBUG
    private void OnAXAMLResourcesReloaded()
    {
        if (_closed) return;

        ApplyWindowLayout();
        RebuildContent();
        PositionOverOwnerUpperThird();
    }
#endif

    /// <summary>Places this prompt over the upper third of a flyout owner after final layout.</summary>
    internal void PlaceOverOwnerUpperThird(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (ReferenceEquals(_upperThirdPlacementOwner, owner)) return;
        if (_upperThirdPlacementOwner != null)
            throw new InvalidOperationException("The update prompt already has a placement owner.");

        _upperThirdPlacementOwner = owner;
        Opened += OnUpperThirdPlacementOpened;
        _windowResources.Add(() => Opened -= OnUpperThirdPlacementOpened);
    }

    private void OnUpperThirdPlacementOpened(object? sender, EventArgs eventArgs) =>
        PositionOverOwnerUpperThird();

    private void PositionOverOwnerUpperThird()
    {
        Window? owner = _upperThirdPlacementOwner;
        if (owner == null || !IsVisible) return;

        UpdateLayout();
        PixelRect ownerBounds = ResolveWindowPixelBounds(owner);
        PixelRect promptBounds = ResolveWindowPixelBounds(this);
        PixelRect workArea = Screens.ScreenFromWindow(owner)?.WorkingArea
                             ?? Screens.ScreenFromBounds(ownerBounds)?.WorkingArea
                             ?? ownerBounds;
        Position = ConfirmationWindowPositioning.ResolveOwnerPosition(
            ownerBounds,
            new PixelSize(promptBounds.Width, promptBounds.Height),
            workArea,
            ConfirmationWindowLayout.FlyoutVerticalAnchorRatio);
    }

    private static PixelRect ResolveWindowPixelBounds(Window window)
    {
        double renderScaling = double.IsFinite(window.RenderScaling) && window.RenderScaling > 0
            ? window.RenderScaling
            : 1;
        double logicalWidth = ResolveLogicalLength(
            window.ClientSize.Width,
            window.Bounds.Width,
            window.Width,
            window.MinWidth);
        double logicalHeight = ResolveLogicalLength(
            window.ClientSize.Height,
            window.Bounds.Height,
            window.Height,
            window.MinHeight);
        int pixelWidth = Math.Max(val1: 1, (int)Math.Ceiling(logicalWidth * renderScaling));
        int pixelHeight = Math.Max(val1: 1, (int)Math.Ceiling(logicalHeight * renderScaling));
        return new PixelRect(window.Position.X, window.Position.Y, pixelWidth, pixelHeight);
    }

    private static double ResolveLogicalLength(
        double clientLength,
        double boundsLength,
        double requestedLength,
        double minimumLength)
    {
        if (double.IsFinite(clientLength) && clientLength > 0) return clientLength;
        if (double.IsFinite(boundsLength) && boundsLength > 0) return boundsLength;
        if (double.IsFinite(requestedLength) && requestedLength > 0) return requestedLength;
        if (double.IsFinite(minimumLength) && minimumLength > 0) return minimumLength;
        return 1;
    }

    private void ApplyWindowLayout()
    {
        MinWidth = ConfirmationWindowLayout.WindowMinWidth;
        Width = ConfirmationWindowLayout.WindowWidth;
    }

    private void RebuildContent()
    {
        UIResourceScope contentResources = new(nameof(TrayAppDotNETConfirmationWindow) + ".Content");
        UIContentGeneration replacement;
        Border root;
        try
        {
            root = new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
                BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
                BorderThickness = ConfirmationWindowLayout.RootBorderThickness,
                CornerRadius = _rounded
                    ? ConfirmationWindowLayout.RootCornerRadius
                    : ConfirmationWindowLayout.ZeroCornerRadius,
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
                nameof(TrayAppDotNETConfirmationWindow),
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
        Grid body = new()
        {
            Margin = _useModalContentLayout
                ? ConfirmationWindowLayout.ModalBodyMargin
                : ConfirmationWindowLayout.BodyMargin
        };
        int descriptionRow;
        int actionButtonsRow;
        if (_useModalContentLayout)
        {
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            body.Children.Add(BuildModalTitle(title, palette));
            descriptionRow = 1;
            actionButtonsRow = 3;
        }
        else
        {
            root.RowDefinitions.Add(new RowDefinition(new GridLength(ConfirmationWindowLayout.TitleBarHeight)));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.Children.Add(BuildTitleBar(title, palette, resources));

            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(body, value: 1);
            descriptionRow = 0;
            actionButtonsRow = 1;
        }

        if (_useModalContentLayout && _modalDetails != null)
        {
            Grid modalDescription = BuildModalDescription(
                description,
                _modalDetails,
                palette);
            Grid.SetRow(modalDescription, descriptionRow);
            body.Children.Add(modalDescription);
        }
        else
        {
            TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
            if (_useModalContentLayout)
            {
                descriptionText.Margin = ConfirmationWindowLayout.ModalDescriptionMargin;
                descriptionText.LineHeight =
                    descriptionText.FontSize + ConfirmationWindowLayout.VersionLineHeightPadding;
                descriptionText.TextWrapping = TextWrapping.Wrap;
            }

            Grid.SetRow(descriptionText, descriptionRow);
            body.Children.Add(descriptionText);
        }

        if (_useModalContentLayout && !string.IsNullOrWhiteSpace(_modalFooterText))
        {
            TextBlock modalFooterText = TrayAppDotNETSettingsUI.DescriptionText(_modalFooterText, palette);
            modalFooterText.Margin = ConfirmationWindowLayout.ModalRestartNoticeMargin;
            modalFooterText.LineHeight =
                modalFooterText.FontSize + ConfirmationWindowLayout.VersionLineHeightPadding;
            modalFooterText.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(modalFooterText, value: 2);
            body.Children.Add(modalFooterText);
        }

        List<SettingsButton> actionButtons = [];

        SettingsButton install = TrayAppDotNETSettingsUI.Button(confirmText, palette)
            .MapTo(ControlMap.Confirmation.Actions.Confirm);
        install.Padding = ConfirmationWindowLayout.ActionButtonPadding;
        install.Click += OnConfirmClick;
        resources.Add(() => install.Click -= OnConfirmClick);

        if (!string.IsNullOrWhiteSpace(alternateText))
        {
            SettingsButton alternate = TrayAppDotNETSettingsUI.Button(alternateText, palette)
                .MapTo(ControlMap.Confirmation.Actions.Alternate);
            alternate.Padding = ConfirmationWindowLayout.ActionButtonPadding;
            if (!_useModalContentLayout)
                alternate.Margin = ConfirmationWindowLayout.SecondaryButtonMargin;
            alternate.Click += OnAlternateClick;
            resources.Add(() => alternate.Click -= OnAlternateClick);
            actionButtons.Add(alternate);
        }

        SettingsButton? cancel = null;
        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            cancel = TrayAppDotNETSettingsUI.Button(cancelText, palette)
                .MapTo(ControlMap.Confirmation.Actions.Cancel);
            cancel.Padding = ConfirmationWindowLayout.ActionButtonPadding;
            if (!_useModalContentLayout)
                cancel.Margin = ConfirmationWindowLayout.SecondaryButtonMargin;
            cancel.Click += OnCancelClick;
            resources.Add(() => cancel.Click -= OnCancelClick);
        }

        if (!_useModalContentLayout && cancel != null)
            actionButtons.Add(cancel);

        actionButtons.Add(install);
        if (_useModalContentLayout && cancel != null)
            actionButtons.Add(cancel);

        Panel buttons;
        if (_useModalContentLayout)
        {
            Grid modalButtons = new()
            {
                ColumnSpacing = ConfirmationWindowLayout.ModalActionButtonSpacing,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = ConfirmationWindowLayout.ModalActionButtonsMargin
            };
            int lastButtonIndex = actionButtons.Count - 1;
            for (int buttonIndex = 0; buttonIndex < actionButtons.Count; buttonIndex++)
            {
                bool fillsRemainingWidth = buttonIndex == lastButtonIndex;
                modalButtons.ColumnDefinitions.Add(new ColumnDefinition(
                    fillsRemainingWidth ? GridLength.Star : GridLength.Auto));
                SettingsButton actionButton = actionButtons[buttonIndex];
                actionButton.HorizontalAlignment = fillsRemainingWidth
                    ? HorizontalAlignment.Stretch
                    : HorizontalAlignment.Left;
                Grid.SetColumn(actionButton, buttonIndex);
                modalButtons.Children.Add(actionButton);
            }

            buttons = modalButtons;
        }
        else
        {
            StackPanel standardButtons = new()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = ConfirmationWindowLayout.ActionButtonsMargin
            };
            foreach (SettingsButton actionButton in actionButtons)
            {
                actionButton.HorizontalAlignment = HorizontalAlignment.Left;
                standardButtons.Children.Add(actionButton);
            }

            buttons = standardButtons;
        }

        // The standard column puts Cancel above Confirm; the modal row puts it last
        buttons.MapVariant(ControlMap.Variants.StandardConfirmation, !_useModalContentLayout);
        Grid.SetRow(buttons, actionButtonsRow);
        body.Children.Add(buttons);

        root.Children.Add(body);
        if (_useModalContentLayout && _modalDetails != null)
            root.Children.Add(BuildModalLinks(_modalDetails, palette, resources));
        return root;
    }

    private static Grid BuildModalDescription(
        string applicationText,
        TrayAppDotNETUpdateModalDetails details,
        SettingsPalette palette)
    {
        Grid description = new()
        {
            Margin = ConfirmationWindowLayout.ModalDescriptionMargin,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        TextBlock application = BuildModalDescriptionText(applicationText, palette, TextWrapping.Wrap);
        description.Children.Add(application);

        TextBlock newVersion = BuildModalDescriptionText(
            details.NewVersionText,
            palette,
            TextWrapping.NoWrap);
        Grid.SetRow(newVersion, value: 1);
        description.Children.Add(newVersion);

        TextBlock currentVersion = BuildModalDescriptionText(
            details.CurrentVersionText,
            palette,
            TextWrapping.NoWrap);
        Grid.SetRow(currentVersion, value: 2);
        description.Children.Add(currentVersion);

        return description;
    }

    private StackPanel BuildModalLinks(
        TrayAppDotNETUpdateModalDetails details,
        SettingsPalette palette,
        UIResourceScope resources)
    {
        StackPanel links = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = ConfirmationWindowLayout.ModalLinkSpacing,
            Margin = ConfirmationWindowLayout.ModalLinksMargin
        };
        links.Children.Add(BuildModalHyperlink(
            details.WebsiteLinkText,
            TrayAppDotNETWebsitePageURI,
            palette,
            resources,
            ControlMap.Confirmation.Links.OpenWebsite));
        links.Children.Add(BuildModalHyperlink(
            details.ReleasesLinkText,
            details.ReleasesPageURI,
            palette,
            resources,
            ControlMap.Confirmation.Links.OpenReleases));
        return links;
    }

    private static TextBlock BuildModalDescriptionText(
        string text,
        SettingsPalette palette,
        TextWrapping textWrapping)
    {
        TextBlock textBlock = TrayAppDotNETSettingsUI.DescriptionText(text, palette);
        textBlock.LineHeight = textBlock.FontSize + ConfirmationWindowLayout.VersionLineHeightPadding;
        textBlock.Margin = default;
        textBlock.TextWrapping = textWrapping;
        textBlock.VerticalAlignment = VerticalAlignment.Center;
        return textBlock;
    }

    private TextBlock BuildModalHyperlink(
        string text,
        Uri pageURI,
        SettingsPalette palette,
        UIResourceScope resources,
        ControlMapNodeID node)
    {
        TextBlock link = TrayAppDotNETSettingsUI.Text(
            text,
            palette,
            SettingsUILayout.DescriptionFontSize);
        link.Foreground = TrayAppDotNETSettingsUI.Brush(palette.Accent);
        link.TextDecorations = TrayAppDotNETSettingsUI.Underline();
        link.Cursor = TrayAppDotNETCursors.Hand;
        link.Focusable = true;
        link.HorizontalAlignment = HorizontalAlignment.Right;
        link.VerticalAlignment = VerticalAlignment.Center;

        link.PointerPressed += OnPointerPressed;
        resources.Add(() => link.PointerPressed -= OnPointerPressed);
        link.KeyDown += OnKeyDown;
        resources.Add(() => link.KeyDown -= OnKeyDown);
        link.MapTo(node);
        link.MapActivation(_ =>
        {
            if (!_closed) OpenPage(pageURI);
        });
        return link;

        void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            if (_closed || !eventArgs.GetCurrentPoint(link).Properties.IsLeftButtonPressed) return;

            OpenPage(pageURI);
            eventArgs.Handled = true;
        }

        void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (_closed || eventArgs.Key is not (Key.Enter or Key.Space)) return;

            OpenPage(pageURI);
            eventArgs.Handled = true;
        }
    }

    private static TextBlock BuildModalTitle(string title, SettingsPalette palette)
    {
        TextBlock modalTitle = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            ConfirmationWindowLayout.ModalTitleFontSize,
            FontWeight.SemiBold);
        modalTitle.TextWrapping = TextWrapping.Wrap;
        return modalTitle;
    }

    private Grid BuildTitleBar(string title, SettingsPalette palette, UIResourceScope resources) =>
        TrayAppDotNETDialogChrome.TitleBar(
            new TrayAppDotNETDialogTitleBarOptions
            {
                Window = this,
                Title = title,
                Palette = palette,
                CloseTooltip = L(nameof(CommonStrings.UpdateDialog_CaptionClose_Tooltip)),
                Close = () => OnCancelClick(sender: null, EventArgs.Empty),
                BarNode = ControlMap.Confirmation.TitleBar.ID,
                CloseNode = ControlMap.Confirmation.TitleBar.Close,
                FontSize = 13,
                TitleMargin = ConfirmationWindowLayout.TitleMargin
            },
            resources);

    private static void OpenPage(Uri pageURI)
    {
        try
        {
            if (!ExplorerProcessLauncher.TryShellExecute(
                    pageURI.AbsoluteUri,
                    arguments: null,
                    workingDirectory: null,
                    verb: null,
                    out _,
                    out string errorMessage))
                throw new InvalidOperationException(errorMessage);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Confirmation window failed to open {pageURI.Host}: {exception.Message}");
        }
    }

    private void OnConfirmClick(object? sender, EventArgs e) => Complete(TrayAppDotNETConfirmationResult.Confirmed);

    private void OnCancelClick(object? sender, EventArgs e) => Complete(TrayAppDotNETConfirmationResult.Cancelled);

    private void OnAlternateClick(object? sender, EventArgs e) => Complete(TrayAppDotNETConfirmationResult.Alternate);

    /// <summary>Gets the button the user chose; a prompt closed any other way is Cancelled.</summary>
    public TrayAppDotNETConfirmationResult Result { get; private set; } = TrayAppDotNETConfirmationResult.Cancelled;

    private void Complete(TrayAppDotNETConfirmationResult result)
    {
        if (_closed) return;
        Result = result;
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
                Close(TrayAppDotNETConfirmationResult.Cancelled);
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
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

        _closed = true;
        _windowResources.Dispose();
        _upperThirdPlacementOwner = null;
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, value: null);
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
