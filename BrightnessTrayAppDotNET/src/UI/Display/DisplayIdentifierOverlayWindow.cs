using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BrightnessTrayAppDotNET.UI.Display;

public sealed class DisplayIdentifierOverlayWindow : Window
{
    private readonly int _pxLeft;
    private readonly int _pxTop;
    private readonly int _pxWidth;
    private readonly int _pxHeight;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public DisplayIdentifierOverlayWindow(int displayNumber, int pxLeft, int pxTop, int pxWidth, int pxHeight)
    {
        _pxLeft = pxLeft;
        _pxTop = pxTop;
        _pxWidth = Math.Max(1, pxWidth);
        _pxHeight = Math.Max(1, pxHeight);

        double DPIScale = ResolveDPIScale(_pxLeft, _pxTop, _pxWidth, _pxHeight);

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.Manual;
        Position = new PixelPoint(_pxLeft, _pxTop);
        Width = _pxWidth / DPIScale;
        Height = _pxHeight / DPIScale;
        Focusable = false;
        IsHitTestVisible = false;

        Content = BuildContent(displayNumber);
        Opened += (_, _) => ApplyWindowChrome();
    }

    private static Grid BuildContent(int displayNumber)
    {
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLightTheme = AppTheme.ResolveEffectiveIsLightTheme(AppServices.Settings);

        TextBlock number = new()
        {
            Text = displayNumber.ToString(CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 220,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(theme.DisplayIdentifierForeground.For(isLightTheme)),
            TextAlignment = TextAlignment.Center,
            LineHeight = 230,
            Focusable = false,
            IsHitTestVisible = false,
        };

        Border card = new()
        {
            Background = Brush(theme.DisplayIdentifierBackground.For(isLightTheme)),
            BorderBrush = Brush(theme.DisplayIdentifierBorder.For(isLightTheme)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(56, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 24, Color = WithOpacity(theme.DisplayIdentifierShadow.For(isLightTheme), 0.5),
            }),
            Child = number,
            Focusable = false,
            IsHitTestVisible = false,
        };

        Grid root = new() { Background = Brushes.Transparent, Focusable = false, IsHitTestVisible = false, };
        root.Children.Add(card);
        return root;
    }

    private void ApplyWindowChrome()
    {
        IntPtr hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        _ = SetWindowPos(hwnd, IntPtr.Zero, _pxLeft, _pxTop, _pxWidth, _pxHeight, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static double ResolveDPIScale(int pxLeft, int pxTop, int pxWidth, int pxHeight)
    {
        User32.POINT point = new() { X = pxLeft + Math.Max(1, pxWidth) / 2, Y = pxTop + Math.Max(1, pxHeight) / 2, };

        IntPtr monitor = User32.MonitorFromPoint(point, User32.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero
            && User32.GetDpiForMonitor(monitor, User32.MDT_EFFECTIVE_DPI, out uint DPIX, out _) == 0
            && DPIX > 0)
            return Math.Max(0.25, DPIX / 96.0);

        return 1.0;
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static Color WithOpacity(Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
