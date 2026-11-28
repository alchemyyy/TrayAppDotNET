using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>
/// The user's changes to one Performance device card, keyed by the device's stable ID. An empty text or color means
/// the card's default, so a default customization is never stored.
/// </summary>
public sealed class PerformanceDeviceCardCustomization
{
    /// <summary>Line count value that keeps the device kind's default number of lines.</summary>
    public const int DefaultLineCount = -1;

    /// <summary>Most lines a card shows under its title.</summary>
    public const int MaximumLineCount = 2;

    [XmlAttribute]
    [AllowNull]
    public string DeviceID
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Replaces the card title, e.g. <c>GPU 0</c>.</summary>
    [XmlAttribute]
    [AllowNull]
    public string Title
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Replaces the device description, the text the <c>{description}</c> value inserts.</summary>
    [XmlAttribute]
    [AllowNull]
    public string Description
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Lines under the title, 0 to <see cref="MaximumLineCount"/>, or <see cref="DefaultLineCount"/>.</summary>
    [XmlAttribute]
    public int LineCount { get; set; } = DefaultLineCount;

    [XmlAttribute]
    [AllowNull]
    public string FirstLineFormat
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    [AllowNull]
    public string SecondLineFormat
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Base graph color as <c>#RRGGBB</c>; the card's other graph colors are shades of it.</summary>
    [XmlAttribute]
    [AllowNull]
    public string AccentColor
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Regex replacements applied, top to bottom, to the device description.</summary>
    [XmlArray("DescriptionReplacements")]
    [XmlArrayItem("Rule")]
    public List<PerformanceTextReplacementRule> DescriptionReplacements { get; set; } = [];

    [XmlIgnore]
    public bool IsDefault =>
        Title.Length == 0
        && Description.Length == 0
        && LineCount == DefaultLineCount
        && FirstLineFormat.Length == 0
        && SecondLineFormat.Length == 0
        && AccentColor.Length == 0
        && DescriptionReplacements.Count == 0;

    /// <summary>Returns the line format at <paramref name="lineIndex"/>, empty for the default.</summary>
    public string GetLineFormat(int lineIndex) => lineIndex == 0 ? FirstLineFormat : SecondLineFormat;

    /// <summary>Sets the line format at <paramref name="lineIndex"/>; empty restores the default.</summary>
    public void SetLineFormat(int lineIndex, string format)
    {
        if (lineIndex == 0) FirstLineFormat = format;
        else SecondLineFormat = format;
    }

    public PerformanceDeviceCardCustomization Clone() =>
        new()
        {
            DeviceID = DeviceID,
            Title = Title,
            Description = Description,
            LineCount = LineCount,
            FirstLineFormat = FirstLineFormat,
            SecondLineFormat = SecondLineFormat,
            AccentColor = AccentColor,
            DescriptionReplacements = DescriptionReplacements
                .Select(static rule => new PerformanceTextReplacementRule
                {
                    MatchPattern = rule.MatchPattern,
                    Replacement = rule.Replacement
                })
                .ToList()
        };

    /// <summary>Clamps an out-of-range line count to the default.</summary>
    public static int NormalizeLineCount(int lineCount) =>
        lineCount is >= 0 and <= MaximumLineCount ? lineCount : DefaultLineCount;
}

/// <summary>One regex replacement on a Performance device card's description.</summary>
public sealed class PerformanceTextReplacementRule
{
    [XmlAttribute]
    [AllowNull]
    public string MatchPattern
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    [AllowNull]
    public string Replacement
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;
}

/// <summary>Normalizes and edits the persisted Performance device card customizations.</summary>
internal static class PerformanceDeviceCardCustomizationCollection
{
    /// <summary>
    /// Copies the customizations, dropping entries without a device, default ones, and earlier duplicates of a
    /// device, so the last entry for a device wins.
    /// </summary>
    public static List<PerformanceDeviceCardCustomization> Normalize(
        IEnumerable<PerformanceDeviceCardCustomization?>? cards)
    {
        List<PerformanceDeviceCardCustomization> normalized = [];
        if (cards == null) return normalized;

        foreach (PerformanceDeviceCardCustomization? card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.DeviceID)) continue;

            PerformanceDeviceCardCustomization copy = card.Clone();
            copy.LineCount = PerformanceDeviceCardCustomization.NormalizeLineCount(copy.LineCount);
            copy.DescriptionReplacements.RemoveAll(static rule => rule == null);
            normalized.RemoveAll(existing => IsSameDevice(existing.DeviceID, copy.DeviceID));
            if (!copy.IsDefault) normalized.Add(copy);
        }

        return normalized;
    }

    /// <summary>Returns the customization stored for a device, or null when its card is the default.</summary>
    public static PerformanceDeviceCardCustomization? Find(
        IReadOnlyList<PerformanceDeviceCardCustomization> cards,
        string deviceID)
    {
        for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
        {
            if (IsSameDevice(cards[cardIndex].DeviceID, deviceID)) return cards[cardIndex];
        }

        return null;
    }

    /// <summary>Returns a copy of the list with one device's customization replaced, or removed when default.</summary>
    public static List<PerformanceDeviceCardCustomization> Replace(
        IReadOnlyList<PerformanceDeviceCardCustomization> cards,
        PerformanceDeviceCardCustomization card)
    {
        List<PerformanceDeviceCardCustomization> updated = new(cards.Count + 1);
        for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
        {
            if (!IsSameDevice(cards[cardIndex].DeviceID, card.DeviceID)) updated.Add(cards[cardIndex]);
        }

        updated.Add(card);
        return Normalize(updated);
    }

    private static bool IsSameDevice(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
