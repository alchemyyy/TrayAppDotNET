using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

public sealed record SettingsPageDescriptor<TPageKey>(
    TPageKey Key,
    string Label,
    Func<Control> BuildPage)
    where TPageKey : notnull;

/// <summary>
/// Shared settings-window shell: custom chrome, navigation, page hosting, confirmation overlay,
/// rounded-corner policy, and common card wrappers.
/// </summary>
public abstract partial class SettingsWindowCommon<TPageKey> : Window
    where TPageKey : notnull
{
    private const double TitleBarHeight = 32.0;
    private const double TitleBarDragZoneHeight = 72.0;

    private ContentControl _content = new();
    private readonly Dictionary<TPageKey, Func<Control>> _pages = [];
    private readonly Dictionary<TPageKey, SettingsNavItem> _navItems = [];
    private readonly Dictionary<TPageKey, double> _pageScrollOffsets = [];
    private SettingsScrollHost? _scrollHost;
    private TaskCompletionSource<bool>? _confirmTcs;
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private SettingsButton? _confirmOk;
    private SettingsButton? _confirmCancel;
    private readonly Win32Properties.CustomWndProcHookCallback _wndProcHook;
    private bool _shellInitialized;
    private bool _hasShownPage;
    private bool _wndProcHookAttached;

    protected TPageKey CurrentPageKey { get; private set; } = default!;
    protected bool IsClosing { get; private set; }

    protected abstract SettingsPalette Palette { get; }
    protected abstract bool EnableRoundedCorners { get; }
    protected abstract TPageKey DefaultPageKey { get; }
    protected abstract string HeaderText { get; }
    protected abstract string OpenSettingsFolderText { get; }
    protected abstract string SettingsFolderPath { get; }
    protected abstract IReadOnlyList<SettingsPageDescriptor<TPageKey>> CreatePageDescriptors();
    protected abstract void Save();

    protected virtual Color ConfirmOverlayBackdrop => Color.FromArgb(0xA0, 0, 0, 0);
    protected virtual double SidebarWidth => 230;

    protected SettingsWindowCommon()
    {
        _wndProcHook = WndProcHook;
        Opened += (_, _) => AttachWndProcHook();
        Closed += (_, _) => DetachWndProcHook();
    }

    protected void ConfigureSettingsWindow(
        string title,
        double width,
        double height,
        double minWidth,
        double minHeight,
        WindowIcon? icon)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = minWidth;
        MinHeight = minHeight;
        Icon = icon;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = true;
    }

    protected void InitializeSettingsShell()
    {
        if (_shellInitialized) return;

        _shellInitialized = true;
        CurrentPageKey = DefaultPageKey;
        SetSettingsContent(BuildRoot());
        ShowPage(CurrentPageKey);

        Closed += (_, _) =>
        {
            IsClosing = true;
            OnSettingsWindowClosed();
        };
    }

    protected virtual void OnSettingsWindowClosed()
    {
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        _confirmTcs?.TrySetResult(false);
        _confirmTitle!.Text = title;
        _confirmMessage!.Text = message;
        _confirmOk!.Text = confirmText;
        _confirmCancel!.Text = cancelText;
        _confirmOverlay!.IsVisible = true;
        _confirmTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _confirmTcs.Task;
    }

    protected void SelectPage(TPageKey key) => ShowPage(key);

    protected void RefreshCurrentPage() => ShowPage(CurrentPageKey, force: true);

    protected void RebuildShell(TPageKey selectedPageKey)
    {
        if (_hasShownPage && _scrollHost != null)
            _pageScrollOffsets[CurrentPageKey] = _scrollHost.VerticalOffset;

        _content = new ContentControl();
        _hasShownPage = false;
        SetSettingsContent(BuildRoot());
        ShowPage(selectedPageKey, force: true);
    }

    protected static string L(string key, string fallback = "")
    {
        try
        {
            string value = LocalizationManager.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    protected static string Loc(string key) => L(key, key);

    protected CornerRadius RadiusTiny => new(EnableRoundedCorners ? 1.5 : 0);
    protected CornerRadius RadiusMedium => new(EnableRoundedCorners ? 4 : 0);
    protected CornerRadius RadiusLarge => new(EnableRoundedCorners ? 6 : 0);

    protected StackPanel PageStack(string title, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.PageStack(title, palette);

    protected SettingsButton Button(string text, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.Button(text, palette, RadiusMedium);

    protected Border BoolCard(
        string title,
        string description,
        bool value,
        Action<bool> set,
        SettingsPalette palette,
        Action? afterSave = null) =>
        TrayAppDotNETSettingsCards.BoolCard(
            title,
            description,
            value,
            set,
            palette,
            RadiusLarge,
            Save,
            afterSave);

    protected Border IntCard(
        string title,
        string description,
        int value,
        int min,
        int max,
        Action<int> set,
        SettingsPalette palette,
        string suffix = "") =>
        TrayAppDotNETSettingsCards.IntCard(
            title,
            description,
            value,
            min,
            max,
            set,
            palette,
            RadiusLarge,
            Save,
            suffix);

    protected Border ComboCard(
        string title,
        string description,
        IReadOnlyList<(string Tag, string Text)> items,
        string selectedTag,
        Action<string> set,
        SettingsPalette palette,
        Action? afterSave = null,
        bool autoSizeToText = false,
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem) =>
        TrayAppDotNETSettingsCards.ComboCard(
            title,
            description,
            items,
            selectedTag,
            set,
            palette,
            RadiusLarge,
            Save,
            afterSave,
            autoSizeToText,
            autoSizeMode);

    protected Border Card(string title, string description, Control? rightControl, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.Card(title, description, rightControl, palette, RadiusLarge);

    protected Border RawCard(Control content, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.RawCard(content, palette, RadiusLarge);

    protected Border MutableCard(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        out TextBlock descriptionText) =>
        TrayAppDotNETSettingsCards.MutableCard(
            title,
            description,
            rightControl,
            palette,
            RadiusLarge,
            out descriptionText);

    protected Task ShowMessage(string title, string message) =>
        ConfirmAsync(title, message, "OK", "OK");

    private void SetSettingsContent(Control content) => Content = content;

    private Border BuildRoot()
    {
        SettingsPalette palette = Palette;
        _pages.Clear();
        _navItems.Clear();

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SidebarWidth)));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Grid sidebar = new() { Background = Brushes.Transparent };
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(sidebar, 0);
        body.Children.Add(sidebar);

        TextBlock header = TrayAppDotNETSettingsUI.Text(HeaderText, palette, 22, FontWeight.SemiBold);
        header.Margin = new Thickness(24, 8, 20, 20);
        Grid.SetRow(header, 0);
        sidebar.Children.Add(header);

        StackPanel nav = new() { Margin = new Thickness(8, 0) };
        foreach (SettingsPageDescriptor<TPageKey> page in CreatePageDescriptors())
        {
            _pages[page.Key] = page.BuildPage;
            AddNavItem(nav, page.Key, page.Label, palette);
        }

        Grid.SetRow(nav, 1);
        sidebar.Children.Add(nav);

        StackPanel footer = new() { Margin = new Thickness(8, 0, 8, 12) };
        SettingsNavAction folderButton = new(OpenSettingsFolderText, palette, RadiusTiny, RadiusMedium);
        folderButton.Click += (_, _) => TrayAppDotNETSettingsActions.OpenFolder(SettingsFolderPath);
        footer.Children.Add(folderButton);
        Grid.SetRow(footer, 2);
        sidebar.Children.Add(footer);

        _scrollHost = TrayAppDotNETSettingsUI.ScrollHost(_content, palette, new Thickness(24, 4, 30, 28));
        Grid.SetColumn(_scrollHost, 1);
        body.Children.Add(_scrollHost);

        Control titleBar = BuildTitleBar(palette);
        Grid.SetRow(titleBar, 0);
        Grid.SetRowSpan(titleBar, 2);
        root.Children.Add(titleBar);

        _confirmOverlay = BuildConfirmOverlay();
        _confirmOverlay.IsVisible = false;
        Grid.SetRow(_confirmOverlay, 1);
        root.Children.Add(_confirmOverlay);

        CornerRadius outerRadius = new(EnableRoundedCorners ? 8 : 0);
        CornerRadius innerRadius = new(EnableRoundedCorners ? 7 : 0);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = outerRadius,
            ClipToBounds = false,
            Child = new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
                CornerRadius = innerRadius,
                ClipToBounds = EnableRoundedCorners,
                Margin = new Thickness(1),
                Child = root,
            },
        };
    }

    private Grid BuildTitleBar(SettingsPalette palette)
    {
        Grid titleBar = new()
        {
            Background = Brushes.Transparent,
            Height = TitleBarDragZoneHeight,
            VerticalAlignment = VerticalAlignment.Top,
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is SettingsButton) return;
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(e);
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SettingsButton minimize = CaptionButton(GlyphCatalog.CHROME_MINIMIZE, palette);
        SettingsButton maximize = CaptionButton(
            WindowState == WindowState.Maximized
                ? GlyphCatalog.CHROME_RESTORE
                : GlyphCatalog.CHROME_MAXIMIZE,
            palette);
        SettingsButton close = CaptionButton(GlyphCatalog.CHROME_CLOSE, palette, closeButton: true);
        SetCaptionButtonTip(minimize, L("SettingsWindow_Caption_Minimize", "Minimize"));
        SetCaptionButtonTip(maximize, L("SettingsWindow_Caption_Maximize", "Maximize"));
        SetCaptionButtonTip(close, L("Common_Close", "Close"));
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        maximize.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => Close();
        buttons.Children.Add(minimize);
        buttons.Children.Add(maximize);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 1);
        titleBar.Children.Add(buttons);
        return titleBar;
    }

    private static SettingsButton CaptionButton(string glyph, SettingsPalette palette, bool closeButton = false)
    {
        SettingsButton button = new(glyph, palette, transparentBase: true)
        {
            Width = 46,
            Height = TitleBarHeight,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0),
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = 10 }
        };
        if (closeButton)
        {
            button.PointerEntered += (_, _) =>
            {
                button.Background = TrayAppDotNETSettingsUI.Brush(palette.CloseButtonHover);
                button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(palette.CloseButtonGlyphActive);
            };
            button.PointerExited += (_, _) =>
            {
                button.Background = Brushes.Transparent;
                button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
            };
        }

        return button;
    }

    private static void SetCaptionButtonTip(SettingsButton button, string text)
    {
        TrayAppDotNETToolTip.SetTip(button, text);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void AddNavItem(StackPanel nav, TPageKey key, string label, SettingsPalette palette)
    {
        SettingsNavItem item = new(label, palette, RadiusTiny, RadiusMedium);
        item.Click += (_, _) => ShowPage(key);
        _navItems[key] = item;
        nav.Children.Add(item);
    }

    private void ShowPage(TPageKey key, bool force = false)
    {
        if (!_pages.TryGetValue(key, out Func<Control>? factory)) return;
        if (!force
            && _hasShownPage
            && EqualityComparer<TPageKey>.Default.Equals(CurrentPageKey, key))
            return;

        if (_hasShownPage && _scrollHost != null)
            _pageScrollOffsets[CurrentPageKey] = _scrollHost.VerticalOffset;

        CurrentPageKey = key;
        foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
            item.IsSelected = EqualityComparer<TPageKey>.Default.Equals(navKey, key);
        _content.Content = factory();
        RestorePageScroll(key, resetBeforeLayout: !force);
        _hasShownPage = true;
    }

    private void RestorePageScroll(TPageKey key, bool resetBeforeLayout)
    {
        SettingsScrollHost? scrollHost = _scrollHost;
        if (scrollHost == null) return;

        double requestedOffset = _pageScrollOffsets.GetValueOrDefault(key, 0);

        if (resetBeforeLayout || requestedOffset <= 0)
            scrollHost.SetVerticalOffset(requestedOffset);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!EqualityComparer<TPageKey>.Default.Equals(CurrentPageKey, key)) return;
                _scrollHost?.SetVerticalOffset(requestedOffset);
            },
            DispatcherPriority.Loaded);
    }

    private Border BuildConfirmOverlay()
    {
        SettingsPalette palette = Palette;
        _confirmTitle = TrayAppDotNETSettingsUI.Text(
            L("SettingsWindow_ConfirmOverlay_DefaultTitle", "Confirm"),
            palette,
            16,
            FontWeight.SemiBold);
        _confirmTitle.TextWrapping = TextWrapping.Wrap;
        _confirmTitle.Margin = new Thickness(0, 0, 0, 8);
        _confirmMessage = TrayAppDotNETSettingsUI.DescriptionText(
            L("SettingsWindow_ConfirmOverlay_DefaultMessage", string.Empty),
            palette,
            new Thickness(0, 0, 0, 16));
        _confirmOk = Button(L("SettingsWindow_ConfirmOverlay_Confirm", "Confirm"), palette);
        _confirmCancel = Button(L("SettingsWindow_ConfirmOverlay_Cancel", "Cancel"), palette);
        _confirmCancel.Margin = new Thickness(0, 0, 8, 0);
        _confirmOk.MinWidth = 96;
        _confirmCancel.MinWidth = 96;
        _confirmOk.Click += (_, _) => CompleteConfirm(true);
        _confirmCancel.Click += (_, _) => CompleteConfirm(false);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(_confirmCancel);
        buttons.Children.Add(_confirmOk);

        Border dialog = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = RadiusLarge,
            Padding = new Thickness(24),
            MinWidth = 360,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { _confirmTitle, _confirmMessage, buttons } },
        };
        return new Border { Background = TrayAppDotNETSettingsUI.Brush(ConfirmOverlayBackdrop), Child = dialog, };
    }

    private void CompleteConfirm(bool result)
    {
        _confirmOverlay!.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    private void AttachWndProcHook()
    {
        if (_wndProcHookAttached || !OperatingSystem.IsWindows()) return;

        Win32Properties.AddWndProcHookCallback(this, _wndProcHook);
        _wndProcHookAttached = true;
    }

    private void DetachWndProcHook()
    {
        if (!_wndProcHookAttached || !OperatingSystem.IsWindows()) return;

        Win32Properties.RemoveWndProcHookCallback(this, _wndProcHook);
        _wndProcHookAttached = false;
    }

    private static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != User32.WM_MOUSEACTIVATE) return IntPtr.Zero;

        handled = true;
        return new IntPtr(User32.MA_ACTIVATE);
    }
}
