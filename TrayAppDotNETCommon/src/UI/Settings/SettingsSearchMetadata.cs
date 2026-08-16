using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

internal enum SettingsSearchRole
{
    None,
    PageHeader,
    SubsectionHeader,
    Card
}

/// <summary>Associates code-created settings controls with their search structure.</summary>
internal static class SettingsSearchMetadata
{
    private sealed class MetadataHolder
    {
        public SettingsSearchRole Role;
        public string TitleText = string.Empty;
        public readonly List<string> SearchKeywords = [];
    }

    private static readonly ConditionalWeakTable<Control, MetadataHolder> Metadata = new();

    public static T Mark<T>(T control, SettingsSearchRole role)
        where T : Control
    {
        MetadataHolder holder = Metadata.GetOrCreateValue(control);
        holder.Role = role;
        return control;
    }

    public static T MarkCard<T>(
        T control,
        string titleText,
        IReadOnlyList<string>? searchKeywords = null)
        where T : Control
    {
        MetadataHolder holder = Metadata.GetOrCreateValue(control);
        holder.Role = SettingsSearchRole.Card;
        holder.TitleText = titleText;
        AddSearchKeywords(holder, searchKeywords);
        return control;
    }

    public static T AddSearchKeywords<T>(T control, IReadOnlyList<string>? searchKeywords)
        where T : Control
    {
        MetadataHolder holder = Metadata.GetOrCreateValue(control);
        AddSearchKeywords(holder, searchKeywords);
        return control;
    }

    public static SettingsSearchRole GetRole(Control control) =>
        Metadata.TryGetValue(control, out MetadataHolder? holder) ? holder.Role : SettingsSearchRole.None;

    public static string GetTitleText(Control control) =>
        Metadata.TryGetValue(control, out MetadataHolder? holder) ? holder.TitleText : string.Empty;

    public static IReadOnlyList<string> GetSearchKeywords(Control control) =>
        Metadata.TryGetValue(control, out MetadataHolder? holder)
            ? holder.SearchKeywords
            : Array.Empty<string>();

    private static void AddSearchKeywords(MetadataHolder holder, IReadOnlyList<string>? searchKeywords)
    {
        if (searchKeywords == null) return;

        foreach (string searchKeyword in searchKeywords)
        {
            if (string.IsNullOrWhiteSpace(searchKeyword)) continue;
            if (holder.SearchKeywords.Contains(searchKeyword, StringComparer.OrdinalIgnoreCase)) continue;
            holder.SearchKeywords.Add(searchKeyword);
        }
    }
}
