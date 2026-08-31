using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides shared color calculations for Performance graph rendering.</summary>
internal static class PerformanceGraphRendering
{
    /// <summary>Creates a translucent darker shade derived from a graph line color.</summary>
    public static Color CreateUnderfillColor(
        Color lineColor,
        double opacity,
        int darkenAmount)
    {
        double normalizedOpacity = double.IsFinite(opacity)
            ? Math.Clamp(opacity, min: 0, max: 1)
            : 0;
        int normalizedDarkenAmount = Math.Clamp(darkenAmount, min: 0, byte.MaxValue);
        byte alpha = (byte)Math.Round(
            lineColor.A / (double)byte.MaxValue * normalizedOpacity * byte.MaxValue,
            MidpointRounding.AwayFromZero);
        return Color.FromArgb(
            alpha,
            Darken(lineColor.R, normalizedDarkenAmount),
            Darken(lineColor.G, normalizedDarkenAmount),
            Darken(lineColor.B, normalizedDarkenAmount));
    }

    private static byte Darken(byte component, int amount) =>
        (byte)Math.Max(val1: 0, component - amount);
}
