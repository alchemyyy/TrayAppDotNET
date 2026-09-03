using System.Globalization;
using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.UI.Tray;

/// <summary>Formats the latest performance snapshot for the tray tooltip.</summary>
internal static class TaskManagerTrayTooltipFormatter
{
    private const string UnavailableText = "Unavailable";

    /// <summary>Builds the compact four-line tooltip.</summary>
    public static string Format(
        PerformanceSnapshot snapshot,
        IReadOnlyList<string>? performanceDeviceOrder = null,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        string CPU = FormatPercent(
            snapshot.CPU.HasUtilizationSample,
            snapshot.CPU.UtilizationPercent,
            effectiveCulture);
        string memory = FormatPercent(
            snapshot.Memory.HasMemoryData,
            snapshot.Memory.UtilizationPercent,
            effectiveCulture);
        string disk = FormatDisk(snapshot.Disks.Span, effectiveCulture);
        string network = FormatNetwork(
            snapshot.Networks.Span,
            performanceDeviceOrder,
            effectiveCulture);
        return $"CPU {CPU}\nMemory {memory}\nDisk {disk}\nNetwork {network}";
    }

    private static string FormatDisk(
        ReadOnlySpan<DiskPerformanceSnapshot> disks,
        CultureInfo culture)
    {
        bool hasSample = false;
        double highestActiveTimePercent = 0;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
        {
            DiskPerformanceSnapshot disk = disks[diskIndex];
            DiskPerformanceDetailsSnapshot? details = disk.Details;
            bool hasPerformanceSample = details?.HasPerformanceSample
                                        ?? disk.HasPerformanceSample;
            double activeTimePercent = details?.ActiveTimePercent
                                       ?? disk.ActiveTimePercent;
            if (!hasPerformanceSample || !double.IsFinite(activeTimePercent))
                continue;

            hasSample = true;
            highestActiveTimePercent = Math.Max(
                highestActiveTimePercent,
                Math.Clamp(activeTimePercent, min: 0, max: 100));
        }

        return FormatPercent(hasSample, highestActiveTimePercent, culture);
    }

    private static string FormatNetwork(
        ReadOnlySpan<NetworkPerformanceSnapshot> networks,
        IReadOnlyList<string>? performanceDeviceOrder,
        CultureInfo culture)
    {
        List<PerformanceDeviceOrderItem> orderItems = new(networks.Length);
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
        {
            NetworkPerformanceSnapshot network = networks[networkIndex];
            orderItems.Add(new PerformanceDeviceOrderItem(
                network.DeviceID,
                network.Kind,
                network.SortKey));
        }

        List<PerformanceDeviceOrderItem> sortedItems = PerformanceDeviceOrdering.Resolve(
            orderItems,
            priority: null,
            explicitDeviceIDs: performanceDeviceOrder);
        if (sortedItems.Count == 0) return UnavailableText;

        string firstDeviceID = sortedItems[0].ID;
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
        {
            NetworkPerformanceSnapshot network = networks[networkIndex];
            if (!string.Equals(network.DeviceID, firstDeviceID, StringComparison.Ordinal))
                continue;

            double utilizationPercent = 0;
            bool hasUtilization = network.IsOperational
                                  && PerformanceDevicePresentationFactory.TryGetNetworkUtilization(
                                      network,
                                      out utilizationPercent);
            return FormatPercent(hasUtilization, utilizationPercent, culture);
        }

        return UnavailableText;
    }

    private static string FormatPercent(
        bool isAvailable,
        double percent,
        CultureInfo culture)
    {
        if (!isAvailable || !double.IsFinite(percent)) return UnavailableText;

        int roundedPercent = (int)Math.Round(
            Math.Clamp(percent, min: 0, max: 100),
            MidpointRounding.AwayFromZero);
        return string.Concat(roundedPercent.ToString(culture), str1: "%");
    }
}
