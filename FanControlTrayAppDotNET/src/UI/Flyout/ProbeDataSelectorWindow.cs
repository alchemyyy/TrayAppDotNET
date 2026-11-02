#if DEBUG
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FanControlTrayAppDotNET.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Drag;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace FanControlTrayAppDotNET.UI.Flyout;

/// <summary>
/// Window for selecting and transforming probe data shown by a probe card.
/// </summary>
public sealed partial class ProbeDataSelectorWindow : FlyoutCompanionWindow
{
    private const string NicknameTargetControlAnchor = "NicknameTargetRegex";
    private const string NicknameReplacementControlAnchor = "NicknameReplacement";
    private const int UpDirection = -1;
    private const int DownDirection = 1;

    private static readonly ProbeSelectorTab[] Tabs =
    [
        ProbeSelectorTab.Home,
        ProbeSelectorTab.Temperatures,
        ProbeSelectorTab.Power,
        ProbeSelectorTab.Load,
        ProbeSelectorTab.Clocks,
        ProbeSelectorTab.Voltages
    ];

    private readonly ProbeCard _probeCard;
    private readonly AppSettings _settings;
    private readonly Action<ProbeCard> _changed;
    private readonly SettingsPalette _palette;
    private ControlNameScope ControlNames { get; }
    private readonly LHMService? _subscribedLHMService;
    private readonly UIResourceScope _windowResources = new(nameof(ProbeDataSelectorWindow));
    private readonly HashSet<string> _expandedTransformKeys = new(StringComparer.OrdinalIgnoreCase);
    private ProbeSelectorVisualGeneration? _activeVisualGeneration;
    private TextBox? _focusedTransformTextBox;
    private ProbeFocusIdentity? _focusIdentity;
    private ProbeSelectorAxamlProperties? _layout;
    private bool _isKeyboardFocus;

    /// <summary>
    /// Initializes the XAML designer constructor.
    /// </summary>
    public ProbeDataSelectorWindow()
    {
        ControlNames = ControlNameScope.For(this);
        _probeCard = null!;
        _settings = null!;
        _changed = static _ => { };
        _palette = null!;
        _subscribedLHMService = null;

        try
        {
            InitializeComponent();
            InitializeComponentState();
            AttachSelectorInputHandlers();
        }
        catch
        {
            DisposeSelectorResources();
            throw;
        }
    }

    /// <summary>
    /// Initializes a selector window for a probe card.
    /// </summary>
    public ProbeDataSelectorWindow(ProbeCard probeCard, AppSettings settings, Action<ProbeCard> changed)
    {
        ControlNames = ControlNameScope.For(this);
        _probeCard = probeCard;
        _settings = settings;
        _changed = changed;
        _settings.EnsureFanProfileLayouts();
        _probeCard.EnsureProfileVisibility(_settings.SelectedFanProfileIndex);
        _palette = AppSettingsPalette.Create(
            AppServices.Theme,
            settings,
            AppTheme.ResolveIsLight(settings, AppServices.Theme));

        _subscribedLHMService = AppServices.LHMService;
        try
        {
            InitializeComponent();
            InitializeComponentState();
            AttachSelectorInputHandlers();
            Title = string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(AppStrings.ProbeSelector_Title_Format)),
                _probeCard.DisplayName);
            if (_subscribedLHMService != null)
            {
                _subscribedLHMService.PollTickCompleted += OnPollTickCompleted;
                _windowResources.Add(() =>
                    _subscribedLHMService.PollTickCompleted -= OnPollTickCompleted);
            }

            RebuildContent(ProbeSelectorTab.Home);
            _settings.Changed += OnSettingsChanged;
            _windowResources.Add(() => _settings.Changed -= OnSettingsChanged);
#if DEBUG
            GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
            _windowResources.Add(() =>
                GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
#endif
        }
        catch
        {
            DisposeSelectorResources();
            throw;
        }
    }

    /// <summary>Refreshes AXAML-backed layout and root geometry after initialization or hot reload.</summary>
    private void InitializeComponentState()
    {
        _layout = AxamlProbeSelector;
        Width = Layout.WindowWidth;
        Height = Layout.WindowHeight;
        MinWidth = Layout.WindowMinWidth;
        MinHeight = Layout.WindowMinHeight;
    }

    /// <summary>
    /// Tags the window as the editor surface, whose control map drives keyboard navigation, and installs the
    /// window-level pointer and focus routing.
    /// </summary>
    private void AttachSelectorInputHandlers()
    {
        this.MapTo(ControlMap.ProbeDataSelector.ID);
        AddHandler(PointerPressedEvent, OnSelectorPointerPressed, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _windowResources.Add(() => RemoveHandler(PointerPressedEvent, OnSelectorPointerPressed));
        AddHandler(GotFocusEvent, OnSelectorGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _windowResources.Add(() => RemoveHandler(GotFocusEvent, OnSelectorGotFocus));
    }

#if DEBUG
    /// <summary>
    /// Rebuilds code-created selector glyphs after a catalog source reload.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (_windowResources.IsDisposed || _activeVisualGeneration == null) return;

        RebuildContent();
    }
#endif

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_windowResources.IsDisposed || _activeVisualGeneration == null) return;

        _palette.UpdateFrom(AppSettingsPalette.Create(
            AppServices.Theme,
            _settings,
            AppTheme.ResolveIsLight(_settings, AppServices.Theme)));
        RebuildContent();
    });

    private ProbeSelectorAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Probe selector layout resources have not been loaded.");

    private ProbeSelectorTab SelectedTab =>
        _activeVisualGeneration?.SelectedTab ?? ProbeSelectorTab.Home;

    private double TruncateToggleTrackHeight =>
        Layout.TruncateToggleTrackWidth * Layout.TruncateToggleTrackHeightRatio;

    private double TruncateToggleThumbSize =>
        Layout.TruncateToggleTrackWidth * Layout.TruncateToggleThumbSizeRatio;

    private CornerRadius TruncateToggleTrackCornerRadius =>
        new(TruncateToggleTrackHeight / 2.0);

    private CornerRadius TruncateToggleThumbCornerRadius =>
        new(TruncateToggleThumbSize / 2.0);

    private Thickness TruncateToggleThumbUncheckedMargin =>
        new(TruncateToggleThumbInset, top: 0, right: 0, bottom: 0);

    private Thickness TruncateToggleThumbCheckedMargin =>
        new(left: 0, top: 0, TruncateToggleThumbInset, bottom: 0);

    private double TruncateToggleThumbInset =>
        Math.Max(val1: 0, (TruncateToggleTrackHeight - TruncateToggleThumbSize) / 2.0);

    /// <summary>
    /// Rebuilds the selector chrome and active tab body.
    /// </summary>
    private void RebuildContent() => RebuildContent(SelectedTab);

    private void RebuildContent(ProbeSelectorTab selectedTab)
    {
        ApplyFocusedTextEdit();
        UIResourceScope resources = new($"{nameof(ProbeDataSelectorWindow)}.Content");
        ProbeSelectorVisualGeneration replacement = new(
            selectedTab,
            DeviceNicknameResolver.Create(_settings),
            ProbeNicknameResolver.Create(_settings),
            resources);
        resources.Add(replacement.Retire);

        try
        {
            Grid shell = ControlNames.Assign(
                new Grid
                {
                    Background = TrayAppDotNETSettingsUI.Brush(_palette.Background), Margin = Layout.ContentMargin
                },
                nameof(ProbeDataSelectorWindow));
            shell.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.TabRowHeight)));
            shell.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            shell.Children.Add(BuildTabRow(replacement));

            Control body = BuildBodyHost(replacement);
            Grid.SetRow(body, value: 1);
            shell.Children.Add(body);

            Border root = ControlNames.Assign(
                new Border
                {
                    Focusable = true,
                    Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
                    BorderThickness = Layout.ZeroThickness,
                    CornerRadius = Layout.ZeroCornerRadius,
                    Child = shell
                },
                nameof(ProbeDataSelectorWindow));

            // Clicking empty space focuses the root, which commits text edits; it is never a Tab stop
            KeyboardNavigation.SetIsTabStop(root, value: false);
            ControlNames.AssignLogicalSubtree(root, this);
            replacement.FocusSink = root;
            replacement.AttachContentGeneration(new UIContentGeneration(
                $"{nameof(ProbeDataSelectorWindow)}.Content",
                root,
                resources));
        }
        catch
        {
            resources.Dispose();
            throw;
        }

        CommitContentGeneration(replacement);
    }

    /// <summary>
    /// Rebuilds once the current focus change completes. A commit from LostFocus runs while focus is still moving,
    /// and rebuilding then would detach the control that is taking focus.
    /// </summary>
    private void ScheduleRebuild()
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            // A rebuild in the meantime already shows the committed edit
            if (_windowResources.IsDisposed || !ReferenceEquals(generation, _activeVisualGeneration)) return;

            RebuildContent();
        });
    }

    private void CommitContentGeneration(ProbeSelectorVisualGeneration replacement)
    {
        ProbeSelectorVisualGeneration? previous = _activeVisualGeneration;
        object? previousWindowContent = Content;
        _activeVisualGeneration = replacement;
        try
        {
            Content = replacement.ContentGeneration.Root;
        }
        catch (Exception exception)
        {
            _activeVisualGeneration = previous;
            try
            {
                Content = previousWindowContent;
            }
            catch (Exception rollbackException)
            {
                TADNLog.Log(
                    $"ProbeDataSelectorWindow content rollback failed after {exception.GetType().Name}: " +
                    $"{rollbackException.GetType().Name}: {rollbackException.Message}");
            }
            finally
            {
                // The replacement's reorder gestures retire with it
                if (!replacement.ContentGeneration.IsDisposed)
                    replacement.ContentGeneration.Dispose();
            }

            throw;
        }

        // Retire the old generation, and with it any reorder gesture, only after the replacement root is live
        ClearHomeEditState();
        previous?.ContentGeneration.Dispose();

        // Focus moves last, so the reset cannot clear what a restored transform editor tracks on focus
        RestoreFocus(replacement);
    }

    /// <summary>
    /// Builds the tab body host, leaving Home available for section-local scrolling.
    /// </summary>
    private Control BuildBodyHost(ProbeSelectorVisualGeneration generation)
    {
        Control content = BuildTabBody(generation);
        if (generation.SelectedTab == ProbeSelectorTab.Home)
            return new Border { Margin = Layout.BodyMargin, Child = content };

        SettingsScrollHost scrollHost = generation.Resources.Own(
            new SettingsScrollHost(
                content,
                _palette,
                Layout.ZeroThickness,
                ControlMap.ProbeDataSelector.TypePage.ScrollBar.ID) { Margin = Layout.BodyMargin });
        return scrollHost;
    }

    /// <summary>
    /// Builds the single-row browser-style tab strip.
    /// </summary>
    private Grid BuildTabRow(ProbeSelectorVisualGeneration generation)
    {
        Grid row = ControlNames.Assign(new Grid(), parentName: "Tabs").MapTo(ControlMap.ProbeDataSelector.Tabs.ID);
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int i = 1; i < Tabs.Length; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Border homeTab = BuildTab(Tabs[0], generation);
        Grid.SetColumn(homeTab, value: 0);
        row.Children.Add(homeTab);
        Border selectedTab = homeTab;

        for (int i = 1; i < Tabs.Length; i++)
        {
            Border tab = BuildTab(Tabs[i], generation);
            Grid.SetColumn(tab, i + 1);
            row.Children.Add(tab);
            if (Tabs[i] == generation.SelectedTab)
                selectedTab = tab;
        }

        // The strip is one Tab stop; Tab lands on the open tab rather than the first one
        KeyboardNavigation.SetTabOnceActiveElement(row, selectedTab);
        return row;
    }

    /// <summary>
    /// Builds one tab button.
    /// </summary>
    private Border BuildTab(ProbeSelectorTab tab, ProbeSelectorVisualGeneration generation)
    {
        bool selected = generation.SelectedTab == tab;
        TextBlock label = TrayAppDotNETSettingsUI.Text(TabLabel(tab), _palette, Layout.TabFontSize,
            selected ? FontWeight.SemiBold : FontWeight.Normal);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        Border border = ControlNames.Assign(
            new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(
                    selected ? _palette.CardBackground : _palette.ControlBackground),
                BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
                BorderThickness = Layout.RootBorderThickness,
                CornerRadius = _settings.EnableRoundedCorners
                    ? Layout.TabCornerRadius
                    : Layout.ZeroCornerRadius,
                Margin = Layout.TabMargin,
                Padding = Layout.TabPadding,
                Width = Layout.TabWidth,
                MinHeight = Layout.TabMinHeight,
                Child = label,
                Cursor = TrayAppDotNETCursors.Hand
            },
            $"{tab}Tab").MapTo(TabNode(tab));
        border.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;

            SelectTab(tab);
            e.Handled = true;
        };
        border.MapActivation(_ => SelectTab(tab));
        RegisterFocusTarget(generation, border, new ProbeFocusIdentity(tab, ProbeFocusRole.Tab));
        return border;
    }

    /// <summary>
    /// Opens an editor tab from a click on its header or from Enter or Space while the header has focus.
    /// </summary>
    private void SelectTab(ProbeSelectorTab tab)
    {
        if (SelectedTab == tab) return;

        RebuildContent(tab);
    }

    /// <summary>
    /// Resolves the control map leaf of a tab header.
    /// </summary>
    private static ControlMapNodeID TabNode(ProbeSelectorTab tab) => tab switch
    {
        ProbeSelectorTab.Home => ControlMap.ProbeDataSelector.Tabs.Home,
        ProbeSelectorTab.Temperatures => ControlMap.ProbeDataSelector.Tabs.Temperatures,
        ProbeSelectorTab.Power => ControlMap.ProbeDataSelector.Tabs.Power,
        ProbeSelectorTab.Load => ControlMap.ProbeDataSelector.Tabs.Load,
        ProbeSelectorTab.Clocks => ControlMap.ProbeDataSelector.Tabs.Clocks,
        ProbeSelectorTab.Voltages => ControlMap.ProbeDataSelector.Tabs.Voltages,
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, message: null)
    };

    /// <summary>
    /// Remembers which editor control holds focus and whether the keyboard put it there, so a rebuild can hand focus
    /// to the control that replaces it.
    /// </summary>
    private void OnSelectorGotFocus(object? sender, FocusChangedEventArgs e)
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null || e.Source is not Visual source || !IsActiveGenerationVisual(source)) return;

        _focusIdentity = FindFocusIdentity(generation, source);
        _isKeyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
    }

    /// <summary>
    /// Hands focus to the rebuilt control that matches the one focused before the rebuild, or to the first page
    /// control when that one is gone. The focus cue shows only when the keyboard had put focus there.
    /// </summary>
    private void RestoreFocus(ProbeSelectorVisualGeneration generation)
    {
        if (_focusIdentity is not { } identity) return;

        // Scroll hosts attach their content only when their template applies on measure, and detached controls
        // cannot take focus
        UpdateLayout();
        Control? target = FindFocusTarget(generation, identity) ?? FirstPageFocusTarget(generation);
        target?.Focus(_isKeyboardFocus ? NavigationMethod.Tab : NavigationMethod.Unspecified);
    }

    /// <summary>
    /// Finds the available control registered under a focus identity in one visual generation.
    /// </summary>
    private static Control? FindFocusTarget(ProbeSelectorVisualGeneration generation, ProbeFocusIdentity identity)
    {
        foreach (ProbeFocusTarget target in generation.FocusTargets)
        {
            if (target.Identity.Equals(identity) && IsFocusTargetAvailable(target.Control))
                return target.Control;
        }

        return null;
    }

    /// <summary>
    /// Finds the first available page control in build order, skipping the tab strip.
    /// </summary>
    private static Control? FirstPageFocusTarget(ProbeSelectorVisualGeneration generation)
    {
        foreach (ProbeFocusTarget target in generation.FocusTargets)
        {
            if (target.Identity.Role != ProbeFocusRole.Tab && IsFocusTargetAvailable(target.Control))
                return target.Control;
        }

        return null;
    }

    /// <summary>
    /// Returns the identity of the registered control that is or contains a visual, or null when none does.
    /// </summary>
    private static ProbeFocusIdentity? FindFocusIdentity(ProbeSelectorVisualGeneration generation, Visual visual)
    {
        for (Visual? current = visual; current != null; current = current.GetVisualParent())
        {
            foreach (ProbeFocusTarget target in generation.FocusTargets)
            {
                if (ReferenceEquals(target.Control, current))
                    return target.Identity;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a registered control can take focus.
    /// </summary>
    private static bool IsFocusTargetAvailable(Control control) =>
        control is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true };

    /// <summary>
    /// Registers a focusable control under a stable identity, so focus can return to its replacement after a rebuild.
    /// </summary>
    private static void RegisterFocusTarget(
        ProbeSelectorVisualGeneration generation,
        Control control,
        ProbeFocusIdentity identity) =>
        generation.FocusTargets.Add(new ProbeFocusTarget(identity, control));

    /// <summary>
    /// Builds the active tab body.
    /// </summary>
    private Control BuildTabBody(ProbeSelectorVisualGeneration generation)
    {
        return generation.SelectedTab switch
        {
            ProbeSelectorTab.Home => BuildHomeBody(generation),
            ProbeSelectorTab.Temperatures => BuildTypeBody(DataSourceTypeEnum.Temperature, generation),
            ProbeSelectorTab.Power => BuildTypeBody(DataSourceTypeEnum.Power, generation),
            ProbeSelectorTab.Load => BuildTypeBody(DataSourceTypeEnum.Load, generation),
            ProbeSelectorTab.Clocks => BuildTypeBody(DataSourceTypeEnum.Clock, generation),
            ProbeSelectorTab.Voltages => BuildTypeBody(DataSourceTypeEnum.Voltage, generation),
            _ => BuildHomeBody(generation)
        };
    }

    /// <summary>
    /// Builds the selected-probes home tab.
    /// </summary>
    private Grid BuildHomeBody(ProbeSelectorVisualGeneration generation)
    {
        Grid home = ControlNames.Assign(
            new Grid { UseLayoutRounding = true },
            parentName: "Home").MapTo(ControlMap.ProbeDataSelector.HomePage.ID);
        home.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        home.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        home.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        home.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.HomeSectionSeparatorThickness)));
        home.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(NicknameColumnWidth())));

        Grid profileVisibility = BuildProfileVisibilityRow(generation);
        Grid.SetColumnSpan(profileVisibility, value: 3);
        home.Children.Add(profileVisibility);

        Border selectedProbeHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding, Child = BuildSelectedProbesSection(generation)
        };
        Grid.SetRow(selectedProbeHost, value: 1);
        Grid.SetColumn(selectedProbeHost, value: 0);
        home.Children.Add(selectedProbeHost);

        Border columnSeparator = BuildHomeColumnSeparator();
        Grid.SetRow(columnSeparator, value: 1);
        Grid.SetColumn(columnSeparator, value: 1);
        home.Children.Add(columnSeparator);

        Grid nicknames = new() { UseLayoutRounding = true };
        nicknames.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.HomeDeviceNicknamesRowHeight)));
        nicknames.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.HomeSectionSeparatorThickness)));
        nicknames.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Border deviceNicknameHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding, Child = BuildDeviceNicknamesSection(generation)
        };
        Grid.SetRow(deviceNicknameHost, value: 0);
        nicknames.Children.Add(deviceNicknameHost);

        Border rowSeparator = BuildHomeNicknameRowSeparator();
        Grid.SetRow(rowSeparator, value: 1);
        nicknames.Children.Add(rowSeparator);

        Border probeNicknameHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding, Child = BuildProbeNicknamesSection(generation)
        };
        Grid.SetRow(probeNicknameHost, value: 2);
        nicknames.Children.Add(probeNicknameHost);

        Grid.SetRow(nicknames, value: 1);
        Grid.SetColumn(nicknames, value: 2);
        home.Children.Add(nicknames);
        return home;
    }

    /// <summary>Builds one profile-visibility row above the selected probes and nickname editors.</summary>
    private Grid BuildProfileVisibilityRow(ProbeSelectorVisualGeneration generation)
    {
        Grid row = ControlNames.Assign(
            new Grid { Margin = Layout.ProfileVisibilityRowMargin },
            parentName: "ProfileVisibility");
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        TextBlock label = TrayAppDotNETSettingsUI.Text(
            L(nameof(AppStrings.ProbeSelector_ProfileVisibility_Label)), _palette, Layout.SectionTitleFontSize);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        for (int profileIndex = 0; profileIndex < FanProfile.SlotCount; profileIndex++)
        {
            int selectedProfileIndex = profileIndex;
            FanProfile profile = _settings.FanProfiles[profileIndex];
            string profileName = profile.DisplayName(profileIndex);
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
            TextBlock profileLabel = TrayAppDotNETSettingsUI.Text(
                profileName, _palette, Layout.SectionTitleFontSize);
            profileLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            CheckBox checkBox = ControlNames.Assign(
                new CheckBox
                {
                    Content = profileLabel,
                    IsChecked = _probeCard.IsVisibleOnProfile(profileIndex),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground)
                },
                parentName: "ProfileVisibility").MapTo(ControlMap.ProbeDataSelector.HomePage.ProfileVisibility);
            TrayAppDotNETToolTip.SetTip(
                checkBox,
                string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(AppStrings.ProbeSelector_LastProfile_Tooltip_Format)),
                    profileName));
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (_windowResources.IsDisposed || !ReferenceEquals(generation, _activeVisualGeneration)) return;
                bool wasVisible = _probeCard.IsVisibleOnProfile(selectedProfileIndex);
                bool visible = checkBox.IsChecked == true;
                if (wasVisible == visible) return;
                if (!_probeCard.TrySetProfileVisibility(selectedProfileIndex, visible))
                {
                    checkBox.IsChecked = wasVisible;
                    return;
                }

                _changed(_probeCard);
            };

            RegisterFocusTarget(
                generation,
                checkBox,
                new ProbeFocusIdentity(profile, ProbeFocusRole.ProfileVisibility));

            Border host = new() { Margin = Layout.ProfileVisibilityCheckBoxMargin, Child = checkBox };
            Grid.SetColumn(host, profileIndex + 1);
            row.Children.Add(host);
        }

        return row;
    }

    /// <summary>
    /// Builds the full-height separator between home-tab columns.
    /// </summary>
    private Border BuildHomeColumnSeparator() =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(Layout.HomeSectionSeparatorColor),
            Margin = Layout.HomeColumnSeparatorMargin,
            Width = Layout.HomeSectionSeparatorThickness,
            MinWidth = Layout.HomeSectionSeparatorThickness,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            UseLayoutRounding = true
        };

    /// <summary>
    /// Builds the full-width separator between nickname sections.
    /// </summary>
    private Border BuildHomeNicknameRowSeparator() =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(Layout.HomeSectionSeparatorColor),
            Margin = Layout.HomeNicknameRowSeparatorMargin,
            Height = Layout.HomeSectionSeparatorThickness,
            MinHeight = Layout.HomeSectionSeparatorThickness,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            UseLayoutRounding = true
        };

    /// <summary>
    /// Builds the selected-probes column for the home tab.
    /// </summary>
    private Grid BuildSelectedProbesSection(ProbeSelectorVisualGeneration generation)
    {
        Grid section = ControlNames.Assign(
            new Grid { Margin = Layout.NicknameSectionMargin },
            parentName: "SelectedProbes");
        section.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        section.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        section.Children.Add(BuildSelectedProbesHeader(generation));

        StackPanel selectedProbeList = ControlNames.Assign(
            new StackPanel { Margin = Layout.SelectedProbeGridMargin },
            parentName: "SelectedProbes");
        List<ProbeCardProbe> selectedProbes =
        [
            .. _probeCard.Probes
                .Where(static probe => probe.IsSelected && !string.IsNullOrWhiteSpace(probe.DataSourceKey))
        ];
        generation.SelectedProbeListPanel = selectedProbeList;
        VerticalReorderGesture reorder = generation.Resources.Own(new VerticalReorderGesture(
            new VerticalReorderGestureOptions
            {
                List = selectedProbeList,
                Threshold = Layout.ReorderDragThreshold,
                ActiveRowChanged = (row, active) =>
                    UpdateReorderRowVisual((Border)row, active, _palette.SearchListItemSelected),
                Commit = (sourceIndex, targetIndex) =>
                {
                    ApplySelectedProbeOrder(selectedProbes[sourceIndex], targetIndex);
                    RebuildContent();
                }
            }));
        foreach (ProbeCardProbe probe in selectedProbes)
        {
            DataSource? source = DataSource.Find(probe.DataSourceKey);
            Border card = source is null
                ? BuildMissingProbeCard(probe, generation)
                : BuildProbeChoiceCard(source, generation);
            // Each row carries the probe it stands for
            card.Tag = probe;
            WireSelectedProbeDrag(card, reorder);
            card.MapTo(ControlMap.ProbeDataSelector.HomePage.SelectedProbes.SelectedProbe.ID)
                .MapCommand(
                    ControlMap.ProbeDataSelector.HomePage.SelectedProbes.SelectedProbe.MoveUp,
                    () => MoveSelectedProbeByKeyboard(probe, UpDirection))
                .MapCommand(
                    ControlMap.ProbeDataSelector.HomePage.SelectedProbes.SelectedProbe.MoveDown,
                    () => MoveSelectedProbeByKeyboard(probe, DownDirection));
            selectedProbeList.Children.Add(card);
        }

        Control content = selectedProbes.Count == 0
            ? EmptyText(L(nameof(AppStrings.Flyout_NoProbesSelected)))
            : selectedProbeList;
        SettingsScrollHost scrollHost = BuildVerticalScrollHost(
            content,
            Layout.HomeSectionScrollHostMargin,
            ControlMap.ProbeDataSelector.HomePage.SelectedProbes.ScrollBar.ID,
            generation);
        Grid.SetRow(scrollHost, value: 1);
        section.Children.Add(scrollHost);
        return section;
    }

    /// <summary>
    /// Builds the selected-probes column header.
    /// </summary>
    private Grid BuildSelectedProbesHeader(ProbeSelectorVisualGeneration generation)
    {
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text(L(nameof(AppStrings.ProbeSelector_SelectedProbes_Header)), _palette,
            Layout.SectionTitleFontSize, FontWeight.SemiBold);
        title.Margin = Layout.SectionHeaderMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        SettingsButton clearDeadSensors = TrayAppDotNETSettingsUI.Button(
                L(nameof(AppStrings.ProbeSelector_ClearDeadSensors_Button)), _palette)
            .MapTo(ControlMap.ProbeDataSelector.HomePage.SelectedProbes.ClearDeadSensors);
        clearDeadSensors.Width = Layout.HomeClearDeadSensorsButtonWidth;
        clearDeadSensors.Height = Layout.HomeActionButtonHeight;
        clearDeadSensors.MinHeight = Layout.HomeActionButtonHeight;
        clearDeadSensors.Padding = Layout.HomeActionButtonPadding;
        clearDeadSensors.Margin = Layout.HomeActionButtonTrailingMargin;
        clearDeadSensors.Click += (_, _) => ClearDeadSensors();
        RegisterFocusTarget(
            generation,
            clearDeadSensors,
            new ProbeFocusIdentity(ProbeFocusEntity.ClearDeadSensors, ProbeFocusRole.Action));
        Grid.SetColumn(clearDeadSensors, value: 1);
        header.Children.Add(clearDeadSensors);
        return header;
    }

    /// <summary>
    /// Wires drag reordering for a selected-probe row; Ctrl+Up and Ctrl+Down are its control map commands. A
    /// double-click stays with the card's own toggle.
    /// </summary>
    private void WireSelectedProbeDrag(Border row, VerticalReorderGesture reorder)
    {
        row.Cursor = TrayAppDotNETCursors.Hand;
        UpdateReorderRowVisual(row, active: false, _palette.SearchListItemSelected);
        row.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2) return;
            reorder.TryBegin(row, e);
        };
    }

    /// <summary>
    /// Shows a reorder row as pressed and lifted while its gesture holds the pointer.
    /// </summary>
    private void UpdateReorderRowVisual(Border row, bool active, Color restingBackground)
    {
        row.Background = TrayAppDotNETSettingsUI.Brush(active ? _palette.Pressed : restingBackground);
        row.BorderBrush = TrayAppDotNETSettingsUI.Brush(active ? _palette.Accent : _palette.Border);
        row.BorderThickness = Layout.RootBorderThickness;
        row.Opacity = active ? Layout.ReorderDraggingOpacity : Layout.FullOpacity;
        row.SetValue(ZIndexProperty, active ? Layout.ReorderDraggingZIndex : Layout.ReorderNormalZIndex);
    }

    /// <summary>
    /// Resolves a selected probe's active-list index.
    /// </summary>
    private int SelectedProbeIndex(ProbeCardProbe probe) =>
        ActiveProbeSettingsInOrder().IndexOf(probe);

    /// <summary>
    /// Moves a selected probe with keyboard shortcuts.
    /// </summary>
    private void MoveSelectedProbeByKeyboard(ProbeCardProbe probe, int direction)
    {
        int currentIndex = SelectedProbeIndex(probe);
        int nextIndex = currentIndex + direction;
        int count = ActiveProbeSettingsInOrder().Count;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= count) return;

        ApplySelectedProbeOrder(probe, nextIndex);
        RebuildContent();
    }

    /// <summary>
    /// Applies active selected-probe order while preserving inactive probe settings.
    /// </summary>
    private void ApplySelectedProbeOrder(ProbeCardProbe dragged, int targetIndex)
    {
        List<ProbeCardProbe> activeProbes = ActiveProbeSettingsInOrder();
        int currentIndex = activeProbes.IndexOf(dragged);
        if (currentIndex < 0) return;

        int clampedTargetIndex = Math.Clamp(targetIndex, min: 0, activeProbes.Count - 1);
        if (currentIndex == clampedTargetIndex) return;

        activeProbes.RemoveAt(currentIndex);
        activeProbes.Insert(clampedTargetIndex, dragged);

        List<ProbeCardProbe> inactiveProbes =
        [
            .. _probeCard.Probes.Where(static probe => !probe.IsSelected)
        ];
        _probeCard.Probes.Clear();
        _probeCard.Probes.AddRange(activeProbes);
        _probeCard.Probes.AddRange(inactiveProbes);
        _changed(_probeCard);
    }

    /// <summary>
    /// Returns selected active probe settings in persisted order.
    /// </summary>
    private List<ProbeCardProbe> ActiveProbeSettingsInOrder() =>
    [
        .. _probeCard.Probes
            .Where(static probe => probe.IsSelected && !string.IsNullOrWhiteSpace(probe.DataSourceKey))
    ];

    /// <summary>
    /// Builds the global device nickname editor section.
    /// </summary>
    private Grid BuildDeviceNicknamesSection(ProbeSelectorVisualGeneration generation)
    {
        return BuildNicknameSection(
            controlName: "DeviceNicknames",
            titleText: L(nameof(AppStrings.ProbeSelector_DeviceNicknames_Header)),
            _settings.DeviceNicknameRules,
            LoadDefaultDeviceNicknames,
            AddDeviceNicknameRule,
            DeleteDeviceNicknameRule,
            ProbeFocusEntity.LoadDefaultDeviceNicknames,
            ProbeFocusEntity.AddDeviceNickname,
            generation).MapTo(ControlMap.ProbeDataSelector.HomePage.DeviceNicknames.ID);
    }

    /// <summary>
    /// Builds the global probe nickname editor section.
    /// </summary>
    private Grid BuildProbeNicknamesSection(ProbeSelectorVisualGeneration generation)
    {
        return BuildNicknameSection(
            controlName: "ProbeNicknames",
            titleText: L(nameof(AppStrings.ProbeSelector_ProbeNicknames_Header)),
            _settings.ProbeNicknameRules,
            LoadDefaultProbeNicknames,
            AddProbeNicknameRule,
            DeleteProbeNicknameRule,
            ProbeFocusEntity.LoadDefaultProbeNicknames,
            ProbeFocusEntity.AddProbeNickname,
            generation).MapTo(ControlMap.ProbeDataSelector.HomePage.ProbeNicknames.ID);
    }

    /// <summary>
    /// Builds a global nickname editor section.
    /// </summary>
    private Grid BuildNicknameSection(
        string controlName,
        string titleText,
        List<DeviceNicknameRule> rulesList,
        Action loadDefaultRules,
        Action addRule,
        Action<DeviceNicknameRule> deleteRule,
        ProbeFocusEntity loadDefaultsEntity,
        ProbeFocusEntity addEntity,
        ProbeSelectorVisualGeneration generation)
    {
        Grid section = ControlNames.Assign(
            new Grid { Margin = Layout.NicknameSectionMargin },
            controlName);
        section.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        section.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text(titleText, _palette,
            Layout.SectionTitleFontSize, FontWeight.SemiBold);
        title.Margin = Layout.SectionHeaderMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        SettingsButton loadDefaultButton = ControlNames.Assign(
            TrayAppDotNETSettingsUI.Button(L(nameof(AppStrings.ProbeSelector_LoadDefaultNicknames_Button)), _palette),
            controlName).MapTo(ControlMap.NicknameSection.LoadDefaults);
        loadDefaultButton.Width = Layout.HomeLoadDefaultNicknamesButtonWidth;
        loadDefaultButton.Height = Layout.HomeActionButtonHeight;
        loadDefaultButton.MinHeight = Layout.HomeActionButtonHeight;
        loadDefaultButton.Padding = Layout.HomeActionButtonPadding;
        loadDefaultButton.Margin = Layout.HomeActionButtonMargin;
        loadDefaultButton.Click += (_, _) => loadDefaultRules();
        RegisterFocusTarget(
            generation,
            loadDefaultButton,
            new ProbeFocusIdentity(loadDefaultsEntity, ProbeFocusRole.Action));
        Grid.SetColumn(loadDefaultButton, value: 1);
        header.Children.Add(loadDefaultButton);

        SettingsButton addButton = ControlNames.Assign(
            TrayAppDotNETSettingsUI.Button(L(nameof(AppStrings.ProbeSelector_AddRule_Button)), _palette),
            controlName).MapTo(ControlMap.NicknameSection.AddRule);
        addButton.Width = Layout.NicknameAddButtonWidth;
        addButton.Height = Layout.NicknameAddButtonHeight;
        addButton.MinHeight = Layout.NicknameAddButtonHeight;
        addButton.Padding = Layout.NicknameAddButtonPadding;
        addButton.Margin = Layout.HomeActionButtonTrailingMargin;
        addButton.Click += (_, _) => addRule();
        RegisterFocusTarget(generation, addButton, new ProbeFocusIdentity(addEntity, ProbeFocusRole.Action));
        Grid.SetColumn(addButton, value: 2);
        header.Children.Add(addButton);
        section.Children.Add(header);

        StackPanel rules = ControlNames.Assign(new StackPanel(), controlName);
        VerticalReorderGesture reorder = generation.Resources.Own(new VerticalReorderGesture(
            new VerticalReorderGestureOptions
            {
                List = rules,
                Threshold = Layout.ReorderDragThreshold,
                ActiveRowChanged = (row, active) =>
                    UpdateReorderRowVisual((Border)row, active, _palette.CardBackground),
                Commit = (sourceIndex, targetIndex) =>
                {
                    ApplyNicknameRuleOrder(rulesList, rulesList[sourceIndex], targetIndex);
                    RebuildContent();
                }
            }));
        foreach (DeviceNicknameRule rule in rulesList)
        {
            rules.Children.Add(BuildNicknameRuleCard(
                rule,
                rulesList,
                reorder,
                deleteRule,
                generation));
        }

        Border rulesHost = new()
        {
            Margin = Layout.NicknameListMargin, Padding = Layout.NicknameListPadding, Child = rules
        };
        SettingsScrollHost scrollHost = BuildVerticalScrollHost(
            rulesHost,
            Layout.HomeNicknameScrollHostMargin,
            ControlMap.NicknameSection.ScrollBar.ID,
            generation);
        Grid.SetRow(scrollHost, value: 1);
        section.Children.Add(scrollHost);
        return section;
    }

    /// <summary>
    /// Builds a custom vertical-only scrollbar host for a bounded section; the scroll bar takes its map instance.
    /// </summary>
    private SettingsScrollHost BuildVerticalScrollHost(
        Control content,
        Thickness margin,
        ControlMapNodeID scrollBarNode,
        ProbeSelectorVisualGeneration generation)
    {
        SettingsScrollHost scrollHost = generation.Resources.Own(
            new SettingsScrollHost(content, _palette, Layout.ZeroThickness, scrollBarNode) { Margin = margin });
        return ControlNames.Assign(scrollHost, parentName: "VerticalScroll");
    }

    /// <summary>
    /// Calculates the fixed home nickname column width from card width and padding.
    /// </summary>
    private double NicknameColumnWidth() =>
        Layout.NicknameCardWidth + Layout.HomeNicknameColumnPadding.Left + Layout.HomeNicknameColumnPadding.Right;

    /// <summary>
    /// Builds one global nickname replacement rule card.
    /// </summary>
    private Border BuildNicknameRuleCard(
        DeviceNicknameRule rule,
        List<DeviceNicknameRule> rulesList,
        VerticalReorderGesture reorder,
        Action<DeviceNicknameRule> deleteRule,
        ProbeSelectorVisualGeneration generation)
    {
        Grid row = ControlNames.Assign(
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            },
            parentName: "NicknameRule");

        TextBox target = NicknameTextBox(rule.TargetRegex, L(nameof(AppStrings.ProbeSelector_Target_Placeholder)),
            Layout.NicknameTargetTextBoxWidth);
        ControlNames.Assign(target, NicknameTargetControlAnchor).MapTo(ControlMap.NicknameSection.Rule.Target);
        target.Tag = new NicknameRuleEditorTag(rule, NicknameRuleField.Target);
        target.LostFocus += NicknameRuleLostFocus;
        target.KeyDown += NicknameRuleKeyDown;
        RegisterFocusTarget(generation, target, new ProbeFocusIdentity(rule, ProbeFocusRole.NicknameTargetEditor));
        row.Children.Add(target);

        TextBlock arrow = TrayAppDotNETSettingsUI.Text(
            GlyphCatalog.ARROW_RIGHT.Text,
            _palette,
            Layout.NicknameArrowFontSize).MapTo(ControlMap.NicknameSection.Rule.Reorder);
        GlyphApplicator.ApplyTo(arrow, GlyphCatalog.ARROW_RIGHT);
        arrow.Margin = Layout.NicknameArrowMargin;
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Cursor = TrayAppDotNETCursors.Hand;
        Grid.SetColumn(arrow, value: 1);
        row.Children.Add(arrow);

        TextBox replacement = NicknameTextBox(rule.ReplacementString, L(nameof(AppStrings.ProbeSelector_Replacement_Placeholder)),
            Layout.NicknameReplacementTextBoxWidth);
        ControlNames.Assign(replacement, NicknameReplacementControlAnchor)
            .MapTo(ControlMap.NicknameSection.Rule.Replacement);
        replacement.Tag = new NicknameRuleEditorTag(rule, NicknameRuleField.Replacement);
        replacement.LostFocus += NicknameRuleLostFocus;
        replacement.KeyDown += NicknameRuleKeyDown;
        RegisterFocusTarget(
            generation,
            replacement,
            new ProbeFocusIdentity(rule, ProbeFocusRole.NicknameReplacementEditor));
        Grid.SetColumn(replacement, value: 2);
        row.Children.Add(replacement);

        SettingsButton delete = BuildNicknameDeleteButton().MapTo(ControlMap.NicknameSection.Rule.DeleteRule);
        delete.Click += (_, _) => deleteRule(rule);
        RegisterFocusTarget(generation, delete, new ProbeFocusIdentity(rule, ProbeFocusRole.Delete));
        Grid.SetColumn(delete, value: 3);
        row.Children.Add(delete);

        Border card = ControlNames.Assign(WrapNicknameCard(row), parentName: "NicknameRule")
            .MapTo(ControlMap.NicknameSection.Rule.ID)
            .MapCommand(
                ControlMap.NicknameSection.Rule.MoveUp,
                () => MoveNicknameRuleByKeyboard(rule, rulesList, UpDirection))
            .MapCommand(
                ControlMap.NicknameSection.Rule.MoveDown,
                () => MoveNicknameRuleByKeyboard(rule, rulesList, DownDirection));
        card.Tag = rule;
        WireNicknameRuleDrag(card, arrow, reorder);
        return card;
    }

    /// <summary>
    /// Wires drag reordering for a nickname rule row from its arrow handle; Ctrl+Up and Ctrl+Down are its control
    /// map commands.
    /// </summary>
    private void WireNicknameRuleDrag(Border row, TextBlock dragHandle, VerticalReorderGesture reorder)
    {
        UpdateReorderRowVisual(row, active: false, _palette.CardBackground);
        dragHandle.PointerPressed += (_, e) => reorder.TryBegin(row, e);
    }

    /// <summary>
    /// Toggles a probe when its card chrome or descriptive text is double-clicked.
    /// </summary>
    private static void WireProbeCardDoubleClick(Border card, Action toggleProbe)
    {
        card.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (!IsProbeCardActivationSource(card, e.Source as Visual)) return;

            toggleProbe();
            e.Handled = true;
        };
    }

    /// <summary>
    /// Excludes embedded buttons, toggles, and editors from card double-click activation.
    /// </summary>
    private static bool IsProbeCardActivationSource(Border card, Visual? source)
    {
        Visual? current = source;
        while (current != null && !ReferenceEquals(current, card))
        {
            if (current is SettingsButton or SettingsMiniToggle or TextBox) return false;
            current = current.GetVisualParent();
        }

        return ReferenceEquals(current, card);
    }

    /// <summary>Forgets the transform editor of a retired Home tab during visual generation replacement.</summary>
    private void ClearHomeEditState() => _focusedTransformTextBox = null;

    /// <summary>
    /// Moves a nickname rule with keyboard shortcuts.
    /// </summary>
    private void MoveNicknameRuleByKeyboard(
        DeviceNicknameRule rule,
        List<DeviceNicknameRule> rulesList,
        int direction)
    {
        int currentIndex = rulesList.IndexOf(rule);
        int nextIndex = currentIndex + direction;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= rulesList.Count) return;

        rulesList.RemoveAt(currentIndex);
        rulesList.Insert(nextIndex, rule);
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Applies nickname rule order to preserve replacement precedence.
    /// </summary>
    private void ApplyNicknameRuleOrder(
        List<DeviceNicknameRule> rulesList,
        DeviceNicknameRule dragged,
        int targetIndex)
    {
        int currentIndex = rulesList.IndexOf(dragged);
        if (currentIndex < 0) return;

        int clampedTargetIndex = Math.Clamp(targetIndex, min: 0, rulesList.Count - 1);
        if (currentIndex == clampedTargetIndex) return;

        rulesList.RemoveAt(currentIndex);
        rulesList.Insert(clampedTargetIndex, dragged);
        _changed(_probeCard);
    }

    /// <summary>
    /// Builds a typed probe grid tab.
    /// </summary>
    private Control BuildTypeBody(
        DataSourceTypeEnum type,
        ProbeSelectorVisualGeneration generation)
    {
        WrapPanel grid = ControlNames.Assign(new WrapPanel(), $"{type}Probes")
            .MapTo(ControlMap.ProbeDataSelector.TypePage.ID);
        List<DataSource> sources =
        [
            .. DataSource.DataSources.Values
                .Where(source => source.DataSourceType == type && ProbeValueFormatter.IsProbeDataSource(source))
                .OrderBy(
                    source => generation.DeviceNicknameResolver.Resolve(source),
                    NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    source => generation.ProbeNicknameResolver.Resolve(source.DisplayName),
                    NaturalStringComparer.OrdinalIgnoreCase)
        ];
        if (sources.Count == 0)
            return EmptyText(L(nameof(AppStrings.ProbeSelector_NoProbesFound)));

        foreach (DataSource source in sources)
        {
            Border card = BuildProbeChoiceCard(source, generation)
                .MapTo(ControlMap.ProbeDataSelector.TypePage.Probe.ID);
            grid.Children.Add(card);
        }

        return grid;
    }

    /// <summary>
    /// Builds a card for a selectable live data source.
    /// </summary>
    private Border BuildProbeChoiceCard(
        DataSource source,
        ProbeSelectorVisualGeneration generation)
    {
        ProbeCardProbe? probeSettings = _probeCard.FindProbe(source.DataSourceKey);
        bool isSelected = probeSettings?.IsSelected == true;
        bool isExpanded = probeSettings is not null && _expandedTransformKeys.Contains(source.DataSourceKey);

        Grid card = ControlNames.Assign(new Grid(), parentName: "ProbeChoice");
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Border choiceCard = ControlNames.Assign(WrapCard(card, isSelected), parentName: "ProbeChoice");
        WireProbeCardDoubleClick(choiceCard, () => ToggleProbe(source, !isSelected));

        TextBlock deviceName = TrayAppDotNETSettingsUI.Text(generation.DeviceNicknameResolver.Resolve(source),
            _palette, Layout.CardTitleFontSize, FontWeight.SemiBold);
        deviceName.TextTrimming = TextTrimming.CharacterEllipsis;
        deviceName.Margin = Layout.TextColumnMargin;
        deviceName.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(deviceName);

        TextBlock probeValue = TrayAppDotNETSettingsUI.Text(
            ProbeValueLine(source, probeSettings, generation.ProbeNicknameResolver),
            _palette, Layout.CardValueFontSize);
        probeValue.TextTrimming = TextTrimming.CharacterEllipsis;
        probeValue.Margin = Layout.ValueRowMargin;
        probeValue.VerticalAlignment = VerticalAlignment.Center;
        RegisterValueText(generation, source.DataSourceKey, probeValue);
        Grid valueRow = BuildProbeValueRow(source.DataSourceType, probeValue);
        Grid.SetRow(valueRow, value: 1);
        card.Children.Add(valueRow);

        SettingsMiniToggle enableToggle = BuildProbeEnableToggle(source, isSelected);
        SettingsMiniToggle truncateToggle = BuildProbeTruncateToggle(source, probeSettings);

        SettingsButton gear = ControlNames.Assign(
            BuildGearButton(isExpanded || ProbeTransformIsActive(probeSettings)),
            parentName: "ProbeTransform").MapTo(ControlMap.ProbeChoiceCard.ToggleTransform);
        gear.Margin = Layout.ActionButtonMargin;
        gear.Click += (_, _) => ToggleTransform(source);
        RegisterFocusTarget(
            generation,
            gear,
            new ProbeFocusIdentity(source.DataSourceKey, ProbeFocusRole.TransformButton));
        RegisterFocusTarget(
            generation,
            enableToggle,
            new ProbeFocusIdentity(source.DataSourceKey, ProbeFocusRole.Enable));
        RegisterFocusTarget(
            generation,
            truncateToggle,
            new ProbeFocusIdentity(source.DataSourceKey, ProbeFocusRole.Truncate));

        AddProbeControls(card, enableToggle, truncateToggle, gear, probeSettings, isExpanded, generation);
        return choiceCard;
    }

    /// <summary>
    /// Builds a home-tab card for a selected source that is not currently live.
    /// </summary>
    private Border BuildMissingProbeCard(
        ProbeCardProbe probe,
        ProbeSelectorVisualGeneration generation)
    {
        Grid card = ControlNames.Assign(new Grid(), parentName: "MissingProbe");
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Border missingCard = ControlNames.Assign(WrapCard(card, isSelected: true), parentName: "MissingProbe");

        TextBlock title = TrayAppDotNETSettingsUI.Text(probe.DataSourceKey, _palette, Layout.CardTitleFontSize,
            FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = Layout.TextColumnMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(title);

        TextBlock value = TrayAppDotNETSettingsUI.Text(text: "--", _palette, Layout.CardValueFontSize);
        value.Margin = Layout.ValueRowMargin;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid valueRow = BuildProbeValueRow(DataSourceTypeEnum.Unknown, value);
        Grid.SetRow(valueRow, value: 1);
        card.Children.Add(valueRow);

        SettingsMiniToggle enableToggle = BuildMissingProbeEnableToggle(probe);
        SettingsMiniToggle truncateToggle = BuildMissingProbeTruncateToggle(probe);
        RegisterFocusTarget(
            generation,
            enableToggle,
            new ProbeFocusIdentity(probe.DataSourceKey, ProbeFocusRole.Enable));
        RegisterFocusTarget(
            generation,
            truncateToggle,
            new ProbeFocusIdentity(probe.DataSourceKey, ProbeFocusRole.Truncate));

        AddProbeControls(card, enableToggle, truncateToggle, gear: null, selectedProbe: null, isExpanded: false,
            generation);
        WireProbeCardDoubleClick(missingCard, () => enableToggle.IsChecked = false);
        return missingCard;
    }

    /// <summary>
    /// Builds a probe value row.
    /// </summary>
    private Grid BuildProbeValueRow(DataSourceTypeEnum type, TextBlock value)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Glyph probeGlyph = ProbeValueFormatter.GlyphFor(type);
        TextBlock glyph = TrayAppDotNETSettingsUI.Text(
            probeGlyph.Text,
            _palette,
            Layout.ValueGlyphFontSize);
        GlyphApplicator.ApplyTo(glyph, probeGlyph);
        glyph.Width = Layout.ValueGlyphWidth;
        glyph.Margin = Layout.ValueGlyphMargin;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyph);

        Grid.SetColumn(value, value: 1);
        row.Children.Add(value);
        return row;
    }

    /// <summary>
    /// Adds independent transform and toggle control columns.
    /// </summary>
    private void AddProbeControls(
        Grid card,
        Control enableToggle,
        Control truncateToggle,
        SettingsButton? gear,
        ProbeCardProbe? selectedProbe,
        bool isExpanded,
        ProbeSelectorVisualGeneration generation)
    {
        Control? transformColumn = BuildProbeTransformColumn(
            gear,
            selectedProbe,
            isExpanded,
            generation);
        if (transformColumn is not null)
        {
            Grid.SetColumn(transformColumn, value: 1);
            Grid.SetRowSpan(transformColumn, value: 2);
            card.Children.Add(transformColumn);
        }

        Grid toggleColumn = BuildProbeToggleColumn(enableToggle, truncateToggle);
        Grid.SetColumn(toggleColumn, value: 2);
        Grid.SetRowSpan(toggleColumn, value: 2);
        card.Children.Add(toggleColumn);
    }

    /// <summary>
    /// Builds the transform controls column.
    /// </summary>
    private Grid? BuildProbeTransformColumn(
        SettingsButton? gear,
        ProbeCardProbe? selectedProbe,
        bool isExpanded,
        ProbeSelectorVisualGeneration generation)
    {
        if (gear is null) return null;

        Grid row = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.ProbeControlRowMargin
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        if (!isExpanded || selectedProbe is null)
        {
            row.Children.Add(gear);
            return row;
        }

        Grid transformControls = BuildTransformControls(selectedProbe, generation);
        row.Children.Add(transformControls);

        Grid.SetColumn(gear, value: 1);
        row.Children.Add(gear);
        return row;
    }

    /// <summary>
    /// Builds the mini-toggle control column.
    /// </summary>
    private Grid BuildProbeToggleColumn(
        Control enableToggle,
        Control truncateToggle)
    {
        Grid controls = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.ProbeToggleColumnMargin
        };
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid enableRow = BuildProbeToggleRow(enableToggle);
        controls.Children.Add(enableRow);

        Grid truncateRow = BuildProbeToggleRow(truncateToggle);
        Grid.SetRow(truncateRow, value: 1);
        controls.Children.Add(truncateRow);
        return controls;
    }

    /// <summary>
    /// Builds one mini-toggle row.
    /// </summary>
    private static Grid BuildProbeToggleRow(Control toggle)
    {
        Grid row = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>
    /// Builds the per-probe enable toggle for a live selector card.
    /// </summary>
    private SettingsMiniToggle BuildProbeEnableToggle(DataSource source, bool isSelected)
    {
        return BuildLabeledMiniToggle(
            labelText: L(nameof(AppStrings.ProbeSelector_Enable_Label)),
            isSelected,
            isEnabled: true,
            enabled => ToggleProbe(source, enabled)).MapTo(ControlMap.ProbeChoiceCard.Enabled);
    }

    /// <summary>
    /// Builds the per-probe enable toggle for a missing selector card.
    /// </summary>
    private SettingsMiniToggle BuildMissingProbeEnableToggle(ProbeCardProbe probe)
    {
        return BuildLabeledMiniToggle(
            labelText: L(nameof(AppStrings.ProbeSelector_Enable_Label)),
            isChecked: true,
            isEnabled: true,
            enabled =>
            {
                if (enabled) return;
                _probeCard.Probes.Remove(probe);
                _expandedTransformKeys.Remove(probe.DataSourceKey);
                _changed(_probeCard);
                RebuildContent();
            }).MapTo(ControlMap.ProbeChoiceCard.Enabled);
    }

    /// <summary>
    /// Builds the per-probe truncate toggle for a live selector card.
    /// </summary>
    private SettingsMiniToggle BuildProbeTruncateToggle(DataSource source, ProbeCardProbe? probe)
    {
        SettingsMiniToggle toggle = BuildLabeledMiniToggle(
            labelText: L(nameof(AppStrings.ProbeSelector_Truncate_Label)),
            probe?.TruncateValue == true,
            isEnabled: true,
            truncateValue => SetProbeTruncateValue(source, truncateValue));
        return toggle.MapTo(ControlMap.ProbeChoiceCard.TruncateValue);
    }

    /// <summary>
    /// Builds the per-probe truncate toggle for a missing selector card.
    /// </summary>
    private SettingsMiniToggle BuildMissingProbeTruncateToggle(ProbeCardProbe probe)
    {
        return BuildLabeledMiniToggle(
            labelText: L(nameof(AppStrings.ProbeSelector_Truncate_Label)),
            probe.TruncateValue,
            isEnabled: true,
            truncateValue =>
            {
                if (probe.TruncateValue == truncateValue) return;
                probe.TruncateValue = truncateValue;
                _changed(_probeCard);
            }).MapTo(ControlMap.ProbeChoiceCard.TruncateValue);
    }

    /// <summary>
    /// Builds a labeled mini toggle.
    /// </summary>
    private SettingsMiniToggle BuildLabeledMiniToggle(string labelText, bool isChecked, bool isEnabled,
        Action<bool> changed)
    {
        SettingsMiniToggle toggle = ControlNames.Assign(
            new SettingsMiniToggle(_palette, BuildTruncateToggleLayout(), labelText)
            {
                IsChecked = isChecked, IsEnabled = isEnabled
            },
            $"Probe{labelText}");
        toggle.CheckedChanged += (_, enabled) => changed(enabled);
        return toggle;
    }

    private SettingsMiniToggleLayout BuildTruncateToggleLayout() =>
        new()
        {
            Width = Layout.TruncateToggleWidth,
            TrackWidth = Layout.TruncateToggleTrackWidth,
            TrackHeight = TruncateToggleTrackHeight,
            ThumbSize = TruncateToggleThumbSize,
            ThumbHoverSize = TruncateToggleThumbSize,
            ThumbCheckedSize = TruncateToggleThumbSize,
            LabelFontSize = Layout.TruncateToggleFontSize,
            TrackCornerRadius = TruncateToggleTrackCornerRadius,
            ThumbCornerRadius = TruncateToggleThumbCornerRadius,
            BorderThickness = Layout.RootBorderThickness,
            ThumbUncheckedMargin = TruncateToggleThumbUncheckedMargin,
            ThumbCheckedMargin = TruncateToggleThumbCheckedMargin,
            LabelMargin = Layout.TruncateToggleLabelMargin,
            Margin = Layout.TruncateToggleMargin,
            EnabledOpacity = Layout.FullOpacity,
            DisabledOpacity = Layout.ControlDisabledOpacity
        };

    /// <summary>
    /// Builds the inline transform editor for a selected probe.
    /// </summary>
    private Grid BuildTransformControls(
        ProbeCardProbe probe,
        ProbeSelectorVisualGeneration generation)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock label = TrayAppDotNETSettingsUI.Text(text: "X=", _palette, Layout.TransformLabelFontSize);
        label.Margin = Layout.TransformLabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        TextBox textBox = TransformTextBox(probe.TransformString, Layout.TransformInlineBoxWidth);
        ControlNames.Assign(textBox, parentName: "ProbeTransform")
            .MapTo(ControlMap.ProbeChoiceCard.TransformExpression);
        textBox.Tag = probe;
        textBox.GotFocus += TransformTextBoxGotFocus;
        textBox.KeyDown += TransformTextBoxKeyDown;
        textBox.LostFocus += TransformTextBoxLostFocus;
        RegisterFocusTarget(
            generation,
            textBox,
            new ProbeFocusIdentity(probe.DataSourceKey, ProbeFocusRole.TransformEditor));
        Grid.SetColumn(textBox, value: 1);
        row.Children.Add(textBox);
        return row;
    }

    /// <summary>
    /// Builds the settings gear button for a selectable probe card.
    /// </summary>
    private SettingsButton BuildGearButton(bool transformIsActive)
    {
        SettingsButton button = new(GlyphCatalog.SETTINGS.Text, _palette, transparentBase: true)
        {
            Width = Layout.ActionButtonWidth,
            Height = Layout.ActionButtonHeight,
            MinHeight = Layout.ActionButtonHeight,
            Padding = Layout.ZeroThickness,
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.ActionButtonGlyphFontSize }
        };
        GlyphApplicator.ApplyTo(button.Label, GlyphCatalog.SETTINGS);
        ApplyGearButtonTransformVisual(button, transformIsActive);
        return button;
    }

    /// <summary>
    /// Applies explicit active-transform visuals to icon-only gear buttons.
    /// </summary>
    private void ApplyGearButtonTransformVisual(SettingsButton button, bool transformIsActive)
    {
        button.IsEnabled = true;
        button.Opacity = Layout.FullOpacity;
        button.Label.Opacity = transformIsActive
            ? Layout.FullOpacity
            : Layout.TransformActionButtonInactiveGlyphOpacity;
        button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(
            transformIsActive ? _palette.Foreground : _palette.SecondaryForeground);
    }

    /// <summary>
    /// Checks whether a probe has an active transform expression.
    /// </summary>
    private static bool ProbeTransformIsActive(ProbeCardProbe? probe) =>
        !string.IsNullOrWhiteSpace(probe?.TransformString);

    /// <summary>
    /// Builds a transform text box.
    /// </summary>
    private TextBox TransformTextBox(string text, double width)
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = Layout.TransformBoxHeight,
            MinHeight = Layout.TransformBoxMinHeight,
            Text = text,
            PlaceholderText = "X",
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.CardValueFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.ZeroThickness,
            Padding = Layout.TransformBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            textBox,
            _palette,
            TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(_palette.Hover),
            TrayAppDotNETSettingsUI.Brush(_palette.TextBoxFocused));
        return textBox;
    }

    /// <summary>
    /// Builds a device nickname rule text box.
    /// </summary>
    private TextBox NicknameTextBox(string text, string placeholder, double width)
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = Layout.NicknameTextBoxHeight,
            MinHeight = Layout.NicknameTextBoxHeight,
            MaxHeight = Layout.NicknameTextBoxHeight,
            Text = text,
            PlaceholderText = placeholder,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.CardValueFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.ZeroThickness,
            Padding = Layout.NicknameTextBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            textBox,
            _palette,
            TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(_palette.Hover),
            TrayAppDotNETSettingsUI.Brush(_palette.TextBoxFocused));
        return textBox;
    }

    /// <summary>
    /// Builds the delete button for a device nickname rule.
    /// </summary>
    private SettingsButton BuildNicknameDeleteButton()
    {
        SettingsButton button = new(GlyphCatalog.CLOSE.Text, _palette, transparentBase: true)
        {
            Width = Layout.NicknameDeleteButtonWidth,
            Height = Layout.NicknameDeleteButtonHeight,
            MinHeight = Layout.NicknameDeleteButtonHeight,
            Padding = Layout.ZeroThickness,
            Margin = Layout.NicknameDeleteButtonMargin,
            Label =
            {
                FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.NicknameDeleteButtonFontSize
            }
        };
        GlyphApplicator.ApplyTo(button.Label, GlyphCatalog.CLOSE);
        return button;
    }

    /// <summary>
    /// Wraps a selector card in the common card chrome.
    /// </summary>
    private Border WrapCard(Control content, bool isSelected)
    {
        content.VerticalAlignment = VerticalAlignment.Top;
        return new Border
        {
            Width = Layout.GridCardWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = TrayAppDotNETSettingsUI.Brush(
                isSelected ? _palette.SearchListItemSelected : _palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.NicknameCardPadding,
            Margin = Layout.CardMargin,
            Child = content
        };
    }

    /// <summary>
    /// Wraps a nickname rule row in the nickname card chrome.
    /// </summary>
    private Border WrapNicknameCard(Control content) =>
        new()
        {
            Width = Layout.NicknameCardWidth,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.CardPadding,
            Margin = Layout.CardMargin,
            Child = content
        };

    /// <summary>
    /// Builds empty-state text for a tab body.
    /// </summary>
    private TextBlock EmptyText(string text)
    {
        TextBlock block = TrayAppDotNETSettingsUI.Text(text, _palette, Layout.EmptyStateFontSize);
        block.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    /// <summary>
    /// Toggles whether the probe card displays a source.
    /// </summary>
    private void ToggleProbe(DataSource source, bool enabled)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (enabled)
        {
            if (probe is null)
            {
                _probeCard.Probes.Add(new ProbeCardProbe { DataSourceKey = source.DataSourceKey });
                _changed(_probeCard);
            }
            else if (!probe.IsSelected)
            {
                probe.IsSelected = true;
                _changed(_probeCard);
            }

            RebuildContent();
            return;
        }

        if (probe is not null)
        {
            probe.IsSelected = false;
            _expandedTransformKeys.Remove(source.DataSourceKey);
            RemoveProbeSettingsIfDefault(probe);
            _changed(_probeCard);
        }

        RebuildContent();
    }

    /// <summary>
    /// Persists per-probe value truncation from a selector card.
    /// </summary>
    private void SetProbeTruncateValue(DataSource source, bool truncateValue)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (probe is null)
        {
            if (!truncateValue) return;

            _probeCard.Probes.Add(new ProbeCardProbe
            {
                DataSourceKey = source.DataSourceKey, IsSelected = false, TruncateValue = true
            });
            _changed(_probeCard);
            RebuildContent();
            return;
        }

        if (probe.TruncateValue == truncateValue) return;

        probe.TruncateValue = truncateValue;
        if (!truncateValue)
            RemoveProbeSettingsIfDefault(probe);
        _changed(_probeCard);
        RefreshVisibleValues();
    }

    /// <summary>
    /// Removes inactive probe settings when no transform or truncate state is stored.
    /// </summary>
    private void RemoveProbeSettingsIfDefault(ProbeCardProbe probe)
    {
        if (probe.IsSelected) return;
        if (probe.TruncateValue) return;
        if (!string.IsNullOrWhiteSpace(probe.TransformString)) return;

        _probeCard.Probes.Remove(probe);
        _expandedTransformKeys.Remove(probe.DataSourceKey);
    }

    /// <summary>
    /// Adds a new global device nickname rule.
    /// </summary>
    private void AddDeviceNicknameRule()
    {
        _settings.DeviceNicknameRules.Add(new DeviceNicknameRule());
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Adds a new global probe nickname rule.
    /// </summary>
    private void AddProbeNicknameRule()
    {
        _settings.ProbeNicknameRules.Add(new DeviceNicknameRule());
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Restores the default hardware-type device nickname rules.
    /// </summary>
    private void LoadDefaultDeviceNicknames()
    {
        if (!_settings.LoadDefaultDeviceNicknameRules()) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Restores the default probe nickname rules.
    /// </summary>
    private void LoadDefaultProbeNicknames()
    {
        if (!_settings.LoadDefaultProbeNicknameRules()) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Deletes a global device nickname rule.
    /// </summary>
    private void DeleteDeviceNicknameRule(DeviceNicknameRule rule)
    {
        if (!_settings.DeviceNicknameRules.Remove(rule)) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Deletes a global probe nickname rule.
    /// </summary>
    private void DeleteProbeNicknameRule(DeviceNicknameRule rule)
    {
        if (!_settings.ProbeNicknameRules.Remove(rule)) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Removes persisted probe sensors that were not found by live LHM discovery.
    /// </summary>
    private void ClearDeadSensors()
    {
        if (!DataSource.DataSources.Values.Any(static source => source.IsLiveHardwareSensor)) return;

        HashSet<string> deadKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (DataSource source in DataSource.DataSources.Values)
        {
            if (source.IsLiveHardwareSensor) continue;
            if (!ProbeValueFormatter.IsProbeDataSource(source)) continue;
            if (string.IsNullOrWhiteSpace(source.DataSourceKey)) continue;
            deadKeys.Add(source.DataSourceKey);
        }

        if (deadKeys.Count == 0) return;

        foreach (string deadKey in deadKeys)
            DataSource.Unregister(deadKey);

        foreach (ProbeCard probeCard in _settings.ProbeCards)
            RemoveDeadProbeSelections(probeCard, deadKeys);
        RemoveDeadProbeSelections(_probeCard, deadKeys);

        foreach (string deadKey in deadKeys) _expandedTransformKeys.Remove(deadKey);

        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Removes selected probes whose data source was cleared.
    /// </summary>
    private static void RemoveDeadProbeSelections(ProbeCard probeCard, HashSet<string> deadKeys)
    {
        for (int i = probeCard.Probes.Count - 1; i >= 0; i--)
        {
            if (!deadKeys.Contains(probeCard.Probes[i].DataSourceKey)) continue;
            probeCard.Probes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Commits a nickname rule field when Enter or Escape is pressed; focus stays in the editor.
    /// </summary>
    private void NicknameRuleKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        CommitNicknameRuleTextBox(textBox, isLosingFocus: false);
        e.Handled = true;
    }

    /// <summary>
    /// Commits a nickname rule edit when focus leaves its editor.
    /// </summary>
    private void NicknameRuleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox) CommitNicknameRuleTextBox(textBox, isLosingFocus: true);
    }

    /// <summary>
    /// Persists one nickname rule text-box edit. A commit from LostFocus defers its rebuild until focus settles.
    /// </summary>
    private void CommitNicknameRuleTextBox(TextBox textBox, bool isLosingFocus)
    {
        if (!ApplyNicknameRuleEdit(textBox)) return;

        _changed(_probeCard);
        RebuildAfterCommit(isLosingFocus);
    }

    /// <summary>
    /// Applies one nickname rule text-box edit to its rule and returns whether the rule changed.
    /// </summary>
    private static bool ApplyNicknameRuleEdit(TextBox textBox)
    {
        if (textBox.Tag is not NicknameRuleEditorTag editorTag) return false;

        string next = textBox.Text ?? string.Empty;
        return editorTag.Field switch
        {
            NicknameRuleField.Target => CommitNicknameTarget(editorTag.Rule, next.Trim()),
            NicknameRuleField.Replacement => CommitNicknameReplacement(editorTag.Rule, next),
            _ => false
        };
    }

    /// <summary>
    /// Rebuilds after a text commit. A commit from LostFocus runs while focus is still moving, so its rebuild waits.
    /// </summary>
    private void RebuildAfterCommit(bool isLosingFocus)
    {
        if (isLosingFocus)
        {
            ScheduleRebuild();
            return;
        }

        RebuildContent();
    }

    /// <summary>
    /// Applies the focused text box's pending edit before a rebuild detaches it, so the new content shows the edit and
    /// the detach does not commit it again in the middle of publication.
    /// </summary>
    private void ApplyFocusedTextEdit()
    {
        if (FocusManager?.GetFocusedElement() is not TextBox textBox || !IsActiveGenerationVisual(textBox)) return;

        bool isChanged = textBox.Tag switch
        {
            NicknameRuleEditorTag => ApplyNicknameRuleEdit(textBox),
            ProbeCardProbe => ApplyTransformEdit(textBox).IsChanged,
            _ => false
        };
        if (isChanged)
            _changed(_probeCard);
    }

    /// <summary>
    /// Applies a target regex edit to a nickname rule.
    /// </summary>
    private static bool CommitNicknameTarget(DeviceNicknameRule rule, string next)
    {
        if (string.Equals(rule.TargetRegex, next, StringComparison.Ordinal)) return false;
        rule.TargetRegex = next;
        return true;
    }

    /// <summary>
    /// Applies a replacement-string edit to a nickname rule.
    /// </summary>
    private static bool CommitNicknameReplacement(DeviceNicknameRule rule, string next)
    {
        if (string.Equals(rule.ReplacementString, next, StringComparison.Ordinal)) return false;
        rule.ReplacementString = next;
        return true;
    }

    /// <summary>
    /// Toggles the transform editor for a source without selecting the probe.
    /// </summary>
    private void ToggleTransform(DataSource source)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (probe is null)
        {
            probe = new ProbeCardProbe { DataSourceKey = source.DataSourceKey, IsSelected = false };
            _probeCard.Probes.Add(probe);
            _changed(_probeCard);
        }

        if (!_expandedTransformKeys.Add(source.DataSourceKey))
        {
            _expandedTransformKeys.Remove(source.DataSourceKey);
            RemoveProbeSettingsIfDefault(probe);
            _changed(_probeCard);
        }

        RebuildContent();
    }

    /// <summary>
    /// Tracks the active transform text box for outside-click commit behavior.
    /// </summary>
    private void TransformTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && IsActiveGenerationVisual(textBox))
            _focusedTransformTextBox = textBox;
    }

    /// <summary>
    /// Commits the transform expression on Enter or Escape; focus stays in the editor.
    /// </summary>
    private void TransformTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!IsActiveGenerationVisual(textBox)) return;
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        CommitTransformTextBox(textBox, isLosingFocus: false);
        e.Handled = true;
    }

    /// <summary>
    /// Commits the transform expression when focus leaves the editor.
    /// </summary>
    private void TransformTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!IsActiveGenerationVisual(textBox)) return;

        CommitTransformTextBox(textBox, isLosingFocus: true);
        if (ReferenceEquals(_focusedTransformTextBox, textBox))
            _focusedTransformTextBox = null;
    }

    /// <summary>
    /// Drops transform text focus when the selector is clicked outside the active editor.
    /// </summary>
    private void OnSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_focusedTransformTextBox is null) return;
        if (IsSelfOrDescendant(_focusedTransformTextBox, e.Source as Visual)) return;

        DropTransformTextBoxFocus(_focusedTransformTextBox);
    }

    /// <summary>
    /// Commits transform text and moves focus to selector chrome.
    /// </summary>
    private void DropTransformTextBoxFocus(TextBox textBox)
    {
        CommitTransformTextBox(textBox, isLosingFocus: false);
        textBox.ClearSelection();
        _activeVisualGeneration?.FocusSink?.Focus();
    }

    /// <summary>
    /// Determines whether a visual is the target control or one of its descendants.
    /// </summary>
    private static bool IsSelfOrDescendant(Visual owner, Visual? visual)
    {
        if (visual is null) return false;
        if (ReferenceEquals(visual, owner)) return true;
        return visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, owner));
    }

    private bool IsActiveGenerationVisual(Visual visual)
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null || generation.ContentGeneration.IsDisposed) return false;
        return IsSelfOrDescendant(generation.ContentGeneration.Root, visual);
    }

    /// <summary>
    /// Persists the transform expression from a text box. A commit from LostFocus defers its rebuild until focus
    /// settles.
    /// </summary>
    private void CommitTransformTextBox(TextBox textBox, bool isLosingFocus)
    {
        (bool isChanged, bool isActivityChanged) = ApplyTransformEdit(textBox);
        if (!isChanged) return;

        _changed(_probeCard);
        if (isActivityChanged)
        {
            RebuildAfterCommit(isLosingFocus);
            return;
        }

        RefreshVisibleValues();
    }

    /// <summary>
    /// Applies the transform expression from a text box to its probe. Returns whether the probe changed, and whether
    /// the transform switched between active and inactive, which changes the card chrome.
    /// </summary>
    private (bool IsChanged, bool IsActivityChanged) ApplyTransformEdit(TextBox textBox)
    {
        if (textBox.Tag is not ProbeCardProbe probe) return (IsChanged: false, IsActivityChanged: false);

        string next = (textBox.Text ?? string.Empty).Trim();
        if (string.Equals(next, probe.TransformString, StringComparison.Ordinal))
            return (IsChanged: false, IsActivityChanged: false);

        bool previousTransformIsActive = ProbeTransformIsActive(probe);
        probe.TransformString = next;
        bool nextTransformIsActive = ProbeTransformIsActive(probe);
        if (!nextTransformIsActive)
            RemoveProbeSettingsIfDefault(probe);
        return (IsChanged: true, IsActivityChanged: previousTransformIsActive != nextTransformIsActive);
    }

    /// <summary>
    /// Registers a value text block for live refresh.
    /// </summary>
    private static void RegisterValueText(
        ProbeSelectorVisualGeneration generation,
        string dataSourceKey,
        TextBlock textBlock)
    {
        if (!generation.ValueTextByKey.TryGetValue(dataSourceKey, out List<TextBlock>? list))
        {
            list = [];
            generation.ValueTextByKey[dataSourceKey] = list;
        }

        list.Add(textBlock);
    }

    /// <summary>
    /// Refreshes the value text in visible selector cards.
    /// </summary>
    private void RefreshVisibleValues()
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null) return;

        foreach ((string dataSourceKey, List<TextBlock> textBlocks) in generation.ValueTextByKey)
        {
            DataSource? source = DataSource.Find(dataSourceKey);
            if (source is null) continue;
            ProbeCardProbe? probe = _probeCard.FindProbe(dataSourceKey);
            string value = ProbeValueLine(source, probe, generation.ProbeNicknameResolver);
            foreach (TextBlock textBlock in textBlocks)
                textBlock.Text = value;
        }
    }

    /// <summary>
    /// Formats a probe name and current value for selector cards.
    /// </summary>
    private static string ProbeValueLine(
        DataSource source,
        ProbeCardProbe? probe,
        ProbeNicknameResolver probeNicknameResolver) =>
        $"{probeNicknameResolver.Resolve(source.DisplayName)}: "
        + ProbeValueFormatter.FormatValue(source, probe, probe?.TruncateValue == true);

    /// <summary>
    /// Refreshes probe values after each LHM poll.
    /// </summary>
    private void OnPollTickCompleted()
    {
        if (_windowResources.IsDisposed || !IsVisible) return;
        RefreshVisibleValues();
    }

    /// <summary>
    /// Places initial focus on the first page control, without a focus cue, so arrow keys work at once.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null) return;

        (FirstPageFocusTarget(generation) ?? generation.FocusSink)?.Focus();
    }

    /// <summary>Detaches external publishers before retiring candidate-owned controls.</summary>
    protected override void OnClosed(EventArgs e)
    {
        DisposeSelectorResources();
        base.OnClosed(e);
    }

    private void DisposeSelectorResources()
    {
        _windowResources.Dispose();

        ClearHomeEditState();

        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        _activeVisualGeneration = null;
        try
        {
            Content = null;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow content detach failed: {exception.Message}");
        }
        finally
        {
            if (generation is { ContentGeneration.IsDisposed: false })
                generation.ContentGeneration.Dispose();
        }

        _focusedTransformTextBox = null;
        _focusIdentity = null;
        _isKeyboardFocus = false;
    }

    /// <summary>
    /// Resolves the tab label.
    /// </summary>
    private static string TabLabel(ProbeSelectorTab tab) => tab switch
    {
        ProbeSelectorTab.Home => L(nameof(AppStrings.ProbeSelector_Tab_Home)),
        ProbeSelectorTab.Temperatures => L(nameof(AppStrings.ProbeSelector_Tab_Temperatures)),
        ProbeSelectorTab.Power => L(nameof(AppStrings.ProbeSelector_Tab_Power)),
        ProbeSelectorTab.Load => L(nameof(AppStrings.ProbeSelector_Tab_Load)),
        ProbeSelectorTab.Clocks => L(nameof(AppStrings.ProbeSelector_Tab_Clocks)),
        ProbeSelectorTab.Voltages => L(nameof(AppStrings.ProbeSelector_Tab_Voltages)),
        _ => string.Empty
    };

    private static string L(string key) => LocalizationManager.Instance[key];

    private enum ProbeSelectorTab
    {
        Home,
        Temperatures,
        Power,
        Load,
        Clocks,
        Voltages
    }

    private enum NicknameRuleField
    {
        Target,
        Replacement
    }

    private enum ProbeFocusEntity
    {
        ClearDeadSensors,
        LoadDefaultDeviceNicknames,
        AddDeviceNickname,
        LoadDefaultProbeNicknames,
        AddProbeNickname
    }

    private enum ProbeFocusRole
    {
        Tab,
        ProfileVisibility,
        Enable,
        Truncate,
        TransformButton,
        TransformEditor,
        NicknameTargetEditor,
        NicknameReplacementEditor,
        Delete,
        Action
    }

    private sealed record NicknameRuleEditorTag(DeviceNicknameRule Rule, NicknameRuleField Field);

    /// <summary>
    /// Names a focusable control by what it edits rather than by instance, so it survives content rebuilds.
    /// </summary>
    private readonly record struct ProbeFocusIdentity(object Entity, ProbeFocusRole Role);

    /// <summary>
    /// One focusable control of a visual generation and its stable identity.
    /// </summary>
    private readonly record struct ProbeFocusTarget(ProbeFocusIdentity Identity, Control Control);

    /// <summary>Owns the maps, resolvers, panels, and root for one selector rebuild.</summary>
    private sealed class ProbeSelectorVisualGeneration(
        ProbeSelectorTab selectedTab,
        DeviceNicknameResolver deviceNicknameResolver,
        ProbeNicknameResolver probeNicknameResolver,
        UIResourceScope resources)
    {
        private UIContentGeneration? _contentGeneration;
        private bool _retired;

        public ProbeSelectorTab SelectedTab { get; } = selectedTab;
        public DeviceNicknameResolver DeviceNicknameResolver { get; } = deviceNicknameResolver;
        public ProbeNicknameResolver ProbeNicknameResolver { get; } = probeNicknameResolver;
        public UIResourceScope Resources { get; } = resources;

        public Dictionary<string, List<TextBlock>> ValueTextByKey { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<ProbeFocusTarget> FocusTargets { get; } = [];
        public Control? FocusSink { get; set; }
        public StackPanel? SelectedProbeListPanel { get; set; }

        public UIContentGeneration ContentGeneration =>
            _contentGeneration
            ?? throw new InvalidOperationException("The probe selector generation has not been completed.");

        public void AttachContentGeneration(UIContentGeneration contentGeneration)
        {
            ArgumentNullException.ThrowIfNull(contentGeneration);
            if (_contentGeneration != null)
                throw new InvalidOperationException("The probe selector generation is already complete.");
            _contentGeneration = contentGeneration;
        }

        public void Retire()
        {
            if (_retired) return;
            _retired = true;
            ValueTextByKey.Clear();
            FocusTargets.Clear();
            SelectedProbeListPanel?.Children.Clear();
            SelectedProbeListPanel = null;
            FocusSink?.DataContext = null;
        }
    }
}
