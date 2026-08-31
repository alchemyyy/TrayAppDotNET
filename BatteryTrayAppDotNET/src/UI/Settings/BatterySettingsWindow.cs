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
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;
using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;
using BatteryInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace BatteryTrayAppDotNET.UI.Settings;

public enum BatterySettingsPage
{
    General,
    Triggers,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

public sealed class BatterySettingsWindow : SettingsWindowCommon<BatterySettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Action<string, BatteryInstallScope> _showUninstaller;
    private readonly List<StackPanel> _triggerPagePanels = [];
    private readonly List<TrayAppDotNETAboutPage> _aboutPageGenerations = [];
    private StackPanel? _triggerPanel;
    private StackPanel? _draggedTriggerPanel;
    private Border? _draggedTriggerRow;
    private BatteryTriggerEntry? _draggedTrigger;
    private IPointer? _triggerCapturedPointer;
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
        ConfigureCompactSettingsWindow(L(nameof(AppStrings.SettingsWindow_Title)), AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(BatterySettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette ResolvePalette() =>
        BatterySettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override bool UseWindows11SettingsNavigation => _settings.UseWindows11SettingsNavigation;

    protected override ISettingsSidebarWidthSettings SidebarWidthSettings => _settings;

    protected override BatterySettingsPage DefaultPageKey => BatterySettingsPage.General;

    protected override string HeaderText => L(nameof(AppStrings.SettingsWindow_Header));

    protected override string OpenSettingsFolderText =>
        L(nameof(AppStrings.SettingsWindow_OpenSettingsFolder));

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override IReadOnlyList<SettingsPageDescriptor<BatterySettingsPage>> CreatePageDescriptors() =>
    [
        new(BatterySettingsPage.General, L(nameof(AppStrings.Settings_Common_Page_General)),
            () => NameSettingsPage(BatterySettingsPage.General, BuildGeneralPage()),
            CommonSettingsNavigationGlyphs.General),
        new(BatterySettingsPage.Triggers, L(nameof(AppStrings.Settings_Common_Page_Triggers)),
            () => NameSettingsPage(BatterySettingsPage.Triggers, BuildTriggersPage()),
            CommonSettingsNavigationGlyphs.Triggers),
        new(BatterySettingsPage.Flyout, L(nameof(AppStrings.Settings_Common_Page_Flyout)),
            () => NameSettingsPage(BatterySettingsPage.Flyout, BuildFlyoutPage()),
            CommonSettingsNavigationGlyphs.Flyout),
        new(BatterySettingsPage.TrayIcon, L(nameof(AppStrings.Settings_Common_Page_TrayIcon)),
            () => NameSettingsPage(BatterySettingsPage.TrayIcon, BuildTrayIconPage()),
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(BatterySettingsPage.Hotkeys, L(nameof(AppStrings.Settings_Common_Page_Hotkeys)),
            () => NameSettingsPage(BatterySettingsPage.Hotkeys, BuildHotkeysPage()),
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(BatterySettingsPage.Theme, L(nameof(AppStrings.Settings_Common_Page_Theme)),
            () => NameSettingsPage(BatterySettingsPage.Theme, BuildThemePage()),
            CommonSettingsNavigationGlyphs.Theme),
        new(BatterySettingsPage.About, L(nameof(AppStrings.Settings_Common_Page_About)),
            () => NameSettingsPage(BatterySettingsPage.About, BuildAboutPage()),
            CommonSettingsNavigationGlyphs.About)
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
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private static Control BuildSettingsPage(Func<Control> buildPage) => buildPage();

    private Control NameSettingsPage(BatterySettingsPage page, Control control)
    {
        ControlNames.AssignLogicalSubtree(control, page.ToString());
        return control;
    }

    private StackPanel BuildGeneralPage() =>
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_General_SectionHeader)), p);

            TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
            stack.Children.Add(commonSection.BuildStartupCard());
            commonSection.AddInstallationSection(
                stack,
                [
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.LocalAppData,
                        Title = L(nameof(AppStrings.Settings_General_LocalUser_Title)),
                        ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                        Elevated = false,
                        Install = static () => AppServices.Installation.InstallToLocalAppData(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.LocalAppDataInstallDirectory,
                                BatteryInstallScope.LocalAppData);
                            return Task.CompletedTask;
                        }
                    },
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.ProgramFiles,
                        Title = L(nameof(AppStrings.Settings_General_SystemWide_Title)),
                        ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                        Elevated = true,
                        Install = static () => AppServices.Installation.InstallSystemWide(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.ProgramFilesInstallDirectory,
                                BatteryInstallScope.ProgramFiles);
                            return Task.CompletedTask;
                        }
                    }
                ],
                new TrayAppDotNETStoreInstallOptions(
                    L(nameof(AppStrings.Settings_General_WindowsStore_Title)),
                    StoreInstallDescription));
            CreateRenderingSettingsSection(p).AddCards(stack);

            return stack;
        });

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
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
            CurrentBuildNumber = BuildInfo.BuildNumber
        });

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            TrayMenuSettings = _settings,
            WarmWindowSettings = _settings,
            SupportsFlyoutWarmWindow = true,
            SupportsTrayContextMenuWarmWindow = true
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == BatteryInstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? L(nameof(AppStrings.Settings_General_StoreRunning))
            : L(nameof(AppStrings.Settings_General_StoreNotInstalled));
    }

    private StackPanel BuildTriggersPage() =>
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Triggers_SectionHeader)), p);
            stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L(nameof(AppStrings.Settings_Triggers_Description)),
                p,
                new Thickness(0, 0, 0, 12)));

            _settings.EnsureTriggerDefaults();
            StackPanel triggerPanel = new();
            _triggerPagePanels.Add(triggerPanel);
            _triggerPanel = triggerPanel;
            AddPageCleanup(() => CleanupTriggerPage(triggerPanel));
            RenderTriggerCards();
            stack.Children.Add(triggerPanel);
            return stack;
        });

    private void RenderTriggerCards()
    {
        if (_triggerPanel == null) return;
        _triggerPanel.Children.Clear();

        if (_settings.Triggers.Count == 0)
        {
            Border emptyCard = RawCard(
                TrayAppDotNETSettingsUI.DescriptionText(L(nameof(AppStrings.Settings_Triggers_Empty)), Palette),
                Palette);
            ControlNames.AssignLogicalSubtree(emptyCard, "TriggerCard");
            _triggerPanel.Children.Add(emptyCard);
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
            L(nameof(AppStrings.Settings_Triggers_Condition_Placeholder)),
            TriggerConditionOptions(),
            trigger.Condition,
            value => trigger.Condition = value,
            p);
        SettingsComboBox action = BuildNullableTriggerCombo(
            L(nameof(AppStrings.Settings_Triggers_Action_Placeholder)),
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
                new ColumnDefinition(GridLength.Star) { MinWidth = 153 }
            }
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
            Cursor = TrayAppDotNETCursors.Hand
        };
        ControlNames.Assign(card, "TriggerCard");

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
            StackPanel? triggerPanel = _triggerPanel;
            if (triggerPanel == null) return;

            _draggedTrigger = trigger;
            _draggedTriggerPanel = triggerPanel;
            _draggedTriggerRow = card;
            _triggerCapturedPointer = e.Pointer;
            _triggerDragStart = e.GetPosition(triggerPanel);
            _draggedTriggerPointerOffsetY = e.GetPosition(card).Y;
            _draggedTriggerHeight = Math.Max(1, card.Bounds.Height);
            _draggedTriggerTargetIndex = _settings.Triggers.IndexOf(trigger);
            pointerPressed = true;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
            try
            {
                e.Pointer.Capture(card);
            }
            catch
            {
                pointerPressed = false;
                ClearTriggerDragState();
                UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
                throw;
            }

            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            StackPanel? triggerPanel = _draggedTriggerPanel;
            if (_draggedTrigger == null || triggerPanel == null) return;
            if (!ReferenceEquals(_draggedTriggerRow, card)) return;

            Point current = e.GetPosition(triggerPanel);
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
            L(nameof(AppStrings.Settings_Triggers_Card_ToolTip)));
        Border registeredCard = TrayAppDotNETSettingsCards.RegisterSearchCard(card);
        ControlNames.AssignLogicalSubtree(registeredCard, "TriggerCard");
        return registeredCard;
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
        (BatteryTriggerCondition.BatteryBelow20, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow20))),
        (BatteryTriggerCondition.BatteryBelow10, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow10))),
        (BatteryTriggerCondition.BatteryAbove80, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryAbove80))),
        (BatteryTriggerCondition.ChargingStarted, L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStarted))),
        (BatteryTriggerCondition.ChargingStopped, L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStopped))),
        (BatteryTriggerCondition.ExternalPowerConnected,
            L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerConnected))),
        (BatteryTriggerCondition.ExternalPowerDisconnected,
            L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerDisconnected))),
        (BatteryTriggerCondition.FullyCharged, L(nameof(AppStrings.Settings_Triggers_Condition_FullyCharged)))
    ];

    private static IReadOnlyList<(BatteryTriggerAction Value, string Text)> TriggerActionOptions() =>
    [
        (BatteryTriggerAction.ShowNotification, L(nameof(AppStrings.Settings_Triggers_Action_ShowNotification))),
        (BatteryTriggerAction.OpenFlyout, L(nameof(AppStrings.Settings_Triggers_Action_OpenFlyout))),
        (BatteryTriggerAction.OpenSettings, L(nameof(AppStrings.Settings_Triggers_Action_OpenSettings))),
        (BatteryTriggerAction.OpenPowerSettings, L(nameof(AppStrings.Settings_Triggers_Action_OpenPowerSettings)))
    ];

    private void UpdateTriggerCardVisual(
        Border card,
        BatteryTriggerEntry trigger,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(trigger, _draggedTrigger)
                        && ReferenceEquals(_triggerPanel, _draggedTriggerPanel);
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
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return -1;

        int insertion = 0;
        for (int i = 0; i < triggerPanel.Children.Count; i++)
        {
            Control child = triggerPanel.Children[i];
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(0, 0), triggerPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = _settings.Triggers.Count - (_draggedTrigger != null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    private void ApplyTriggerDragPreview()
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null || _draggedTrigger == null || _draggedTriggerRow == null) return;
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
            for (int i = sourceIndex + 1; i <= targetIndex && i < triggerPanel.Children.Count; i++)
                SetTriggerPreviewOffset(i, -offset);
        }
    }

    private void SetTriggerPreviewOffset(int index, double offset)
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return;
        if (index < 0 || index >= triggerPanel.Children.Count) return;
        if (ReferenceEquals(triggerPanel.Children[index], _draggedTriggerRow)) return;
        triggerPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    private void ResetTriggerDragPreview()
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return;
        foreach (Control child in triggerPanel.Children)
        {
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndTriggerDrag(IPointer? pointer)
    {
        StackPanel? draggedTriggerPanel = _draggedTriggerPanel;
        BatteryTriggerEntry? dragged = _draggedTrigger;
        int targetIndex = _draggedTriggerTargetIndex;
        bool hadDrag = dragged != null;
        bool reordered = false;
        _draggedTriggerRow?.RenderTransform = null;
        if (draggedTriggerPanel != null)
        {
            foreach (Control child in draggedTriggerPanel.Children)
                child.RenderTransform = null;
        }

        ClearTriggerDragState();
        ReleaseTriggerPointerCapture(pointer);

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
        if (hadDrag && ReferenceEquals(_triggerPanel, draggedTriggerPanel)) RenderTriggerCards();
    }

    private void CleanupTriggerPage(StackPanel triggerPanel)
    {
        IPointer? capturedPointer = null;
        if (ReferenceEquals(_draggedTriggerPanel, triggerPanel))
        {
            foreach (Control child in triggerPanel.Children)
                child.RenderTransform = null;

            capturedPointer = _triggerCapturedPointer;
            ClearTriggerDragState();
        }

        _triggerPagePanels.Remove(triggerPanel);
        if (ReferenceEquals(_triggerPanel, triggerPanel))
        {
            _triggerPanel = _triggerPagePanels.Count > 0
                ? _triggerPagePanels[^1]
                : null;
        }

        ReleaseTriggerPointerCapture(capturedPointer);
    }

    private void ClearTriggerDragState()
    {
        _draggedTriggerPanel = null;
        _draggedTriggerRow = null;
        _draggedTrigger = null;
        _triggerCapturedPointer = null;
        _triggerDragStart = default;
        _draggedTriggerTargetIndex = -1;
        _draggedTriggerPointerOffsetY = 0;
        _draggedTriggerHeight = 0;
    }

    private static void ReleaseTriggerPointerCapture(IPointer? pointer)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"BatterySettingsWindow trigger pointer release failed: {exception.Message}");
        }
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
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Flyout_SectionHeader)), p);

            stack.Children.Add(BoolCard(
                L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Title)),
                L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_Description)),
                _settings.RestoreFlyoutUndockedOnStartup,
                v => _settings.RestoreFlyoutUndockedOnStartup = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_SearchKeywords))
                ]));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Flyout_Visibility_Header)), p));
            stack.Children.Add(BoolCard(
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Title)),
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_Description)),
                _settings.AllowFlyoutUndock,
                v => _settings.AllowFlyoutUndock = v,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Flyout),
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_SearchKeywords))
                ]));

            if (_settings.AllowFlyoutUndock)
            {
                stack.Children.Add(BoolCard(
                    L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
                    L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
                    _settings.ClampUndockedFlyoutToScreen,
                    v => _settings.ClampUndockedFlyoutToScreen = v,
                    p,
                    searchKeywords:
                    [
                        L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_SearchKeywords))
                    ]));
            }

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Flyout_Layout_Header)), p));
            stack.Children.Add(BoolCard(
                L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Title)),
                L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Description)),
                _settings.FlyoutHeaderAtBottom,
                v => _settings.FlyoutHeaderAtBottom = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_SearchKeywords))
                ]));

            return stack;
        });

    private StackPanel BuildTrayIconPage() =>
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_TrayIcon_SectionHeader)), p);

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_TrayIcon_ContextMenu_Header)), p));
            stack.Children.Add(ComboCard(
                L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Title)),
                L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Description)),
                [
                    (nameof(ContextMenuPosition.Classic), L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Classic))),
                    (nameof(ContextMenuPosition.Modern), L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_Modern)))
                ],
                _settings.ContextMenuPosition.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ContextMenuPosition value))
                        _settings.ContextMenuPosition = value;
                },
                p,
                autoSizeToText: true,
                autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_TrayIcon_MenuPosition_SearchKeywords))
                ]));

            return stack;
        });

    private StackPanel BuildHotkeysPage() =>
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Hotkeys_SectionHeader)), p);
            stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L(nameof(AppStrings.Settings_Hotkeys_SectionDescription)),
                p,
                new Thickness(0, 0, 0, 16)));

            TextBox searchBox = TrayAppDotNETSettingsUI.TextBox(p, 240);
            StackPanel searchRow = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            TextBlock searchLabel = TrayAppDotNETSettingsUI.TitleText(
                L(nameof(AppStrings.Settings_Hotkeys_SearchLabel)), p);
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
                L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Title)),
                L(nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Description)),
                p);
            AddHotkeyRow(
                stack,
                rows,
                HotkeyAction.OpenSettings,
                L(nameof(AppStrings.Settings_Hotkeys_OpenSettings_Title)),
                L(nameof(AppStrings.Settings_Hotkeys_OpenSettings_Description)),
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
        keyBox.Cursor = TrayAppDotNETCursors.IBeam;

        SettingsButton addButton = Button(L(nameof(AppStrings.Settings_Hotkeys_Add_Button)), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

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
                BindingID = id
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
        return;

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVk == 0)
            {
                addButton.Text = L(nameof(AppStrings.Settings_Hotkeys_Add_Button));
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, string.Empty)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVk);
            addButton.Text = exists
                ? L(nameof(AppStrings.Settings_Hotkeys_Exists_Button))
                : L(nameof(AppStrings.Settings_Hotkeys_Add_Button));
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
            GlyphApplicator.ApplyTo(status, GlyphCatalog.WARNING);
            TrayAppDotNETToolTip.SetTip(
                status,
                L(nameof(AppStrings.Settings_Hotkeys_Status_HotkeyServiceUnavailable)));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            GlyphApplicator.ApplyTo(status, GlyphCatalog.WARNING);
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound)
        {
            TrayAppDotNETToolTip.SetTip(
                status,
                L(nameof(AppStrings.Settings_Hotkeys_Status_Registered)));
        }

        SettingsButton delete = Button(GlyphCatalog.CLOSE, p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = TrayAppDotNETSettingsUI.CloseGlyphFontSize;
        TrayAppDotNETToolTip.SetTip(
            delete,
            L(nameof(AppStrings.Settings_Hotkeys_DeleteHotkey_ToolTip)));
        delete.Click += (_, _) =>
        {
            if (AppSettings.IsDefaultHotkeyIdentity(action, string.Empty, binding.BindingID))
            {
                binding.RemovedByUser = true;
                binding.Enabled = false;
            }
            else
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, string.Empty, binding.BindingID));

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

        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            CornerRadius = RadiusMedium,
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid
        };
        ControlNames.AssignLogicalSubtree(card, "HotkeyBinding");
        return card;
    }

    private static string FormatHotkey(HotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private StackPanel BuildThemePage() =>
        (StackPanel)BuildSettingsPage(() =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Theme_SectionHeader)), p);
            AppTheme theme = AppServices.Theme ?? AppTheme.Default;

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Theme_ContextMenu_Header)), p));
            stack.Children.Add(IntCard(
                L(nameof(AppStrings.Settings_Theme_FontSize_Title)),
                L(nameof(AppStrings.Settings_Theme_FontSize_Description)),
                _settings.ContextMenuFontSize,
                AppSettings.ContextMenuFontSizeMin,
                AppSettings.ContextMenuFontSizeMax,
                v => _settings.ContextMenuFontSize = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_FontSize_SearchKeywords))
                ]));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Theme_Appearance_Header)), p));
            stack.Children.Add(ComboCard(
                L(nameof(AppStrings.Settings_Theme_ThemeStyle_Title)),
                L(nameof(AppStrings.Settings_Theme_ThemeStyle_Description)),
                [
                    (nameof(ThemeMode.System), L(nameof(AppStrings.Settings_Theme_ThemeStyle_System))),
                    (nameof(ThemeMode.Light), L(nameof(AppStrings.Settings_Theme_ThemeStyle_Light))),
                    (nameof(ThemeMode.Dark), L(nameof(AppStrings.Settings_Theme_ThemeStyle_Dark)))
                ],
                _settings.ThemeMode.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ThemeMode value))
                        _settings.ThemeMode = value;
                },
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme),
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_ThemeStyle_SearchKeywords))
                ]));
            stack.Children.Add(BoolCard(
                L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
                L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
                _settings.UseWindows11SettingsNavigation,
                value => _settings.UseWindows11SettingsNavigation = value,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme),
                searchKeywords:
                [
                    L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))
                ]));
            stack.Children.Add(VariantColorCard(
                "Text",
                L(nameof(AppStrings.Settings_Theme_TextColor_Title)),
                L(nameof(AppStrings.Settings_Theme_TextColor_Description)),
                L(nameof(AppStrings.Settings_Theme_TextColor_LightTooltip)),
                L(nameof(AppStrings.Settings_Theme_TextColor_DarkTooltip)),
                _settings.TextColor,
                theme.Foreground.Light,
                theme.Foreground.Dark,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_TextColor_SearchKeywords))
                ]));
            stack.Children.Add(VariantColorCard(
                "Background",
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_Title)),
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_Description)),
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_LightTooltip)),
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
                _settings.BackgroundColor,
                theme.Background.Light,
                theme.Background.Dark,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_BackgroundColor_SearchKeywords))
                ]));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Theme_Flyout_Header)), p));
            stack.Children.Add(VariantColorCard(
                "FlyoutBackground",
                L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Title)),
                L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Description)),
                L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_LightTooltip)),
                L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_DarkTooltip)),
                _settings.FlyoutBackgroundColor,
                theme.Background.Light,
                theme.Background.Dark,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_SearchKeywords))
                ]));
            stack.Children.Add(VariantColorCard(
                "FlyoutTitleBarBackground",
                L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Title)),
                L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Description)),
                L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_LightTooltip)),
                L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_DarkTooltip)),
                _settings.FlyoutTitleBarBackgroundColor,
                theme.FooterBackground.Light,
                theme.FooterBackground.Dark,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_SearchKeywords))
                ]));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Theme_Window_Header)), p));
            stack.Children.Add(BoolCard(
                L(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
                L(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
                _settings.EnableRoundedCorners,
                v => _settings.EnableRoundedCorners = v,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme),
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_RoundedCorners_SearchKeywords))
                ]));
            stack.Children.Add(ComboCard(
                L(nameof(AppStrings.Settings_Theme_Animations_Title)),
                L(nameof(AppStrings.Settings_Theme_Animations_Description)),
                [
                    (nameof(TrayAppDotNETAnimationMode.System), L(nameof(AppStrings.Settings_Theme_Animations_System))),
                    (nameof(TrayAppDotNETAnimationMode.Disabled), L(nameof(AppStrings.Settings_Theme_Animations_Disabled))),
                    (nameof(TrayAppDotNETAnimationMode.Enabled), L(nameof(AppStrings.Settings_Theme_Animations_Enabled)))
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
                },
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_Animations_SearchKeywords))
                ]));
            stack.Children.Add(IntCard(
                L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Title)),
                L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Description)),
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
                " ms",
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_SearchKeywords))
                ]));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L(nameof(AppStrings.Settings_Theme_TrayIcon_Header)), p));
            stack.Children.Add(VariantColorCard(
                "TrayIcon",
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_Title)),
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_LightTooltip)),
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
                _settings.TrayIconColor,
                theme.Foreground.Light,
                theme.Foreground.Dark,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
                ]));

            return stack;
        });

    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(
            new TrayAppDotNETAboutPageOptions
            {
                Palette = Palette,
                ButtonRadius = RadiusMedium,
                CardRadius = RadiusLarge,
                UpdatePromptOwnerBackdrop = ConfirmOverlayBackdrop,
                L = L,
                Save = Save,
                ApplicationName = Constants.ApplicationName,
                Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
                BuildNumber = BuildInfo.BuildNumber,
                CommitHash = BuildInfo.CommitHash,
                Publisher = Constants.Publisher,
                HelpLink = Constants.HelpLink,
                OpenSettingsFolderText = OpenSettingsFolderText,
                SettingsFolderPath = SettingsFolderPath,
                UpdateSettings = _settings,
                UpdateService = static () => AppServices.UpdateCheckService,
                ConfirmAsync = ConfirmAsync,
                PromptOwner = () => this,
                SupportsFlyoutUpdateButton = false,
                Shutdown = () =>
                {
                    if (Application.Current?.ApplicationLifetime
                        is IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                },
                Log = TADNLog.Log,
                RebuildAboutPage = () => RebuildShell(BatterySettingsPage.About),
                StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
                UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs
            }));
        _aboutPageGenerations.Add(aboutPage);
        _aboutPage = aboutPage;
        AddPageCleanup(() =>
        {
            _aboutPageGenerations.Remove(aboutPage);
            if (ReferenceEquals(_aboutPage, aboutPage))
            {
                _aboutPage = _aboutPageGenerations.Count > 0
                    ? _aboutPageGenerations[^1]
                    : null;
            }
        });
        return aboutPage.Build();
    }
}
