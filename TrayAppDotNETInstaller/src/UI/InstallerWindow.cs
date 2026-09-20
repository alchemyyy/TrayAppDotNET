using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrayAppDotNETInstaller.Localization;
using TrayAppDotNETInstaller.Services;

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

    // Theme.xaml resource keys
    private const string WindowStyleKey = "InstallerTheme.WindowStyle";
    private const string TitleTextStyleKey = "InstallerTheme.TitleTextStyle";
    private const string SectionHeaderTextStyleKey = "InstallerTheme.SectionHeaderTextStyle";
    private const string SecondaryTextStyleKey = "InstallerTheme.SecondaryTextStyle";
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

    private readonly EmbeddedPayloadCatalog _catalog;
    private readonly List<PayloadSelection> _payloadSelections = [];
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
    private bool _installRunning;
    private bool _installFinished;

    private sealed record PayloadSelection(EmbeddedPayload Payload, CheckBox CheckBox);

    /// <summary>
    /// Builds the whole page from the catalog, the Windhawk probe and the battery probe.
    /// <paramref name="isExample"/> swaps the engine for a simulation that writes nothing, so the window can
    /// be run on its own; <paramref name="exampleFails"/> makes that simulation fail partway.
    /// </summary>
    public InstallerWindow(
        EmbeddedPayloadCatalog catalog,
        WindhawkDetection windhawk,
        bool hasSystemBattery,
        bool isExample = false,
        bool exampleFails = false)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        _isExample = isExample;
        _exampleFails = exampleFails;
        FrameworkCompatibility.ThrowIfNull(windhawk, nameof(windhawk));

        _catalog = catalog;
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
        _localRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_Local_Title)),
            L(nameof(AppStrings.Installer_Type_Local_Description)),
            isChecked: true);
        _systemRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_System_Title)),
            L(nameof(AppStrings.Installer_Type_System_Description)),
            isChecked: false);
        _portableRadioButton = CreateModeRadioButton(
            L(nameof(AppStrings.Installer_Type_Portable_Title)),
            L(nameof(AppStrings.Installer_Type_Portable_Description)),
            isChecked: false);

        _root.Margin = ThicknessResource(OuterMarginKey);
        _root.Children.Add(CreateHeader(catalog));
        if (!windhawk.IsInstalled) _root.Children.Add(CreateWindhawkNotice());
        if (catalog.IsBundle) _root.Children.Add(CreateAppSelection(catalog, hasSystemBattery));
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

        ApplyMode(InstallMode.Local);
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
            CheckBox checkBox = new() { Content = payload.ApplicationName, IsChecked = !showBatteryHint };
            _payloadSelections.Add(new PayloadSelection(payload, checkBox));
            _inputs.Add(checkBox);
            if (!showBatteryHint)
            {
                rows.Add(checkBox);
                continue;
            }

            StackPanel row = new() { Orientation = Orientation.Horizontal };
            row.Children.Add(checkBox);
            TextBlock hint = CreateSecondaryText(L(nameof(AppStrings.Installer_Apps_NoBatteryHint)));
            hint.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(hint);
            ApplyStackSpacing(row, ThicknessResource(InlineSpacingKey));
            rows.Add(row);
        }

        return CreateSection(L(nameof(AppStrings.Installer_Apps_Header)), rows);
    }

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
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The box holds a panel-free single line, so the header doubles as its accessible name
        AutomationProperties.SetName(_directoryTextBox, header);
        _directoryTextBox.VerticalAlignment = VerticalAlignment.Center;
        _browseButton.Margin = ThicknessResource(BrowseButtonMarginKey);
        _browseButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_directoryTextBox, value: 0);
        Grid.SetColumn(_browseButton, value: 1);
        grid.Children.Add(_directoryTextBox);
        grid.Children.Add(_browseButton);
        _inputs.Add(_directoryTextBox);
        _inputs.Add(_browseButton);

        List<UIElement> rows = [];
        rows.Add(grid);
        return CreateSection(header, rows);
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

    private StackPanel CreateSection(string header, IReadOnlyList<UIElement> items)
    {
        StackPanel section = new();
        section.Children.Add(new TextBlock { Text = header, Style = StyleResource(SectionHeaderTextStyleKey) });
        foreach (UIElement item in items) section.Children.Add(item);
        ApplyStackSpacing(section, ThicknessResource(ItemSpacingKey));
        return section;
    }

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
        try
        {
            List<string> applicationNames = [];
            foreach (EmbeddedPayload payload in _catalog.Payloads) applicationNames.Add(payload.ApplicationName);

            using Stream? stream =
                InstallerIcons.Open(InstallerIcons.ResourceNameForApplications(applicationNames))
                ?? InstallerIcons.Open(InstallerIcons.ResourceName(InstallerIcons.SuiteIconName));
            if (stream == null) return null;

            // OnLoad decodes every frame up front, so the icon survives the stream being closed
            IconBitmapDecoder decoder = new(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame? largestFrame = null;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (largestFrame == null || frame.PixelWidth > largestFrame.PixelWidth) largestFrame = frame;
            }

            if (largestFrame != null && largestFrame.CanFreeze) largestFrame.Freeze();
            return largestFrame;
        }
        catch (Exception exception)
        {
            InstallerLog.Write("InstallerWindow.LoadIcon", exception);
            return null;
        }
    }

    // Interaction

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        WindowInteropHelper interopHelper = new(this);
        SystemTheme.ApplyWindowChrome(interopHelper.Handle);
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
        InstallerLog.Write(
            $"InstallerWindow: starting {plan.Mode} install of {plan.Payloads.Count} app(s) into {plan.TargetDirectory}");

        DispatcherProgress progress = new(Dispatcher, ApplyProgress);
        InstallOutcome outcome;
        try
        {
            // The example simulation never leaves this thread, so neither engine is reachable from it
            if (_isExample)
                outcome = await ExampleMode.RunAsync(plan, progress, _exampleFails, CancellationToken.None);
            else
                outcome = plan.Mode == InstallMode.System
                    ? await Task.Run(() => ElevatedInstallWorker.RunElevatedAsync(plan, progress, CancellationToken.None))
                    : await Task.Run(() => InstallEngine.RunAsync(plan, progress, CancellationToken.None));
        }
        catch (Exception exception)
        {
            InstallerLog.Write("InstallerWindow.OnInstallClick", exception);
            outcome = new InstallOutcome(Success: false, exception.Message, []);
        }

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
        if (line.IsFailure) return;

        _progressBar.Value = line.Percent;
        ShowStatus(line.Message);
    }

    private void FinishInstall(InstallPlan plan, InstallOutcome outcome)
    {
        _installRunning = false;
        _installFinished = true;
        _installButton.Content = L(nameof(AppStrings.Installer_Button_Close));
        _installButton.IsEnabled = true;
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
            AppLauncher.Launch(executablePath);
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

    private static string Format(string key, object argument) =>
        string.Format(CultureInfo.CurrentCulture, L(key), argument);

    private static string L(string key) => LocalizationManager.Instance[key];

    /// <summary>Marshals engine progress from worker threads onto the interface thread.</summary>
    private sealed class DispatcherProgress(Dispatcher dispatcher, Action<InstallProgressLine> handler)
        : IProgress<InstallProgressLine>
    {
        public void Report(InstallProgressLine value)
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => handler(value)));
        }
    }
}
