using System.Globalization;
using System.Text;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

internal sealed record SettingsSearchPageSource(string Label, StackPanel Root);

/// <summary>Indexes and filters the live controls that make up the stitched settings pages.</summary>
internal sealed class SettingsSearchView
{
    private sealed class ControlState(Control control)
    {
        public readonly Control Control = control;
        public readonly bool IsNormallyVisible = control.IsVisible;

        public void SetSearchVisible(bool isVisible) => Control.IsVisible = IsNormallyVisible && isVisible;
    }

    private sealed class SearchEntry(int id, IReadOnlyList<ControlState> controls)
    {
        public readonly int ID = id;
        public readonly IReadOnlyList<ControlState> Controls = controls;
        public bool IsNormallyVisible => Controls.Any(static state => state.IsNormallyVisible);

        public string ReadText()
        {
            List<Control> roots = new(Controls.Count);
            foreach (ControlState state in Controls)
                roots.Add(state.Control);
            return SettingsSearchTextExtractor.Read(roots);
        }

        public bool Apply(HashSet<int> matches)
        {
            bool isMatch = matches.Contains(ID) && Controls.Any(static state => state.IsNormallyVisible);
            foreach (ControlState state in Controls)
                state.SetSearchVisible(isMatch);
            return isMatch;
        }
    }

    private sealed class SearchGroup(ControlState root, IReadOnlyList<SearchEntry> entries)
    {
        public readonly ControlState Root = root;
        public readonly IReadOnlyList<SearchEntry> Entries = entries;
        public bool IsNormallyVisible => Root.IsNormallyVisible;

        public bool Apply(HashSet<int> matches)
        {
            bool hasMatch = false;
            foreach (SearchEntry entry in Entries)
                hasMatch |= entry.Apply(matches);
            Root.SetSearchVisible(hasMatch);
            return IsNormallyVisible && hasMatch;
        }
    }

    private sealed class SearchSection(ControlState? header)
    {
        public readonly ControlState? Header = header;
        public readonly List<ControlState> SupportingControls = [];
        public readonly List<SearchGroup> Groups = [];
        public readonly List<SearchEntry> ContentEntries = [];

        public string ReadContext() => SettingsSearchTextExtractor.Read(
            SupportingControls.Select(static state => state.Control));

        public bool Apply(HashSet<int> matches)
        {
            bool hasMatch = false;
            foreach (SearchGroup group in Groups)
                hasMatch |= group.Apply(matches);
            foreach (SearchEntry entry in ContentEntries)
                hasMatch |= entry.Apply(matches);
            foreach (ControlState supportingControl in SupportingControls)
                supportingControl.SetSearchVisible(hasMatch);
            Header?.SetSearchVisible(hasMatch);
            return hasMatch;
        }
    }

    private sealed class SearchPage(string label, ControlState root, ControlState? header)
    {
        public readonly string Label = label;
        public readonly ControlState Root = root;
        public readonly ControlState? Header = header;
        public readonly List<SearchSection> Sections = [];

        public string ReadHeader() => Header == null
            ? Label
            : SettingsSearchTextExtractor.Read([Header.Control]);

        public bool Apply(HashSet<int> matches)
        {
            bool hasMatch = false;
            foreach (SearchSection section in Sections)
                hasMatch |= section.Apply(matches);
            Header?.SetSearchVisible(hasMatch);
            Root.SetSearchVisible(hasMatch);
            return Root.IsNormallyVisible && hasMatch;
        }
    }

    private readonly TextBlock _statusText;
    private readonly string _findingMatchesText;
    private readonly string _noMatchesFormat;
    private readonly List<SearchPage> _pages = [];
    private int _nextEntryID;

    public SettingsSearchView(
        TextBlock statusText,
        string findingMatchesText,
        string noMatchesFormat,
        IReadOnlyList<SettingsSearchPageSource> sources)
    {
        _statusText = statusText;
        _findingMatchesText = findingMatchesText;
        _noMatchesFormat = noMatchesFormat;
        foreach (SettingsSearchPageSource source in sources)
            _pages.Add(BuildPage(source));
    }

    public IReadOnlyList<SettingsSearchDocument> ReadDocuments()
    {
        List<SettingsSearchDocument> documents = [];
        foreach (SearchPage page in _pages)
        {
            if (!page.Root.IsNormallyVisible) continue;

            string pageContext = JoinContext(page.Label, page.ReadHeader());
            foreach (SearchSection section in page.Sections)
            {
                string sectionHeader = section.Header == null
                    ? string.Empty
                    : SettingsSearchTextExtractor.Read([section.Header.Control]);
                string sectionContext = JoinContext(pageContext, sectionHeader, section.ReadContext());

                foreach (SearchGroup group in section.Groups)
                {
                    if (!group.IsNormallyVisible) continue;

                    foreach (SearchEntry entry in group.Entries)
                    {
                        if (!entry.IsNormallyVisible) continue;
                        string primaryText = entry.ReadText();
                        if (!string.IsNullOrWhiteSpace(primaryText))
                            documents.Add(new SettingsSearchDocument(entry.ID, primaryText, sectionContext));
                    }
                }

                foreach (SearchEntry entry in section.ContentEntries)
                {
                    if (!entry.IsNormallyVisible) continue;
                    string primaryText = entry.ReadText();
                    if (!string.IsNullOrWhiteSpace(primaryText))
                        documents.Add(new SettingsSearchDocument(entry.ID, primaryText, sectionContext));
                }
            }
        }

        return documents;
    }

    public void ApplyMatches(HashSet<int> matches, string query, bool isFinal)
    {
        bool hasAnyMatch = false;
        foreach (SearchPage page in _pages)
            hasAnyMatch |= page.Apply(matches);

        _statusText.IsVisible = !hasAnyMatch;
        if (hasAnyMatch) return;

        _statusText.Text = isFinal
            ? string.Format(CultureInfo.CurrentCulture, _noMatchesFormat, query)
            : _findingMatchesText;
    }

    private SearchPage BuildPage(SettingsSearchPageSource source)
    {
        ControlState root = new(source.Root);
        ControlState? pageHeader = null;
        SearchSection currentSection = new(null);
        List<SearchSection> sections = [currentSection];

        foreach (Control child in source.Root.Children)
        {
            SettingsSearchRole role = SettingsSearchMetadata.GetRole(child);
            switch (role)
            {
                case SettingsSearchRole.PageHeader:
                    pageHeader = new ControlState(child);
                    break;

                case SettingsSearchRole.SubsectionHeader:
                    currentSection = new SearchSection(new ControlState(child));
                    sections.Add(currentSection);
                    break;

                default:
                    AddSectionControl(currentSection, child);
                    break;
            }
        }

        SearchPage page = new(source.Label, root, pageHeader);
        foreach (SearchSection section in sections)
            page.Sections.Add(section);
        FinalizeSections(page);
        return page;
    }

    private void AddSectionControl(SearchSection section, Control control)
    {
        List<Control> cards = FindCards(control);
        if (cards.Count == 0)
        {
            section.SupportingControls.Add(new ControlState(control));
            return;
        }

        List<SearchEntry> entries = new(cards.Count);
        foreach (Control card in cards)
            entries.Add(new SearchEntry(_nextEntryID++, [new ControlState(card)]));
        section.Groups.Add(new SearchGroup(new ControlState(control), entries));
    }

    private void FinalizeSections(SearchPage page)
    {
        foreach (SearchSection section in page.Sections)
        {
            if (section.Groups.Count > 0 || section.SupportingControls.Count == 0) continue;

            SearchEntry contentEntry = new(_nextEntryID++, [.. section.SupportingControls]);
            section.ContentEntries.Add(contentEntry);
            section.SupportingControls.Clear();
        }
    }

    private static List<Control> FindCards(Control root)
    {
        List<Control> cards = [];
        List<Control> pending = [root];
        HashSet<Control> visited = new(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            int lastIndex = pending.Count - 1;
            Control control = pending[lastIndex];
            pending.RemoveAt(lastIndex);
            if (!visited.Add(control)) continue;

            if (SettingsSearchMetadata.GetRole(control) == SettingsSearchRole.Card)
            {
                cards.Add(control);
                continue;
            }

            SettingsSearchTextExtractor.AddChildren(control, pending);
        }

        return cards;
    }

    private static string JoinContext(params string[] values) =>
        string.Join(". ", values.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
}

internal static class SettingsSearchTextExtractor
{
    public static string Read(IEnumerable<Control> roots)
    {
        List<Control> orderedRoots = [];
        foreach (Control root in roots)
            orderedRoots.Add(root);

        List<Control> pending = new(orderedRoots.Count);
        for (int rootIndex = orderedRoots.Count - 1; rootIndex >= 0; rootIndex--)
            pending.Add(orderedRoots[rootIndex]);

        HashSet<Control> visited = new(ReferenceEqualityComparer.Instance);
        HashSet<string> seenValues = new(StringComparer.OrdinalIgnoreCase);
        List<string> values = [];
        while (pending.Count > 0)
        {
            int lastIndex = pending.Count - 1;
            Control control = pending[lastIndex];
            pending.RemoveAt(lastIndex);
            if (!visited.Add(control)) continue;

            switch (control)
            {
                case TextBlock { Text: { } text } when !string.IsNullOrWhiteSpace(text):
                    AddValue(text, seenValues, values);
                    break;

                case TextBox { Text: { } text } when !string.IsNullOrWhiteSpace(text):
                    AddValue(text, seenValues, values);
                    break;

                case ContentControl { Content: string content } when !string.IsNullOrWhiteSpace(content):
                    AddValue(content, seenValues, values);
                    break;
            }

            AddChildren(control, pending);
        }

        StringBuilder result = new();
        foreach (string value in values)
        {
            if (result.Length > 0)
                result.Append(". ");
            result.Append(value);
        }

        return result.ToString();
    }

    private static void AddValue(string value, HashSet<string> seenValues, List<string> values)
    {
        string trimmed = value.Trim();
        if (seenValues.Add(trimmed))
            values.Add(trimmed);
    }

    public static void AddChildren(Control control, List<Control> pending)
    {
        switch (control)
        {
            case Panel panel:
                for (int childIndex = panel.Children.Count - 1; childIndex >= 0; childIndex--)
                    pending.Add(panel.Children[childIndex]);
                break;

            case Decorator { Child: not null } decorator:
                pending.Add(decorator.Child);
                break;

            case ContentControl { Content: Control child }:
                pending.Add(child);
                break;

            case ItemsControl itemsControl:
                for (int itemIndex = itemsControl.Items.Count - 1; itemIndex >= 0; itemIndex--)
                {
                    object? item = itemsControl.Items[itemIndex];
                    if (item is Control itemControl)
                        pending.Add(itemControl);
                }
                break;
        }
    }
}
