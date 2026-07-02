#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BatteryTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;
using BatteryInstallScope = TrayAppDotNETCommon.InstallScope;

namespace BatteryTrayAppDotNET.UI.Settings;

public enum BatterySettingsPage
{
    General,
    Triggers,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About,
}

public sealed class BatterySettingsWindow : SettingsWindowCommon<BatterySettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Action<string, BatteryInstallScope> _showUninstaller;
    private StackPanel? _triggerPanel;
    private Border? _draggedTriggerRow;
    private BatteryTriggerEntry? _draggedTrigger;
    private Point _triggerDragStart;
    private double _draggedTriggerPointerOffsetY;
    private double _draggedTriggerHeight;
    private int _draggedTriggerTargetIndex = -1;
    private TrayAppDotNETAboutPage? _aboutPage;

    public BatterySettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public BatterySettingsWindow(AppSettings settings, Action<string, BatteryInstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 900,
            height: 640,
            minWidth: 680,
            minHeight: 500,
            AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(BatterySettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        BatterySettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override BatterySettingsPage DefaultPageKey => BatterySettingsPage.General;

    protected override string HeaderText => L("SettingsWindow_Header", "Settings");

    protected override string OpenSettingsFolderText =>
        L("SettingsWindow_OpenSettingsFolder", "Open settings folder");

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override IReadOnlyList<SettingsPageDescriptor<BatterySettingsPage>> CreatePageDescriptors() =>
    [
        new(BatterySettingsPage.General, L("Settings_Common_Page_General", "General"), BuildGeneralPage),
        new(BatterySettingsPage.Triggers, L("Settings_Common_Page_Triggers", "Triggers"), BuildTriggersPage),
        new(BatterySettingsPage.Flyout, L("Settings_Common_Page_Flyout", "Flyout"), BuildFlyoutPage),
        new(BatterySettingsPage.TrayIcon, L("Settings_Common_Page_TrayIcon", "Tray Icon"), BuildTrayIconPage),
        new(BatterySettingsPage.Hotkeys, L("Settings_Common_Page_Hotkeys", "Hotkeys"), BuildHotkeysPage),
        new(BatterySettingsPage.Theme, L("Settings_Common_Page_Theme", "Theme"), BuildThemePage),
        new(BatterySettingsPage.About, L("Settings_Common_Page_About", "About"), BuildAboutPage),
    ];

    protected override void Save()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override void OnSettingsWindowClosed()
    {
        StopAboutUpdateRefresh();
        base.OnSettingsWindowClosed();
    }

    internal void StopAboutUpdateRefresh()
    {
        _aboutPage?.StopUpdateRefresh();
        _aboutPage = null;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
    };

    private Control BuildSettingsPage(BatterySettingsPage page, Func<Control> buildPage)
    {
        if (page != BatterySettingsPage.About)
            StopAboutUpdateRefresh();

        return buildPage();
    }

    private StackPanel BuildGeneralPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.General, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_General_SectionHeader", "General"), p);

            TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
            stack.Children.Add(commonSection.BuildStartupCard());
            commonSection.AddInstallationSection(
                stack,
                [
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.LocalAppData,
                        Title = L("Settings_General_LocalUser_Title", "Local user"),
                        ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                        Elevated = false,
                        Install = static () => AppServices.Installation.InstallToLocalAppData(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.LocalAppDataInstallDirectory,
                                BatteryInstallScope.LocalAppData);
                            return Task.CompletedTask;
                        },
                    },
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.ProgramFiles,
                        Title = L("Settings_General_SystemWide_Title", "System-wide"),
                        ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                        Elevated = true,
                        Install = static () => AppServices.Installation.InstallSystemWide(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.ProgramFilesInstallDirectory,
                                BatteryInstallScope.ProgramFiles);
                            return Task.CompletedTask;
                        },
                    },
                ],
                new TrayAppDotNETStoreInstallOptions(
                    L("Settings_General_WindowsStore_Title", "Windows Store"),
                    StoreInstallDescription));
            CreateKeepWarmSettingsSection(p).AddCards(stack);

            return stack;
        });

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = static () => AppServices.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                AppServices.Startup.SetRunOnStartup(enabled);
                _settings.RunOnStartup = enabled;
            },
            GetCurrentStartupShortcutTarget = static () => AppServices.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = static () => AppServices.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = static () => AppServices.Installation.DetectAll(),
            CurrentBuildNumber = BuildInfo.BuildNumber,
        });

    private TrayAppDotNETKeepWarmSettingsSection CreateKeepWarmSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETKeepWarmSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            Settings = _settings,
            SupportsFlyout = true,
            SupportsTrayContextMenu = true,
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == BatteryInstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? L("Settings_General_StoreRunning", "Running from Windows Store")
            : L("Settings_General_StoreNotInstalled", "Not installed from Windows Store");
    }

    private StackPanel BuildTriggersPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Triggers, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Triggers_SectionHeader", "Triggers"), p);
            stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_Triggers_Description",
                    "Drag cards, or use Ctrl+Up/Ctrl+Down, to change trigger order."),
                p,
                new Thickness(0, 0, 0, 12)));

            _settings.EnsureTriggerDefaults();
            _triggerPanel = new StackPanel();
            RenderTriggerCards();
            stack.Children.Add(_triggerPanel);
            return stack;
        });

    private void RenderTriggerCards()
    {
        if (_triggerPanel == null) return;
        _triggerPanel.Children.Clear();

        if (_settings.Triggers.Count == 0)
        {
            _triggerPanel.Children.Add(RawCard(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_Triggers_Empty", "No triggers configured."), Palette), Palette));
            return;
        }

        for (int i = 0; i < _settings.Triggers.Count; i++)
            _triggerPanel.Children.Add(BuildTriggerCard(_settings.Triggers[i], i, Palette));
    }

    private Border BuildTriggerCard(BatteryTriggerEntry trigger, int index, SettingsPalette p)
    {
        TextBlock title = TrayAppDotNETSettingsUI.TitleText(
            string.IsNullOrWhiteSpace(trigger.Title) ? $"Trigger {index + 1}" : trigger.Title,
            p);
        title.FontWeight = FontWeight.SemiBold;

        SettingsComboBox condition = BuildNullableTriggerCombo(
            L("Settings_Triggers_Condition_Placeholder", "Condition"),
            TriggerConditionOptions(),
            trigger.Condition,
            value => trigger.Condition = value,
            p);
        SettingsComboBox action = BuildNullableTriggerCombo(
            L("Settings_Triggers_Action_Placeholder", "Action"),
            TriggerActionOptions(),
            trigger.Action,
            value => trigger.Action = value,
            p);

        TextBlock arrow = TrayAppDotNETSettingsUI.Text("->", p, 14, FontWeight.SemiBold);
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Margin = new Thickness(8, 0);

        Grid selectorRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star) { MinWidth = 153 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star) { MinWidth = 153 },
            },
        };
        condition.HorizontalAlignment = HorizontalAlignment.Stretch;
        action.HorizontalAlignment = HorizontalAlignment.Stretch;
        selectorRow.Children.Add(condition);
        Grid.SetColumn(arrow, 1);
        selectorRow.Children.Add(arrow);
        Grid.SetColumn(action, 2);
        selectorRow.Children.Add(action);

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(title);
        content.Children.Add(selectorRow);

        Border card = new()
        {
            Tag = trigger,
            Background = TrayAppDotNETSettingsUI.Brush(p.CardBackground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = RadiusLarge,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 0, 0, 6),
            Child = content,
            Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);

        card.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
        };
        card.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
        };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (IsTriggerCardInteractiveSource(e.Source, card)) return;
            _draggedTrigger = trigger;
            _draggedTriggerRow = card;
            _triggerDragStart = e.GetPosition(_triggerPanel);
            _draggedTriggerPointerOffsetY = e.GetPosition(card).Y;
            _draggedTriggerHeight = Math.Max(1, card.Bounds.Height);
            _draggedTriggerTargetIndex = _settings.Triggers.IndexOf(trigger);
            pointerPressed = true;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
            e.Pointer.Capture(card);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (_draggedTrigger == null || _triggerPanel == null) return;
            Point current = e.GetPosition(_triggerPanel);
            if (Math.Abs(current.Y - _triggerDragStart.Y) < 4) return;
            double draggedMidpoint = current.Y - _draggedTriggerPointerOffsetY + _draggedTriggerHeight / 2.0;
            _draggedTriggerTargetIndex = TriggerInsertionIndexFromMidpoint(draggedMidpoint);
            ApplyTriggerDragPreview();
            card.RenderTransform = new TranslateTransform(0, current.Y - _triggerDragStart.Y);
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            pointerPressed = false;
            EndTriggerDrag(e.Pointer);
        };
        card.PointerCaptureLost += (_, _) =>
        {
            pointerPressed = false;
            EndTriggerDrag(null);
        };
        card.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;

            int currentIndex = _settings.Triggers.IndexOf(trigger);
            int nextIndex = e.Key == Key.Up ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex >= 0 && nextIndex >= 0 && nextIndex < _settings.Triggers.Count)
            {
                _settings.Triggers.RemoveAt(currentIndex);
                _settings.Triggers.Insert(nextIndex, trigger);
                Save();
                RenderTriggerCards();
            }

            e.Handled = true;
        };

        TrayAppDotNETToolTip.SetTip(
            card,
            L("Settings_Triggers_Card_ToolTip", "Drag to reorder, or press Ctrl+Up/Ctrl+Down."));
        return card;
    }

    private SettingsComboBox BuildNullableTriggerCombo<TEnum>(
        string placeholder,
        IReadOnlyList<(TEnum Value, string Text)> options,
        TEnum? selected,
        Action<TEnum?> set,
        SettingsPalette p)
        where TEnum : struct, Enum
    {
        SettingsComboBox combo = TrayAppDotNETSettingsUI.ComboBox(p, 153);
        combo.Width = double.NaN;
        combo.MinWidth = 153;
        combo.Items.Add(PlaceholderComboItem(placeholder, p));
        foreach ((TEnum value, string text) in options)
            combo.Items.Add(new SettingsComboBoxItem(value.ToString(), text, p));

        if (selected.HasValue)
            TrayAppDotNETSettingsUI.SelectComboByTag(combo, selected.Value.ToString());
        else
            combo.SelectedIndex = 0;

        combo.SelectionChanged += (_, _) =>
        {
            string? tag = combo.SelectedItem?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                set(null);
                Save();
                return;
            }

            if (!Enum.TryParse(tag, out TEnum value)) return;
            set(value);
            Save();
        };
        return combo;
    }

    private static SettingsComboBoxItem PlaceholderComboItem(string text, SettingsPalette p) =>
        new(string.Empty, text, p, () =>
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(text, p);
            label.Foreground = TrayAppDotNETSettingsUI.Brush(p.DisabledForeground);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            return label;
        });

    private static IReadOnlyList<(BatteryTriggerCondition Value, string Text)> TriggerConditionOptions() =>
    [
        (BatteryTriggerCondition.BatteryBelow20, L("Settings_Triggers_Condition_BatteryBelow20", "Battery below 20%")),
        (BatteryTriggerCondition.BatteryBelow10, L("Settings_Triggers_Condition_BatteryBelow10", "Battery below 10%")),
        (BatteryTriggerCondition.BatteryAbove80, L("Settings_Triggers_Condition_BatteryAbove80", "Battery above 80%")),
        (BatteryTriggerCondition.ChargingStarted, L("Settings_Triggers_Condition_ChargingStarted", "Charging started")),
        (BatteryTriggerCondition.ChargingStopped, L("Settings_Triggers_Condition_ChargingStopped", "Charging stopped")),
        (BatteryTriggerCondition.ExternalPowerConnected,
            L("Settings_Triggers_Condition_ExternalPowerConnected", "External power connected")),
        (BatteryTriggerCondition.ExternalPowerDisconnected,
            L("Settings_Triggers_Condition_ExternalPowerDisconnected", "External power disconnected")),
        (BatteryTriggerCondition.FullyCharged, L("Settings_Triggers_Condition_FullyCharged", "Battery full")),
    ];

    private static IReadOnlyList<(BatteryTriggerAction Value, string Text)> TriggerActionOptions() =>
    [
        (BatteryTriggerAction.ShowNotification, L("Settings_Triggers_Action_ShowNotification", "Show notification")),
        (BatteryTriggerAction.OpenFlyout, L("Settings_Triggers_Action_OpenFlyout", "Open flyout")),
        (BatteryTriggerAction.OpenSettings, L("Settings_Triggers_Action_OpenSettings", "Open settings")),
        (BatteryTriggerAction.OpenPowerSettings, L("Settings_Triggers_Action_OpenPowerSettings", "Open power settings")),
    ];

    private void UpdateTriggerCardVisual(
        Border card,
        BatteryTriggerEntry trigger,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(trigger, _draggedTrigger);
        Color background = pointerPressed
            ? p.Pressed
            : pointerOver
                ? p.Hover
                : p.CardBackground;
        card.Background = TrayAppDotNETSettingsUI.Brush(background);
        card.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? p.Accent : Colors.Transparent);
        card.BorderThickness = dragging ? new Thickness(1) : new Thickness(0);
        card.Opacity = dragging ? 0.82 : 1.0;
        card.SetValue(ZIndexProperty, dragging ? 1 : 0);
    }

    private int TriggerInsertionIndexFromMidpoint(double draggedMidpointY)
    {
        if (_triggerPanel == null) return -1;
        int insertion = 0;
        for (int i = 0; i < _triggerPanel.Children.Count; i++)
        {
            Control child = _triggerPanel.Children[i];
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(0, 0), _triggerPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = _settings.Triggers.Count - (_draggedTrigger != null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    private void ApplyTriggerDragPreview()
    {
        if (_triggerPanel == null || _draggedTrigger == null || _draggedTriggerRow == null) return;
        ResetTriggerDragPreview();

        int sourceIndex = _settings.Triggers.IndexOf(_draggedTrigger);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(_draggedTriggerTargetIndex, 0, Math.Max(0, _settings.Triggers.Count - 1));
        double offset = Math.Max(1, _draggedTriggerHeight + Math.Max(0, _draggedTriggerRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetTriggerPreviewOffset(i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex + 1; i <= targetIndex && i < _triggerPanel.Children.Count; i++)
                SetTriggerPreviewOffset(i, -offset);
        }
    }

    private void SetTriggerPreviewOffset(int index, double offset)
    {
        if (_triggerPanel == null) return;
        if (index < 0 || index >= _triggerPanel.Children.Count) return;
        if (ReferenceEquals(_triggerPanel.Children[index], _draggedTriggerRow)) return;
        _triggerPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    private void ResetTriggerDragPreview()
    {
        if (_triggerPanel == null) return;
        foreach (Control child in _triggerPanel.Children)
        {
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndTriggerDrag(IPointer? pointer)
    {
        BatteryTriggerEntry? dragged = _draggedTrigger;
        int targetIndex = _draggedTriggerTargetIndex;
        bool hadDrag = dragged != null;
        bool reordered = false;
        _draggedTriggerRow?.RenderTransform = null;
        if (_triggerPanel != null)
            foreach (Control child in _triggerPanel.Children)
                child.RenderTransform = null;
        _draggedTriggerRow = null;
        _draggedTrigger = null;
        _draggedTriggerTargetIndex = -1;
        _draggedTriggerPointerOffsetY = 0;
        _draggedTriggerHeight = 0;
        pointer?.Capture(null);

        if (dragged != null && targetIndex >= 0)
        {
            int currentIndex = _settings.Triggers.IndexOf(dragged);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                _settings.Triggers.RemoveAt(currentIndex);
                _settings.Triggers.Insert(Math.Clamp(targetIndex, 0, _settings.Triggers.Count), dragged);
                reordered = true;
            }
        }

        if (reordered) Save();
        if (hadDrag) RenderTriggerCards();
    }

    private static bool IsTriggerCardInteractiveSource(object? source, Border card)
    {
        for (Control? control = source as Control; control != null; control = control.GetVisualParent<Control>())
        {
            if (ReferenceEquals(control, card)) return false;
            if (control is SettingsComboBox or TextBox or Avalonia.Controls.Button or ScrollViewer)
                return true;
            if (control.Cursor != null)
                return true;
        }

        return false;
    }

    private StackPanel BuildFlyoutPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Flyout, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Flyout_SectionHeader", "Flyout"), p);

            stack.Children.Add(BoolCard(
                L("Settings_Flyout_RestoreUndockState_Title", "Restore undock state on startup"),
                L("Settings_Flyout_RestoreUndockState_Description",
                    "When the app launches, restore the flyout's docked or undocked state from the previous session. When off, the flyout always opens docked."),
                _settings.RestoreFlyoutUndockedOnStartup,
                v => _settings.RestoreFlyoutUndockedOnStartup = v,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Flyout_Visibility_Header", "Visibility"), p));
            stack.Children.Add(BoolCard(
                L("Settings_Flyout_ShowUndockButton_Title", "Show undock button"),
                L("Settings_Flyout_ShowUndockButton_Description",
                    "Show the undock button in the flyout. When off, the flyout always stays anchored to the tray."),
                _settings.AllowFlyoutUndock,
                v => _settings.AllowFlyoutUndock = v,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Flyout)));

            if (_settings.AllowFlyoutUndock)
            {
                stack.Children.Add(BoolCard(
                    L("Settings_Flyout_ClampUndockedToScreen_Title", "Keep undocked flyout on screen"),
                    L("Settings_Flyout_ClampUndockedToScreen_Description",
                        "Keep the undocked flyout fully inside one monitor's work area when it restores or repositions."),
                    _settings.ClampUndockedFlyoutToScreen,
                    v => _settings.ClampUndockedFlyoutToScreen = v,
                    p));
            }

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Flyout_Layout_Header", "Layout"), p));
            stack.Children.Add(BoolCard(
                L("Settings_Flyout_HeaderAtBottom_Title", "Title bar at the bottom"),
                L("Settings_Flyout_HeaderAtBottom_Description",
                    "Render the flyout title bar at the bottom of the flyout instead of the top."),
                _settings.FlyoutHeaderAtBottom,
                v => _settings.FlyoutHeaderAtBottom = v,
                p));

            return stack;
        });

    private StackPanel BuildTrayIconPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.TrayIcon, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_TrayIcon_SectionHeader", "Tray Icon"), p);

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_TrayIcon_ContextMenu_Header", "Context menu"), p));
            stack.Children.Add(ComboCard(
                L("Settings_TrayIcon_MenuPosition_Title", "Menu position"),
                L("Settings_TrayIcon_MenuPosition_Description",
                    "Choose where the right-click tray menu opens."),
                [
                    (ContextMenuPosition.Classic.ToString(), L("Settings_TrayIcon_MenuPosition_Classic", "Classic")),
                    (ContextMenuPosition.Modern.ToString(), L("Settings_TrayIcon_MenuPosition_Modern", "Modern")),
                ],
                _settings.ContextMenuPosition.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ContextMenuPosition value))
                        _settings.ContextMenuPosition = value;
                },
                p,
                autoSizeToText: true,
                autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem));

            return stack;
        });

    private StackPanel BuildHotkeysPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Hotkeys, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Hotkeys_SectionHeader", "Hotkeys"), p);
            stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_Hotkeys_SectionDescription",
                    "Add keyboard shortcuts for common battery tray actions."),
                p,
                new Thickness(0, 0, 0, 16)));

            TextBox searchBox = TrayAppDotNETSettingsUI.TextBox(p, 240);
            StackPanel searchRow = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            };
            TextBlock searchLabel = TrayAppDotNETSettingsUI.TitleText(
                L("Settings_Hotkeys_SearchLabel", "Search"), p);
            searchLabel.VerticalAlignment = VerticalAlignment.Center;
            searchLabel.Margin = new Thickness(0, 0, 8, 0);
            searchRow.Children.Add(searchLabel);
            searchRow.Children.Add(searchBox);
            stack.Children.Add(searchRow);

            List<(Control Control, string SearchText)> rows = [];
            AddHotkeyRow(
                stack,
                rows,
                HotkeyAction.OpenFlyout,
                L("Settings_Hotkeys_OpenFlyout_Title", "Open flyout"),
                L("Settings_Hotkeys_OpenFlyout_Description", "Show battery details above the tray icon."),
                p);
            AddHotkeyRow(
                stack,
                rows,
                HotkeyAction.OpenSettings,
                L("Settings_Hotkeys_OpenSettings_Title", "Open settings"),
                L("Settings_Hotkeys_OpenSettings_Description", "Open the BatteryTrayAppDotNET settings window."),
                p);

            searchBox.TextChanged += (_, _) =>
            {
                string query = (searchBox.Text ?? string.Empty).Trim();
                foreach ((Control row, string searchText) in rows)
                {
                    row.IsVisible = query.Length == 0
                                    || searchText.Contains(query, StringComparison.OrdinalIgnoreCase);
                }
            };

            return stack;
        });

    private void AddHotkeyRow(
        StackPanel stack,
        List<(Control Control, string SearchText)> rows,
        HotkeyAction action,
        string title,
        string description,
        SettingsPalette p)
    {
        StackPanel entries = new() { Spacing = 0 };
        uint selectedModifiers = 0;
        uint selectedVk = 0;

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, 170);
        modifiers.Padding = new Thickness(8, 0, 2, 0);
        foreach (TrayAppDotNETHotkeyModifierOption option in TrayAppDotNETHotkeyModifierOptions.Create(L))
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, 60);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = new Cursor(StandardCursorType.Ibeam);

        SettingsButton addButton = Button(L("Settings_Hotkeys_Add_Button", "Add"), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVk == 0)
            {
                addButton.Text = L("Settings_Hotkeys_Add_Button", "Add");
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, string.Empty)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVk);
            addButton.Text = exists
                ? L("Settings_Hotkeys_Exists_Button", "Exists")
                : L("Settings_Hotkeys_Add_Button", "Add");
            addButton.IsEnabled = !exists;
        }

        void Refresh()
        {
            HotkeyApplyResult? applyResult = null;
            try { applyResult = AppServices.HotkeyService?.Apply(_settings.Hotkeys); }
            catch (Exception ex) { TADNLog.Log($"BatterySettingsWindow.Hotkeys.Apply: {ex.Message}"); }

            entries.Children.Clear();
            foreach (HotkeyBinding binding in _settings.Hotkeys
                         .Where(h => !h.RemovedByUser && h.Matches(action, string.Empty))
                         .OrderBy(h => h.BindingID))
                entries.Children.Add(BuildHotkeyEntryCard(action, binding, applyResult, Refresh, p));
            entries.IsVisible = entries.Children.Count > 0;
            UpdateAddButtonState();
        }

        modifiers.SelectionChanged += (_, _) =>
        {
            selectedModifiers = modifiers.SelectedItem is { Tag: uint mods } ? mods : 0;
            UpdateAddButtonState();
        };
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
            {
                e.Handled = true;
                return;
            }

            uint vk = TrayAppDotNETHotkeyKeys.VirtualKeyFromKey(e.Key);
            if (vk is 0 or 0x7B)
            {
                e.Handled = true;
                return;
            }

            selectedVk = vk;
            keyBox.Text = TrayAppDotNETHotkeyKeys.KeyName(vk);
            UpdateAddButtonState();
            e.Handled = true;
        };
        addButton.Click += (_, _) =>
        {
            if (!addButton.IsEnabled || selectedModifiers == 0 || selectedVk == 0) return;
            int id = _settings.Hotkeys.Where(h => h.Matches(action, string.Empty))
                .Select(h => h.BindingID)
                .DefaultIfEmpty(0)
                .Max() + 1;
            _settings.Hotkeys.Add(new HotkeyBinding
            {
                Action = action,
                Parameter = string.Empty,
                Modifiers = selectedModifiers,
                VirtualKey = selectedVk,
                Enabled = true,
                BindingID = id,
            });
            selectedModifiers = 0;
            selectedVk = 0;
            modifiers.SelectedIndex = -1;
            keyBox.Text = string.Empty;
            Save();
            Refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 240 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, p));
        text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(description, p));
        grid.Children.Add(text);

        modifiers.Margin = new Thickness(0, 0, 8, 0);
        keyBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(modifiers, 1);
        Grid.SetColumn(keyBox, 2);
        Grid.SetColumn(addButton, 3);
        grid.Children.Add(modifiers);
        grid.Children.Add(keyBox);
        grid.Children.Add(addButton);

        entries.Margin = new Thickness(0, 8, 8, 0);
        Grid.SetRow(entries, 1);
        Grid.SetColumn(entries, 1);
        Grid.SetColumnSpan(entries, 2);
        grid.Children.Add(entries);

        Border card = RawCard(grid, p);
        rows.Add((card, title + "\n" + description));
        stack.Children.Add(card);
        Refresh();
    }

    private Border BuildHotkeyEntryCard(
        HotkeyAction action,
        HotkeyBinding binding,
        HotkeyApplyResult? applyResult,
        Action refresh,
        SettingsPalette p)
    {
        TextBlock display = TrayAppDotNETSettingsUI.Text(FormatHotkey(binding), p);
        display.VerticalAlignment = VerticalAlignment.Center;
        display.Margin = new Thickness(12, 6, 0, 6);

        TextBlock status = TrayAppDotNETSettingsUI.Text(string.Empty, p);
        status.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 8, 0);

        if (AppServices.HotkeyService == null)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(
                status,
                L("Settings_Hotkeys_Status_HotkeyServiceUnavailable", "Hotkey service is unavailable."));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound)
        {
            TrayAppDotNETToolTip.SetTip(
                status,
                L("Settings_Hotkeys_Status_Registered", "Registered"));
        }

        SettingsButton delete = Button("x", p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = 20;
        TrayAppDotNETToolTip.SetTip(
            delete,
            L("Settings_Hotkeys_DeleteHotkey_ToolTip", "Delete hotkey"));
        delete.Click += (_, _) =>
        {
            if (AppSettings.IsDefaultHotkeyIdentity(action, string.Empty, binding.BindingID))
            {
                binding.RemovedByUser = true;
                binding.Enabled = false;
            }
            else
            {
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, string.Empty, binding.BindingID));
            }

            Save();
            refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(display);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(status);
        grid.Children.Add(delete);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            CornerRadius = RadiusMedium,
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
        };
    }

    private static string FormatHotkey(HotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private StackPanel BuildThemePage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Theme, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Theme_SectionHeader", "Theme"), p);
            AppTheme theme = AppServices.Theme ?? AppTheme.Default;

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_ContextMenu_Header", "Context menu"), p));
            stack.Children.Add(IntCard(
                L("Settings_Theme_FontSize_Title", "Font size"),
                L("Settings_Theme_FontSize_Description", "Adjust the right-click tray menu font size."),
                _settings.ContextMenuFontSize,
                AppSettings.ContextMenuFontSizeMin,
                AppSettings.ContextMenuFontSizeMax,
                v => _settings.ContextMenuFontSize = v,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_Appearance_Header", "Appearance"), p));
            stack.Children.Add(ComboCard(
                L("Settings_Theme_ThemeStyle_Title", "Theme"),
                L("Settings_Theme_ThemeStyle_Description", "Choose the app theme mode."),
                [
                    (ThemeMode.System.ToString(), L("Settings_Theme_ThemeStyle_System", "System")),
                    (ThemeMode.Light.ToString(), L("Settings_Theme_ThemeStyle_Light", "Light")),
                    (ThemeMode.Dark.ToString(), L("Settings_Theme_ThemeStyle_Dark", "Dark")),
                ],
                _settings.ThemeMode.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ThemeMode value))
                        _settings.ThemeMode = value;
                },
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme)));
            stack.Children.Add(VariantColorCard(
                "Text",
                L("Settings_Theme_TextColor_Title", "Text color"),
                L("Settings_Theme_TextColor_Description", "Override text color for each theme variant."),
                L("Settings_Theme_TextColor_LightTooltip", "Light theme text color"),
                L("Settings_Theme_TextColor_DarkTooltip", "Dark theme text color"),
                _settings.TextColor,
                theme.Foreground.Light,
                theme.Foreground.Dark,
                p));
            stack.Children.Add(VariantColorCard(
                "Background",
                L("Settings_Theme_BackgroundColor_Title", "Background color"),
                L("Settings_Theme_BackgroundColor_Description", "Override background color for each theme variant."),
                L("Settings_Theme_BackgroundColor_LightTooltip", "Light theme background color"),
                L("Settings_Theme_BackgroundColor_DarkTooltip", "Dark theme background color"),
                _settings.BackgroundColor,
                theme.Background.Light,
                theme.Background.Dark,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_Flyout_Header", "Flyout"), p));
            stack.Children.Add(VariantColorCard(
                "FlyoutBackground",
                L("Settings_Theme_FlyoutBackgroundColor_Title", "Flyout background"),
                L("Settings_Theme_FlyoutBackgroundColor_Description",
                    "Override the main battery flyout background color."),
                L("Settings_Theme_FlyoutBackgroundColor_LightTooltip", "Light flyout background"),
                L("Settings_Theme_FlyoutBackgroundColor_DarkTooltip", "Dark flyout background"),
                _settings.FlyoutBackgroundColor,
                theme.Background.Light,
                theme.Background.Dark,
                p));
            stack.Children.Add(VariantColorCard(
                "FlyoutTitleBarBackground",
                L("Settings_Theme_FlyoutTitleBarBackgroundColor_Title", "Titlebar background"),
                L("Settings_Theme_FlyoutTitleBarBackgroundColor_Description",
                    "Override the battery flyout titlebar background color."),
                L("Settings_Theme_FlyoutTitleBarBackgroundColor_LightTooltip", "Light titlebar background"),
                L("Settings_Theme_FlyoutTitleBarBackgroundColor_DarkTooltip", "Dark titlebar background"),
                _settings.FlyoutTitleBarBackgroundColor,
                theme.FooterBackground.Light,
                theme.FooterBackground.Dark,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_Window_Header", "Windows"), p));
            stack.Children.Add(BoolCard(
                L("Settings_Theme_RoundedCorners_Title", "Rounded corners"),
                L("Settings_Theme_RoundedCorners_Description", "Use rounded corners on BatteryTrayAppDotNET windows."),
                _settings.EnableRoundedCorners,
                v => _settings.EnableRoundedCorners = v,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme)));
            stack.Children.Add(ComboCard(
                L("Settings_Theme_Animations_Title", "Animations"),
                L("Settings_Theme_Animations_Description", "Controls whether tooltip fades and other UI animations are allowed."),
                [
                    (TrayAppDotNETAnimationMode.System.ToString(), L("Settings_Theme_Animations_System", "System")),
                    (TrayAppDotNETAnimationMode.Disabled.ToString(), L("Settings_Theme_Animations_Disabled", "Disabled")),
                    (TrayAppDotNETAnimationMode.Enabled.ToString(), L("Settings_Theme_Animations_Enabled", "Enabled")),
                ],
                _settings.AnimationMode.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                        _settings.AnimationMode = value;
                },
                p,
                afterSave: () =>
                {
                    if (Application.Current != null)
                        TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                    RebuildShell(BatterySettingsPage.Theme);
                }));
            stack.Children.Add(IntCard(
                L("Settings_Theme_ToolTipShowDelay_Title", "Tooltip delay"),
                L("Settings_Theme_ToolTipShowDelay_Description", "Milliseconds to wait before showing a tooltip."),
                _settings.ToolTipShowDelayMs,
                TimeConstants.ToolTipShowDelayMinMs,
                TimeConstants.ToolTipShowDelayMaxMs,
                v =>
                {
                    _settings.ToolTipShowDelayMs = v;
                    TrayAppDotNETToolTip.ShowDelayMs = v;
                    TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
                },
                p,
                " ms"));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_TrayIcon_Header", "Tray icon"), p));
            stack.Children.Add(VariantColorCard(
                "TrayIcon",
                L("Settings_Theme_StaticIconColor_Title", "Tray icon color"),
                L("Settings_Theme_StaticIconColor_Description",
                    "Override the tray icon color for each theme variant."),
                L("Settings_Theme_StaticIconColor_LightTooltip", "Light theme tray icon color"),
                L("Settings_Theme_StaticIconColor_DarkTooltip", "Dark theme tray icon color"),
                _settings.TrayIconColor,
                theme.Foreground.Light,
                theme.Foreground.Dark,
                p));

            return stack;
        });

    private StackPanel BuildAboutPage()
    {
        _aboutPage = new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L("Settings_About_Tagline", "A tray-based battery status monitor."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Shutdown = () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            },
            Log = TADNLog.Log,
            RebuildAboutPage = () => RebuildShell(BatterySettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
        });
        return _aboutPage.Build();
    }
}
