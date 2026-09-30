using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrayAppDotNETInstaller.Localization;
using TrayAppDotNETInstaller.Services;
// Aliased because System.Windows.Shapes also declares a Path, which would hide System.IO.Path
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;

namespace TrayAppDotNETInstaller.UI;

/// <summary>
/// Single-page installer window. The layout is built in code, every size, colour and control template comes
/// from Theme.xaml and every user-facing string from the localized resources.
/// </summary>
public sealed class InstallerWindow : Window
{
    private const double ProgressMaximum = InstallProgressLine.CompletePercent;
    private const double ProgressMinimum = 0;

    private const string ModeGroupName = "InstallMode";
    private const string BatteryApplicationName = "BatteryTrayAppDotNET";
    private const string WindhawkURL = "https://windhawk.net/";
    private const string WindhawkModURL = "https://windhawk.net/mods/taskbar-tray-system-icon-tweaks";
    private const string UnsupportedApplicationStateMessage = "Unsupported application state.";
    private const string UnsupportedInstallModeMessage = "Unsupported install mode.";

    // Theme.xaml resource keys
    private const string WindowStyleKey = "InstallerTheme.WindowStyle";
    private const string TitleTextStyleKey = "InstallerTheme.TitleTextStyle";
    private const string SectionHeaderTextStyleKey = "InstallerTheme.SectionHeaderTextStyle";
    private const string SecondaryTextStyleKey = "InstallerTheme.SecondaryTextStyle";
    private const string ApplicationStateTextStyleKey = "InstallerTheme.ApplicationStateTextStyle";
    private const string ApplicationStateInstallingTextStyleKey = "InstallerTheme.ApplicationStateInstallingTextStyle";
    private const string ApplicationStateInstalledTextStyleKey = "InstallerTheme.ApplicationStateInstalledTextStyle";
    private const string ApplicationStateFailedTextStyleKey = "InstallerTheme.ApplicationStateFailedTextStyle";
    private const string AccentButtonStyleKey = "InstallerTheme.AccentButtonStyle";
    private const string HyperlinkButtonStyleKey = "InstallerTheme.HyperlinkButtonStyle";
    private const string OuterMarginKey = "InstallerTheme.OuterMargin";
    private const string SectionSpacingKey = "InstallerTheme.SectionSpacing";
    private const string ItemSpacingKey = "InstallerTheme.ItemSpacing";
    private const string DescriptionSpacingKey = "InstallerTheme.DescriptionSpacing";
    private const string InlineSpacingKey = "InstallerTheme.InlineSpacing";
    private const string ButtonSpacingKey = "InstallerTheme.ButtonSpacing";
    private const string BrowseButtonMarginKey = "InstallerTheme.BrowseButtonMargin";
    private const string NoticePaddingKey = "InstallerTheme.NoticePadding";
    private const string NoticeCornerRadiusKey = "InstallerTheme.NoticeCornerRadius";
    private const string NoticeBorderThicknessKey = "InstallerTheme.NoticeBorderThickness";
    private const string CautionBackgroundBrushKey = "InstallerTheme.CautionBackgroundBrush";
    private const string CautionBorderBrushKey = "InstallerTheme.CautionBorderBrush";
    private const string InformationBackgroundBrushKey = "InstallerTheme.InformationBackgroundBrush";
    private const string InformationBorderBrushKey = "InstallerTheme.InformationBorderBrush";
    private const string ApplicationIconSizeKey = "InstallerTheme.ApplicationIconSize";
    private const string ApplicationIconMarginKey = "InstallerTheme.ApplicationIconMargin";
    private const string CaptionHeightKey = "InstallerTheme.CaptionHeight";
    private const string CaptionIconSizeKey = "InstallerTheme.CaptionIconSize";

    // The notice's sentences are localized one by one and read as a single paragraph
    private const string SentenceSeparator = " ";

    // Parts of the window template in Theme.xaml, which draws the title bar
    private const string CaptionIconPartName = "PART_CaptionIcon";
    private const string CloseButtonPartName = "PART_CloseButton";

    // Launched apps wait in the tray; a freshly installed Task Manager would otherwise open its window
    private static readonly string[] PostInstallLaunchArguments = [AppLauncher.HiddenArgument];

    private readonly EmbeddedPayloadCatalog _catalog;
    private readonly IReadOnlyList<DetectedInstallation> _installations;
    private readonly List<PayloadSelection> _payloadSelections = [];
    private readonly List<ApplicationIcon> _applicationIcons = [];
    private readonly List<Control> _inputs = [];
    private readonly StackPanel _root = new();
    private readonly RadioButton _localRadioButton;
    private readonly RadioButton _systemRadioButton;
    private readonly RadioButton _portableRadioButton;
    private readonly TextBox _directoryTextBox = new() { IsReadOnly = true };
    private readonly Button _browseButton;
    private readonly CheckBox _desktopShortcutCheckBox;
    private readonly CheckBox _startMenuShortcutCheckBox;
    private readonly CheckBox _launchCheckBox;
    private readonly StackPanel _progressPanel = new() { Visibility = Visibility.Collapsed };
    private readonly ProgressBar _progressBar = new()
    {
        Minimum = ProgressMinimum,
        Maximum = ProgressMaximum,
        Value = ProgressMinimum
    };
    private readonly TextBlock _statusTextBlock = new();
    private readonly Button _installButton;
    private readonly Button _cancelButton;
    private readonly bool _isExample;
    private readonly bool _exampleFails;
    private readonly List<BitmapFrame> _iconFrames = [];
    private Image? _captionIcon;
    private InstallApplicationTracker? _applicationTracker;
    private bool _installRunning;
    private bool _installFinished;

    /// <summary>One suite row. <paramref name="InstalledLabel"/> says where the application is already installed, or is null.</summary>
    private sealed record PayloadSelection(
        EmbeddedPayload Payload,
        CheckBox CheckBox,
        TextBlock StateTextBlock,
        string? InstalledLabel);

    /// <summary>The mask that paints one suite row's icon, and the frames it picks from as the scaling changes.</summary>
    private sealed record ApplicationIcon(ImageBrush Mask, IReadOnlyList<BitmapFrame> Frames);

    /// <summary>
    /// Builds the whole page from the catalog, the Windhawk probe, the battery probe and the installations
    /// already on the machine, which also decide the installation type the page starts on.
    /// <paramref name="isExample"/> swaps the engine for a simulation that writes nothing, so the window can
    /// be run on its own; <paramref name="exampleFails"/> makes that simulation fail partway.
    /// </summary>
    public InstallerWindow(
        EmbeddedPayloadCatalog catalog,
        WindhawkDetection windhawk,
        bool hasSystemBattery,
        IReadOnlyList<DetectedInstallation> installations,
        bool isExample = false,
        bool exampleFails = false)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));
        FrameworkCompatibility.ThrowIfNull(windhawk, nameof(windhawk));
        FrameworkCompatibility.ThrowIfNull(installations, nameof(installations));

        _isExample = isExample;
        _exampleFails = exampleFails;
        _catalog = catalog;
        _installations = installations;
        Style = StyleResource(WindowStyleKey);
        Title = ResolveTitle(catalog);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = LoadIcon();

        _browseButton = new Button
        {
            Content = L(nameof(AppStrings.Installer_Location_Browse)),
            IsEnabled = false
        };
        _desktopShortcutCheckBox = new CheckBox
        {
            Content = L(nameof(AppStrings.Installer_Options_DesktopShortcut)),
            IsChecked = false
        };
        _startMenuShortcutCheckBox = new CheckBox
        {
            Content = L(nameof(AppStrings.Installer_Options_StartMenuShortcut)),
            IsChecked = true
        };
        _launchCheckBox = new CheckBox
        {
            Content = L(nameof(AppStrings.Installer_Options_Launch)),
            IsChecked = true
        };
        _installButton = new Button
        {
            Content = L(nameof(AppStrings.Installer_Button_Install)),
            Style = StyleResource(AccentButtonStyleKey)
        };
        _cancelButton = new Button { Content = L(nameof(AppStrings.Installer_Button_Cancel)) };
        // An existing installation picks the starting type, whose folder is where that installation lives, so
        // installing again replaces it rather than adding a second copy
        InstallMode initialMode = InstallationDetector.PreferredMode(installations) ?? InstallMode.Local;
        _localRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_Local_Title)),
            L(nameof(AppStrings.Installer_Type_Local_Description)),
            isChecked: initialMode == InstallMode.Local);
        _systemRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_System_Title)),
            L(nameof(AppStrings.Installer_Type_System_Description)),
            isChecked: initialMode == InstallMode.System);
        _portableRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_Portable_Title)),
            L(nameof(AppStrings.Installer_Type_Portable_Description)),
            isChecked: false);

        _root.Margin = ThicknessResource(OuterMarginKey);
        _root.Children.Add(CreateHeader(catalog));
        if (!windhawk.IsInstalled) _root.Children.Add(CreateWindhawkNotice());
        if (catalog.IsBundle) _root.Children.Add(CreateAppSelection(catalog, hasSystemBattery));
        if (installations.Count > 0) _root.Children.Add(CreateInstalledNotice(catalog, installations, initialMode));
        _root.Children.Add(CreateInstallTypeSection());
        _root.Children.Add(CreateLocationSection());
        _root.Children.Add(CreateOptionsSection());
        _root.Children.Add(CreateProgressSection());
        _root.Children.Add(CreateButtonRow());
        ApplyStackSpacing(_root, ThicknessResource(SectionSpacingKey));
        Content = _root;

        _localRadioButton.Checked += OnModeChecked;
        _systemRadioButton.Checked += OnModeChecked;
        _portableRadioButton.Checked += OnModeChecked;
        _browseButton.Click += OnBrowseClick;
        _installButton.Click += OnInstallClick;
        _cancelButton.Click += OnCancelClick;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;

        ApplyMode(initialMode);
        UpdateApplicationIcons(VisualTreeHelper.GetDpi(this));
    }

    /// <summary>
    /// The mode the user last selected. It is tracked rather than read back from the radio group because
    /// the group unchecks its other buttons only after the newly checked button raises its event.
    /// </summary>
    private InstallMode SelectedMode { get; set; } = InstallMode.Local;

    // Section builders

    private static string ResolveTitle(EmbeddedPayloadCatalog catalog)
    {
        if (catalog.IsBundle) return L(nameof(AppStrings.Installer_Title_Bundle));
        if (catalog.Payloads.Count == 0) return L(nameof(AppStrings.Installer_Title_Empty));

        return Format(nameof(AppStrings.Installer_Title_Format), catalog.Payloads[0].ApplicationName);
    }

    private StackPanel CreateHeader(EmbeddedPayloadCatalog catalog)
    {
        string subtitle;
        switch (catalog.Payloads.Count)
        {
            case 0:
                subtitle = L(nameof(AppStrings.Installer_Subtitle_NoApps));
                break;
            case 1:
                subtitle = Format(nameof(AppStrings.Installer_Subtitle_Version_Format), catalog.Payloads[0].Version);
                break;
            default:
                subtitle = Format(nameof(AppStrings.Installer_Subtitle_Apps_Format), catalog.Payloads.Count);
                break;
        }

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = ResolveTitle(catalog),
            Style = StyleResource(TitleTextStyleKey)
        });
        header.Children.Add(CreateSecondaryText(subtitle));
        ApplyStackSpacing(header, ThicknessResource(DescriptionSpacingKey));
        return header;
    }

    /// <summary>The caution card shown only when Windhawk was not found, with the two links it advertises.</summary>
    private Border CreateWindhawkNotice()
    {
        StackPanel links = new() { Orientation = Orientation.Horizontal };
        links.Children.Add(CreateLinkButton(L(nameof(AppStrings.Installer_Windhawk_GetLink)), WindhawkURL));
        links.Children.Add(CreateLinkButton(L(nameof(AppStrings.Installer_Windhawk_ModLink)), WindhawkModURL));
        ApplyStackSpacing(links, ThicknessResource(InlineSpacingKey));

        StackPanel content = new();
        content.Children.Add(new TextBlock { Text = L(nameof(AppStrings.Installer_Windhawk_Notice)) });
        content.Children.Add(links);
        ApplyStackSpacing(content, ThicknessResource(ItemSpacingKey));

        Border notice = new()
        {
            Child = content,
            Padding = ThicknessResource(NoticePaddingKey),
            CornerRadius = CornerRadiusResource(NoticeCornerRadiusKey),
            BorderThickness = ThicknessResource(NoticeBorderThicknessKey)
        };
        // Resource references rather than fixed brushes, so a palette swap reaches the card too
        notice.SetResourceReference(Border.BackgroundProperty, CautionBackgroundBrushKey);
        notice.SetResourceReference(Border.BorderBrushProperty, CautionBorderBrushKey);
        return notice;
    }

    /// <summary>
    /// The card that reports installations already on the machine, just above the installation type it explains.
    /// A single-application installer names each installed copy and its folder; the suite counts the applications
    /// and leaves the details to the labels at the end of their rows.
    /// </summary>
    private Border CreateInstalledNotice(
        EmbeddedPayloadCatalog catalog,
        IReadOnlyList<DetectedInstallation> installations,
        InstallMode initialMode)
    {
        List<string> sentences = [];
        if (catalog.IsBundle)
        {
            int applicationCount = InstallationDetector.CountApplications(installations);
            sentences.Add(applicationCount == 1
                ? L(nameof(AppStrings.Installer_Installed_BundleOne))
                : Format(nameof(AppStrings.Installer_Installed_Bundle_Format), applicationCount));
        }
        else
        {
            foreach (DetectedInstallation installation in installations) sentences.Add(DescribeInstallation(installation));
        }

        // Counts the copies the starting type would replace, not the applications
        sentences.Add(InstallationDetector.CountInMode(installations, initialMode) == 1
            ? L(nameof(AppStrings.Installer_Installed_PresetOne))
            : L(nameof(AppStrings.Installer_Installed_PresetMany)));

        Border notice = new()
        {
            Child = new TextBlock { Text = string.Join(SentenceSeparator, sentences) },
            Padding = ThicknessResource(NoticePaddingKey),
            CornerRadius = CornerRadiusResource(NoticeCornerRadiusKey),
            BorderThickness = ThicknessResource(NoticeBorderThicknessKey)
        };
        notice.SetResourceReference(Border.BackgroundProperty, InformationBackgroundBrushKey);
        notice.SetResourceReference(Border.BorderBrushProperty, InformationBorderBrushKey);
        return notice;
    }

    private static string DescribeInstallation(DetectedInstallation installation) => installation.Mode switch
    {
        InstallMode.System => Format(
            nameof(AppStrings.Installer_Installed_System_Format),
            installation.ApplicationName,
            installation.Directory),
        InstallMode.Local => Format(
            nameof(AppStrings.Installer_Installed_Local_Format),
            installation.ApplicationName,
            installation.Directory),
        _ => throw new ArgumentOutOfRangeException(nameof(installation), installation.Mode, UnsupportedInstallModeMessage)
    };

    private StackPanel CreateAppSelection(EmbeddedPayloadCatalog catalog, bool hasSystemBattery)
    {
        List<UIElement> rows = [];
        foreach (EmbeddedPayload payload in catalog.Payloads)
        {
            bool isBatteryApp = string.Equals(
                payload.ApplicationName,
                BatteryApplicationName,
                StringComparison.OrdinalIgnoreCase);
            bool showBatteryHint = isBatteryApp && !hasSystemBattery;
            CheckBox checkBox = new() { IsChecked = !showBatteryHint };
            checkBox.Content = CreateApplicationLabel(payload.ApplicationName, checkBox);
            // The content is a panel, so accessibility tools need the name spelled out
            AutomationProperties.SetName(checkBox, payload.ApplicationName);
            // Says where the application is already installed until a run starts, then the run's state for the
            // applications it includes
            TextBlock stateTextBlock = new()
            {
                Style = StyleResource(ApplicationStateTextStyleKey),
                Visibility = Visibility.Collapsed
            };
            PayloadSelection selection = new(payload, checkBox, stateTextBlock, InstalledLabel(payload.ApplicationName));
            _payloadSelections.Add(selection);
            ShowInstalledState(selection);
            _inputs.Add(checkBox);
            if (!showBatteryHint)
            {
                rows.Add(CreateApplicationRow(checkBox, stateTextBlock));
                continue;
            }

            StackPanel checkBoxWithHint = new() { Orientation = Orientation.Horizontal };
            checkBoxWithHint.Children.Add(checkBox);
            TextBlock hint = CreateSecondaryText(L(nameof(AppStrings.Installer_Apps_NoBatteryHint)));
            hint.VerticalAlignment = VerticalAlignment.Center;
            checkBoxWithHint.Children.Add(hint);
            ApplyStackSpacing(checkBoxWithHint, ThicknessResource(InlineSpacingKey));
            rows.Add(CreateApplicationRow(checkBoxWithHint, stateTextBlock));
        }

        return CreateSection(L(nameof(AppStrings.Installer_Apps_Header)), rows);
    }

    /// <summary>One application row: the selector on the left, and the install state a run shows at the right edge.</summary>
    private static Grid CreateApplicationRow(UIElement selector, TextBlock stateTextBlock)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(selector, value: 0);
        Grid.SetColumn(stateTextBlock, value: 1);
        row.Children.Add(selector);
        row.Children.Add(stateTextBlock);
        return row;
    }

    /// <summary>
    /// A suite row's check box content: the application's icon, then its name. The icons are white line art drawn
    /// for the dark taskbar, so the icon is a mask over a fill bound to the check box's text colour. That keeps it
    /// visible in the light palette and dims it with the row when the inputs are disabled.
    /// </summary>
    private StackPanel CreateApplicationLabel(string applicationName, CheckBox checkBox)
    {
        StackPanel label = new() { Orientation = Orientation.Horizontal };
        List<BitmapFrame> frames = LoadIconFrames(InstallerIcons.ResourceName(applicationName));
        if (frames.Count > 0)
        {
            double iconSize = DoubleResource(ApplicationIconSizeKey);
            // The frame is picked once the scaling is known; see UpdateApplicationIcons
            ImageBrush mask = new();
            Rectangle icon = new()
            {
                Width = iconSize,
                Height = iconSize,
                Margin = ThicknessResource(ApplicationIconMarginKey),
                VerticalAlignment = VerticalAlignment.Center,
                OpacityMask = mask
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            icon.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground)) { Source = checkBox });
            _applicationIcons.Add(new ApplicationIcon(mask, frames));
            label.Children.Add(icon);
        }

        label.Children.Add(new TextBlock { Text = applicationName, VerticalAlignment = VerticalAlignment.Center });
        return label;
    }

    /// <summary>The label a suite row shows while the application is already installed, or null when it is not.</summary>
    private string? InstalledLabel(string applicationName)
    {
        IReadOnlyList<InstallMode> modes = InstallationDetector.ModesOf(_installations, applicationName);
        switch (modes.Count)
        {
            case 0:
                return null;
            case 1:
                return Format(nameof(AppStrings.Installer_Apps_Installed_Format), ModeTitle(modes[0]));
            default:
                return Format(
                    nameof(AppStrings.Installer_Apps_InstalledBoth_Format),
                    ModeTitle(modes[0]),
                    ModeTitle(modes[1]));
        }
    }

    /// <summary>The installation type's radio button title, so a row label names the type the way the page does.</summary>
    private static string ModeTitle(InstallMode mode) => mode switch
    {
        InstallMode.Local => L(nameof(AppStrings.Installer_Type_Local_Title)),
        InstallMode.System => L(nameof(AppStrings.Installer_Type_System_Title)),
        InstallMode.Portable => L(nameof(AppStrings.Installer_Type_Portable_Title)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, UnsupportedInstallModeMessage)
    };

    private StackPanel CreateInstallTypeSection()
    {
        List<UIElement> modes = [];
        modes.Add(_localRadioButton);
        modes.Add(_systemRadioButton);
        modes.Add(_portableRadioButton);
        _inputs.Add(_localRadioButton);
        _inputs.Add(_systemRadioButton);
        _inputs.Add(_portableRadioButton);
        return CreateSection(L(nameof(AppStrings.Installer_Type_Header)), modes);
    }

    private StackPanel CreateLocationSection()
    {
        string header = L(nameof(AppStrings.Installer_Location_Header));
        // Browse sits at the end of the header row, which leaves the box the full width of the window
        TextBlock headerText = CreateSectionHeader(header);
        headerText.VerticalAlignment = VerticalAlignment.Center;
        _browseButton.Margin = ThicknessResource(BrowseButtonMarginKey);
        _browseButton.VerticalAlignment = VerticalAlignment.Center;
        Grid headerRow = new();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerText, value: 0);
        Grid.SetColumn(_browseButton, value: 1);
        headerRow.Children.Add(headerText);
        headerRow.Children.Add(_browseButton);

        // The box holds a panel-free single line, so the header doubles as its accessible name
        AutomationProperties.SetName(_directoryTextBox, header);
        _inputs.Add(_directoryTextBox);
        _inputs.Add(_browseButton);

        List<UIElement> rows = [];
        rows.Add(_directoryTextBox);
        return CreateSection(headerRow, rows);
    }

    private StackPanel CreateOptionsSection()
    {
        List<UIElement> options = [];
        options.Add(_desktopShortcutCheckBox);
        options.Add(_startMenuShortcutCheckBox);
        options.Add(_launchCheckBox);
        _inputs.Add(_desktopShortcutCheckBox);
        _inputs.Add(_startMenuShortcutCheckBox);
        _inputs.Add(_launchCheckBox);
        return CreateSection(L(nameof(AppStrings.Installer_Options_Header)), options);
    }

    private StackPanel CreateProgressSection()
    {
        _progressPanel.Children.Add(_progressBar);
        _progressPanel.Children.Add(_statusTextBlock);
        ApplyStackSpacing(_progressPanel, ThicknessResource(ItemSpacingKey));
        return _progressPanel;
    }

    private StackPanel CreateButtonRow()
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(_installButton);
        row.Children.Add(_cancelButton);
        ApplyStackSpacing(row, ThicknessResource(ButtonSpacingKey));
        return row;
    }

    private StackPanel CreateSection(string header, IReadOnlyList<UIElement> items) =>
        CreateSection(CreateSectionHeader(header), items);

    /// <summary>Stacks a header element above its items, for a section whose header row carries more than text.</summary>
    private StackPanel CreateSection(UIElement header, IReadOnlyList<UIElement> items)
    {
        StackPanel section = new();
        section.Children.Add(header);
        foreach (UIElement item in items) section.Children.Add(item);
        ApplyStackSpacing(section, ThicknessResource(ItemSpacingKey));
        return section;
    }

    private TextBlock CreateSectionHeader(string text) =>
        new() { Text = text, Style = StyleResource(SectionHeaderTextStyleKey) };

    private RadioButton CreateModeRadioButton(string title, string description, bool isChecked)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock { Text = title });
        content.Children.Add(CreateSecondaryText(description));
        ApplyStackSpacing(content, ThicknessResource(DescriptionSpacingKey));

        RadioButton radioButton = new()
        {
            GroupName = ModeGroupName,
            Content = content,
            IsChecked = isChecked,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        // The content is a panel, so accessibility tools need the title spelled out
        AutomationProperties.SetName(radioButton, title);
        return radioButton;
    }

    private Button CreateLinkButton(string text, string url)
    {
        Button button = new()
        {
            Content = text,
            Style = StyleResource(HyperlinkButtonStyleKey),
            Tag = url
        };
        button.Click += OnLinkClick;
        return button;
    }

    private TextBlock CreateSecondaryText(string text) =>
        new() { Text = text, Style = StyleResource(SecondaryTextStyleKey) };

    /// <summary>
    /// Spaces a stack by giving every child but the last a margin. WPF panels have no spacing of their own
    /// on this framework, and a margin on the last child would pad the bottom of the section.
    /// </summary>
    private static void ApplyStackSpacing(Panel panel, Thickness spacing)
    {
        int lastIndex = panel.Children.Count - 1;
        for (int index = 0; index < lastIndex; index++)
        {
            if (panel.Children[index] is FrameworkElement element) element.Margin = spacing;
        }
    }

    /// <summary>
    /// Picks the window icon out of the embedded set: a single-application installer shows that
    /// application's icon, while a bundle or an installer with no payload shows the suite icon.
    /// </summary>
    private ImageSource? LoadIcon()
    {
        List<string> applicationNames = [];
        foreach (EmbeddedPayload payload in _catalog.Payloads) applicationNames.Add(payload.ApplicationName);

        // Every frame is kept, so the title bar can draw the one nearest its size rather than shrink the largest
        _iconFrames.AddRange(LoadIconFrames(InstallerIcons.ResourceNameForApplications(applicationNames)));
        if (_iconFrames.Count == 0)
            _iconFrames.AddRange(LoadIconFrames(InstallerIcons.ResourceName(InstallerIcons.SuiteIconName)));

        BitmapFrame? largestFrame = null;
        foreach (BitmapFrame frame in _iconFrames)
        {
            if (largestFrame == null || frame.PixelWidth > largestFrame.PixelWidth) largestFrame = frame;
        }

        return largestFrame;
    }

    /// <summary>Decodes every frame of an embedded icon. Returns none when the factory carries no such icon.</summary>
    private static List<BitmapFrame> LoadIconFrames(string resourceName)
    {
        List<BitmapFrame> frames = [];
        try
        {
            using Stream? stream = InstallerIcons.Open(resourceName);
            if (stream == null) return frames;

            // OnLoad decodes every frame up front, so the frames survive the stream being closed
            IconBitmapDecoder decoder = new(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (frame.CanFreeze) frame.Freeze();
                frames.Add(frame);
            }
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"InstallerWindow: could not load the icon {resourceName}", exception);
            frames.Clear();
        }

        return frames;
    }

    /// <summary>
    /// The frame drawn nearest <paramref name="size"/> units at this scaling, so an icon stays sharp instead of
    /// being scaled down from the largest image. Null when there are no frames.
    /// </summary>
    private static BitmapFrame? SelectIconFrame(IReadOnlyList<BitmapFrame> frames, double size, double scale)
    {
        List<int> frameWidths = [];
        foreach (BitmapFrame frame in frames) frameWidths.Add(frame.PixelWidth);
        int frameIndex = InstallerIcons.SelectFrameIndex(frameWidths, (int)Math.Ceiling(size * scale));
        return frameIndex >= 0 ? frames[frameIndex] : null;
    }

    // Title bar

    /// <summary>Wires the title bar the window template draws: the close button, and the icon's system menu.</summary>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        Button? closeButton = GetTemplateChild(CloseButtonPartName) as Button;
        if (closeButton != null)
        {
            // The button shows only a glyph, so screen readers need its name spelled out
            AutomationProperties.SetName(closeButton, L(nameof(AppStrings.Installer_Button_Close)));
            closeButton.Click += OnCloseButtonClick;
        }

        _captionIcon = GetTemplateChild(CaptionIconPartName) as Image;
        if (_captionIcon == null) return;

        _captionIcon.MouseLeftButtonDown += OnCaptionIconMouseDown;
        UpdateCaptionIcon(VisualTreeHelper.GetDpi(this));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateCaptionIcon(newDpi);
        UpdateApplicationIcons(newDpi);
    }

    /// <summary>Points the title bar icon at the frame drawn nearest its size on this monitor.</summary>
    private void UpdateCaptionIcon(DpiScale dpi)
    {
        if (_captionIcon == null) return;

        BitmapFrame? frame = SelectIconFrame(_iconFrames, DoubleResource(CaptionIconSizeKey), dpi.DpiScaleX);
        if (frame != null) _captionIcon.Source = frame;
    }

    /// <summary>Points every suite row icon at the frame drawn nearest its size on this monitor.</summary>
    private void UpdateApplicationIcons(DpiScale dpi)
    {
        double iconSize = DoubleResource(ApplicationIconSizeKey);
        foreach (ApplicationIcon icon in _applicationIcons)
        {
            BitmapFrame? frame = SelectIconFrame(icon.Frames, iconSize, dpi.DpiScaleX);
            if (frame != null) icon.Mask.ImageSource = frame;
        }
    }

    // Closing goes through the system command so it still passes OnClosing, which holds the window open while
    // an install is running
    private void OnCloseButtonClick(object sender, RoutedEventArgs eventArgs) => SystemCommands.CloseWindow(this);

    /// <summary>Opens the system menu below the title bar, as clicking the icon of a Windows title bar does.</summary>
    private void OnCaptionIconMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        Image? icon = sender as Image;
        if (icon == null) return;

        eventArgs.Handled = true;
        double iconLeft = icon.TranslatePoint(new Point(0, 0), this).X;
        // PointToScreen answers in device pixels, while ShowSystemMenu takes device independent units
        Point devicePoint = PointToScreen(new Point(iconLeft, DoubleResource(CaptionHeightKey)));
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        SystemCommands.ShowSystemMenu(this, new Point(devicePoint.X / dpi.DpiScaleX, devicePoint.Y / dpi.DpiScaleY));
    }

    // Interaction

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        WindowInteropHelper interopHelper = new(this);
        SystemTheme.ApplyWindowChrome(interopHelper.Handle);
        // The handle now exists, so the monitor's scaling is known for certain
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        UpdateCaptionIcon(dpi);
        UpdateApplicationIcons(dpi);
    }

    private void OnModeChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not RadioButton radioButton) return;

        ApplyMode(ModeForRadioButton(radioButton));
    }

    /// <summary>Maps the button that raised the event to its mode, which the group state cannot yet give.</summary>
    private InstallMode ModeForRadioButton(RadioButton radioButton)
    {
        if (ReferenceEquals(radioButton, _systemRadioButton)) return InstallMode.System;
        if (ReferenceEquals(radioButton, _portableRadioButton)) return InstallMode.Portable;

        return InstallMode.Local;
    }

    private void ApplyMode(InstallMode mode)
    {
        SelectedMode = mode;
        bool portable = mode == InstallMode.Portable;
        _directoryTextBox.Text = InstallDefaults.DefaultDirectory(mode, _catalog);
        _directoryTextBox.IsReadOnly = !portable;
        _browseButton.IsEnabled = portable;
        _desktopShortcutCheckBox.IsEnabled = !portable;
        _startMenuShortcutCheckBox.IsEnabled = !portable;
    }

    private void OnLinkClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button button || button.Tag is not string url) return;

        try
        {
            ProcessStartInfo startInfo = new() { FileName = url, UseShellExecute = true };
            Process? browser = Process.Start(startInfo);
            browser?.Dispose();
        }
        catch (Exception exception)
        {
            InstallerLog.Write($"InstallerWindow: could not open {url}", exception);
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowInteropHelper interopHelper = new(this);
        string? selectedDirectory = FolderPicker.PickFolder(
            interopHelper.Handle,
            L(nameof(AppStrings.Installer_Location_PickerTitle)),
            _directoryTextBox.Text.Trim(),
            out string? failureMessage);
        if (failureMessage != null)
        {
            ShowStatus(Format(nameof(AppStrings.Installer_Error_BrowseFailed_Format), failureMessage));
            return;
        }

        if (selectedDirectory == null) return;

        _directoryTextBox.Text = selectedDirectory;
    }

    private void OnCancelClick(object sender, RoutedEventArgs eventArgs) => Close();

    private async void OnInstallClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_installFinished)
        {
            Close();
            return;
        }

        if (_installRunning) return;

        InstallPlan? plan = BuildPlan(out string? validationError);
        if (plan == null)
        {
            ShowStatus(validationError ?? L(nameof(AppStrings.Installer_Status_Failed)));
            return;
        }

        _installRunning = true;
        SetInputsEnabled(false);
        _installButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;
        _progressBar.Value = ProgressMinimum;
        ShowStatus(L(nameof(AppStrings.Installer_Status_Starting)));
        StartApplicationStates(plan);
        InstallerLog.Write(
            $"InstallerWindow: starting {plan.Mode} install of {plan.Payloads.Count} app(s) into {plan.TargetDirectory}");

        DispatcherProgress<InstallProgressLine> progress = new(Dispatcher, ApplyProgress);
        DispatcherProgress<InstallApplicationStatus> applicationProgress = new(Dispatcher, ApplyApplicationStatus);
        InstallOutcome outcome;
        try
        {
            // The example simulation never leaves this thread, so neither engine is reachable from it
            if (_isExample)
                outcome = await ExampleMode.RunAsync(plan, progress, applicationProgress, _exampleFails, CancellationToken.None);
            else
                outcome = plan.Mode == InstallMode.System
                    ? await Task.Run(() => ElevatedInstallWorker.RunElevatedAsync(
                        plan, progress, applicationProgress, CancellationToken.None))
                    : await Task.Run(() => InstallEngine.RunAsync(plan, progress, applicationProgress, CancellationToken.None));
        }
        catch (Exception exception)
        {
            InstallerLog.Write("InstallerWindow.OnInstallClick", exception);
            outcome = new InstallOutcome(Success: false, exception.Message, []);
        }

        // Reports wait at Normal priority; this continuation inherits the click's Send priority and would overtake them
        // Yielding at Normal runs every queued report first, so the outcome settles only what the reports left open
        await Dispatcher.Yield(DispatcherPriority.Normal);
        FinishInstall(plan, outcome);
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        // The engine has child installers running; closing now would orphan them mid-copy
        if (_installRunning) eventArgs.Cancel = true;
    }

    private InstallPlan? BuildPlan(out string? validationError)
    {
        validationError = null;
        List<EmbeddedPayload> selected = [];
        if (_catalog.IsBundle)
        {
            foreach (PayloadSelection selection in _payloadSelections)
            {
                if (selection.CheckBox.IsChecked == true) selected.Add(selection.Payload);
            }
        }
        else
        {
            selected.AddRange(_catalog.Payloads);
        }

        if (selected.Count == 0)
        {
            validationError = L(nameof(AppStrings.Installer_Error_NoApps));
            return null;
        }

        InstallMode mode = SelectedMode;
        string targetDirectory;
        if (mode == InstallMode.Portable)
        {
            string text = _directoryTextBox.Text.Trim();
            if (text.Length == 0 || !TryGetRootedFullPath(text, out targetDirectory))
            {
                validationError = L(nameof(AppStrings.Installer_Error_PortableDirectory));
                return null;
            }
        }
        else
        {
            targetDirectory = InstallDefaults.DefaultDirectory(mode, _catalog);
        }

        bool portable = mode == InstallMode.Portable;
        return new InstallPlan(
            mode,
            targetDirectory,
            selected,
            CreateDesktopShortcut: !portable && _desktopShortcutCheckBox.IsChecked == true,
            CreateStartMenuShortcut: !portable && _startMenuShortcutCheckBox.IsChecked == true);
    }

    private static bool TryGetRootedFullPath(string text, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(text);
            return Path.IsPathRooted(fullPath);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private void ApplyProgress(InstallProgressLine line)
    {
        // A line arriving after the run finished must not replace the final status
        if (line.IsFailure || _installFinished) return;

        _progressBar.Value = line.Percent;
        ShowStatus(line.Message);
    }

    /// <summary>Starts every application of the plan Waiting, so each selected row shows a state for the whole run.</summary>
    private void StartApplicationStates(InstallPlan plan)
    {
        List<string> applicationNames = [];
        foreach (EmbeddedPayload payload in plan.Payloads) applicationNames.Add(payload.ApplicationName);

        _applicationTracker = new InstallApplicationTracker(applicationNames);
        ShowApplicationStates();
    }

    private void ApplyApplicationStatus(InstallApplicationStatus status)
    {
        if (_applicationTracker?.Apply(status) != true) return;

        ShowApplicationStates();
    }

    /// <summary>
    /// Shows each application's state at the end of its row.
    /// An application the run does not include keeps showing where it is already installed, so an unselected row
    /// stays as it was. A single-application installer has no rows at all.
    /// </summary>
    private void ShowApplicationStates()
    {
        if (_applicationTracker == null) return;

        foreach (PayloadSelection selection in _payloadSelections)
        {
            InstallApplicationState? state = _applicationTracker.StateOf(selection.Payload.ApplicationName);
            if (state == null)
            {
                ShowInstalledState(selection);
                continue;
            }

            string label = ApplicationStateLabel(state.Value);
            selection.StateTextBlock.Text = label;
            selection.StateTextBlock.Style = StyleResource(ApplicationStateStyleKey(state.Value));
            selection.StateTextBlock.Visibility = Visibility.Visible;
            // The state sits beside the check box rather than in it, so accessibility tools need it as the item status
            AutomationProperties.SetItemStatus(selection.CheckBox, label);
        }
    }

    /// <summary>Shows where the application is already installed at the end of its row, or nothing when it is not.</summary>
    private void ShowInstalledState(PayloadSelection selection)
    {
        if (selection.InstalledLabel == null)
        {
            selection.StateTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        selection.StateTextBlock.Text = selection.InstalledLabel;
        selection.StateTextBlock.Style = StyleResource(ApplicationStateTextStyleKey);
        selection.StateTextBlock.Visibility = Visibility.Visible;
        // The label sits beside the check box rather than in it, so accessibility tools need it as the item status
        AutomationProperties.SetItemStatus(selection.CheckBox, selection.InstalledLabel);
    }

    private static string ApplicationStateLabel(InstallApplicationState state) => state switch
    {
        InstallApplicationState.Waiting => L(nameof(AppStrings.Installer_Apps_State_Waiting)),
        InstallApplicationState.Installing => L(nameof(AppStrings.Installer_Apps_State_Installing)),
        InstallApplicationState.Installed => L(nameof(AppStrings.Installer_Apps_State_Installed)),
        InstallApplicationState.Failed => L(nameof(AppStrings.Installer_Apps_State_Failed)),
        InstallApplicationState.NotInstalled => L(nameof(AppStrings.Installer_Apps_State_NotInstalled)),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, UnsupportedApplicationStateMessage)
    };

    private static string ApplicationStateStyleKey(InstallApplicationState state) => state switch
    {
        InstallApplicationState.Waiting or InstallApplicationState.NotInstalled => ApplicationStateTextStyleKey,
        InstallApplicationState.Installing => ApplicationStateInstallingTextStyleKey,
        InstallApplicationState.Installed => ApplicationStateInstalledTextStyleKey,
        InstallApplicationState.Failed => ApplicationStateFailedTextStyleKey,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, UnsupportedApplicationStateMessage)
    };

    private void FinishInstall(InstallPlan plan, InstallOutcome outcome)
    {
        _installRunning = false;
        _installFinished = true;
        _installButton.Content = L(nameof(AppStrings.Installer_Button_Close));
        _installButton.IsEnabled = true;
        // The outcome settles whatever the reports left open, so every row keeps a final state
        _applicationTracker?.Finish(outcome.Success);
        ShowApplicationStates();
        if (!outcome.Success)
        {
            ShowStatus(outcome.ErrorMessage ?? L(nameof(AppStrings.Installer_Status_Failed)));
            return;
        }

        _progressBar.Value = ProgressMaximum;
        ShowStatus(Format(nameof(AppStrings.Installer_Status_Complete_Format), plan.TargetDirectory));
        if (_launchCheckBox.IsChecked != true) return;

        // Nothing was installed, so there is nothing to start
        if (_isExample)
        {
            InstallerLog.Write("InstallerWindow: example mode, so the launch checkbox starts nothing");
            return;
        }

        foreach (EmbeddedPayload payload in plan.Payloads)
        {
            string executablePath = InstallDefaults.InstalledExecutablePath(
                plan.Mode,
                plan.TargetDirectory,
                payload.ApplicationName);
            AppLauncher.Launch(executablePath, PostInstallLaunchArguments);
        }
    }

    private void ShowStatus(string text)
    {
        _progressPanel.Visibility = Visibility.Visible;
        _statusTextBlock.Text = text;
    }

    private void SetInputsEnabled(bool enabled)
    {
        foreach (Control input in _inputs) input.IsEnabled = enabled;
    }

    // Resources and strings

    private Style StyleResource(string key) => (Style)FindResource(key);

    private Thickness ThicknessResource(string key) => (Thickness)FindResource(key);

    private CornerRadius CornerRadiusResource(string key) => (CornerRadius)FindResource(key);

    private double DoubleResource(string key) => (double)FindResource(key);

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);

    private static string L(string key) => LocalizationManager.Instance[key];

    /// <summary>
    /// Marshals engine reports from worker threads onto the interface thread.
    /// Every instance queues at the same priority,
    /// so progress lines and application statuses are handled in the order they were reported.
    /// </summary>
    private sealed class DispatcherProgress<TReport>(Dispatcher dispatcher, Action<TReport> handler) : IProgress<TReport>
    {
        public void Report(TReport value)
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => handler(value)));
        }
    }
}
