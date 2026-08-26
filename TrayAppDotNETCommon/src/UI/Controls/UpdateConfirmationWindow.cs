using System.Diagnostics;
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
    public static Thickness ModalBodyMargin => AXAMLResources.AxamlUpdateConfirmation.ModalBodyMargin;
    public static double ModalTitleFontSize => AXAMLResources.AxamlUpdateConfirmation.ModalTitleFontSize;
    public static Thickness ModalDescriptionMargin =>
        AXAMLResources.AxamlUpdateConfirmation.ModalDescriptionMargin;
    public static double VersionLineHeightPadding =>
        AXAMLResources.AxamlUpdateConfirmation.VersionLineHeightPadding;
    public static double ModalLinkColumnSpacing =>
        AXAMLResources.AxamlUpdateConfirmation.ModalLinkColumnSpacing;
    public static Thickness ModalLinkMargin =>
        AXAMLResources.AxamlUpdateConfirmation.ModalLinkMargin;
    public static Thickness ModalRestartNoticeMargin =>
        AXAMLResources.AxamlUpdateConfirmation.ModalRestartNoticeMargin;
    public static Thickness ModalActionButtonsMargin =>
        AXAMLResources.AxamlUpdateConfirmation.ModalActionButtonsMargin;
    public static double ModalActionButtonSpacing =>
        AXAMLResources.AxamlUpdateConfirmation.ModalActionButtonSpacing;
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

/// <summary>Structured version rows and links for the update modal body.</summary>
public sealed record TrayAppDotNETUpdateModalDetails(
    string NewVersionText,
    string CurrentVersionText,
    string ReleasesLinkText,
    Uri ReleasesPageURI,
    string WebsiteLinkText);

public sealed class TrayAppDotNETUpdateConfirmationWindow : Window, IDisposable
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
    private int _disposeState;
    private bool _closed;

    public TrayAppDotNETUpdateConfirmationWindow(
        UpdateInfo info,
        UpdateCheckService service,
        SettingsPalette palette,
        bool rounded)
        : this(
            L(nameof(CommonStrings.UpdateDialog_Title)),
            string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(CommonStrings.UpdateDialog_AppFormat)),
                service.ApplicationName),
            L(nameof(CommonStrings.UpdateDialog_Install)),
            palette,
            rounded,
            alternateText: L(nameof(CommonStrings.UpdateDialog_SkipRelease)),
            cancelText: L(nameof(CommonStrings.UpdateDialog_Close)),
            modalDetails: new TrayAppDotNETUpdateModalDetails(
                string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.UpdateDialog_NewVersionFormat)),
                    info.Version),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.UpdateDialog_CurrentVersionFormat)),
                    service.CurrentBuild),
                L(nameof(CommonStrings.UpdateDialog_ViewReleases)),
                service.ReleasesPageUrl,
                L(nameof(CommonStrings.UpdateDialog_VisitWebsite))),
            modalFooterText: L(nameof(CommonStrings.UpdateDialog_RestartNotice)),
            useModalContentLayout: true)
    {
    }

    public TrayAppDotNETUpdateConfirmationWindow(
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
        Grid body = new()
        {
            Margin = _useModalContentLayout
                ? UpdateConfirmationLayout.ModalBodyMargin
                : UpdateConfirmationLayout.BodyMargin
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
            root.RowDefinitions.Add(new RowDefinition(new GridLength(UpdateConfirmationLayout.TitleBarHeight)));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.Children.Add(BuildTitleBar(title, palette, resources));

            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(body, 1);
            descriptionRow = 0;
            actionButtonsRow = 1;
        }

        if (_useModalContentLayout && _modalDetails != null)
        {
            Grid modalDescription = BuildModalDescription(
                description,
                _modalDetails,
                palette,
                resources);
            Grid.SetRow(modalDescription, descriptionRow);
            body.Children.Add(modalDescription);
        }
        else
        {
            TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
            if (_useModalContentLayout)
            {
                descriptionText.Margin = UpdateConfirmationLayout.ModalDescriptionMargin;
                descriptionText.LineHeight =
                    descriptionText.FontSize + UpdateConfirmationLayout.VersionLineHeightPadding;
                descriptionText.TextWrapping = TextWrapping.Wrap;
            }
            Grid.SetRow(descriptionText, descriptionRow);
            body.Children.Add(descriptionText);
        }

        if (_useModalContentLayout && !string.IsNullOrWhiteSpace(_modalFooterText))
        {
            TextBlock modalFooterText = TrayAppDotNETSettingsUI.DescriptionText(_modalFooterText, palette);
            modalFooterText.Margin = UpdateConfirmationLayout.ModalRestartNoticeMargin;
            modalFooterText.LineHeight =
                modalFooterText.FontSize + UpdateConfirmationLayout.VersionLineHeightPadding;
            modalFooterText.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(modalFooterText, 2);
            body.Children.Add(modalFooterText);
        }

        List<SettingsButton> actionButtons = [];

        SettingsButton install = TrayAppDotNETSettingsUI.Button(confirmText, palette);
        install.Padding = UpdateConfirmationLayout.ActionButtonPadding;
        install.Click += OnConfirmClick;
        resources.Add(() => install.Click -= OnConfirmClick);

        if (!string.IsNullOrWhiteSpace(alternateText))
        {
            SettingsButton alternate = TrayAppDotNETSettingsUI.Button(alternateText, palette);
            alternate.Padding = UpdateConfirmationLayout.ActionButtonPadding;
            if (!_useModalContentLayout)
                alternate.Margin = UpdateConfirmationLayout.SecondaryButtonMargin;
            alternate.Click += OnAlternateClick;
            resources.Add(() => alternate.Click -= OnAlternateClick);
            actionButtons.Add(alternate);
        }

        SettingsButton? cancel = null;
        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            cancel = TrayAppDotNETSettingsUI.Button(cancelText, palette);
            cancel.Padding = UpdateConfirmationLayout.ActionButtonPadding;
            if (!_useModalContentLayout)
                cancel.Margin = UpdateConfirmationLayout.SecondaryButtonMargin;
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
                ColumnSpacing = UpdateConfirmationLayout.ModalActionButtonSpacing,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = UpdateConfirmationLayout.ModalActionButtonsMargin
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
                Margin = UpdateConfirmationLayout.ActionButtonsMargin
            };
            foreach (SettingsButton actionButton in actionButtons)
            {
                actionButton.HorizontalAlignment = HorizontalAlignment.Left;
                standardButtons.Children.Add(actionButton);
            }
            buttons = standardButtons;
        }

        Grid.SetRow(buttons, actionButtonsRow);
        body.Children.Add(buttons);

        root.Children.Add(body);
        return root;
    }

    private Grid BuildModalDescription(
        string applicationText,
        TrayAppDotNETUpdateModalDetails details,
        SettingsPalette palette,
        UIResourceScope resources)
    {
        Grid description = new()
        {
            Margin = UpdateConfirmationLayout.ModalDescriptionMargin,
            ColumnSpacing = UpdateConfirmationLayout.ModalLinkColumnSpacing,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
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
        Grid.SetRow(newVersion, 1);
        description.Children.Add(newVersion);

        TextBlock currentVersion = BuildModalDescriptionText(
            details.CurrentVersionText,
            palette,
            TextWrapping.NoWrap);
        Grid.SetRow(currentVersion, 2);
        description.Children.Add(currentVersion);

        TextBlock releasesLink = BuildModalHyperlink(
            details.ReleasesLinkText,
            details.ReleasesPageURI,
            palette,
            resources);
        Grid.SetRow(releasesLink, 1);
        Grid.SetColumn(releasesLink, 1);
        description.Children.Add(releasesLink);

        TextBlock websiteLink = BuildModalHyperlink(
            details.WebsiteLinkText,
            TrayAppDotNETWebsitePageURI,
            palette,
            resources);
        Grid.SetColumn(websiteLink, 1);
        description.Children.Add(websiteLink);

        return description;
    }

    private static TextBlock BuildModalDescriptionText(
        string text,
        SettingsPalette palette,
        TextWrapping textWrapping)
    {
        TextBlock textBlock = TrayAppDotNETSettingsUI.DescriptionText(text, palette);
        textBlock.LineHeight = textBlock.FontSize + UpdateConfirmationLayout.VersionLineHeightPadding;
        textBlock.Margin = default;
        textBlock.TextWrapping = textWrapping;
        textBlock.VerticalAlignment = VerticalAlignment.Center;
        return textBlock;
    }

    private TextBlock BuildModalHyperlink(
        string text,
        Uri pageURI,
        SettingsPalette palette,
        UIResourceScope resources)
    {
        TextBlock link = TrayAppDotNETSettingsUI.Text(
            text,
            palette,
            SettingsUILayout.DescriptionFontSize);
        link.Foreground = TrayAppDotNETSettingsUI.Brush(palette.Accent);
        link.LineHeight = link.FontSize + UpdateConfirmationLayout.VersionLineHeightPadding;
        link.Margin = UpdateConfirmationLayout.ModalLinkMargin;
        link.TextDecorations = TextDecorations.Underline;
        link.Cursor = TrayAppDotNETCursors.Hand;
        link.Focusable = true;
        link.HorizontalAlignment = HorizontalAlignment.Right;
        link.VerticalAlignment = VerticalAlignment.Center;

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

        link.PointerPressed += OnPointerPressed;
        resources.Add(() => link.PointerPressed -= OnPointerPressed);
        link.KeyDown += OnKeyDown;
        resources.Add(() => link.KeyDown -= OnKeyDown);
        return link;
    }

    private static TextBlock BuildModalTitle(string title, SettingsPalette palette)
    {
        TextBlock modalTitle = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            UpdateConfirmationLayout.ModalTitleFontSize,
            FontWeight.SemiBold);
        modalTitle.TextWrapping = TextWrapping.Wrap;
        return modalTitle;
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

    private static void OpenPage(Uri pageURI)
    {
        try
        {
            using Process? process = Process.Start(
                new ProcessStartInfo(pageURI.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Update confirmation failed to open {pageURI.Host}: {exception.Message}");
        }
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
