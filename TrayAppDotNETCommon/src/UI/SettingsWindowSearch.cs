using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace TrayAppDotNETCommon.UI;

public abstract partial class SettingsWindowCommon<TPageKey>
    where TPageKey : notnull
{
    private SettingsSearchBox? _settingsSearchBox;
    private SettingsSearchView? _settingsSearchView;
    private string _settingsSearchQuery = string.Empty;
    private bool _isShowingSettingsSearch;
    private bool _suppressSettingsSearchTextChanged;

    private void NavigateToSettingsPage(TPageKey key)
    {
        bool wasShowingSearch = _isShowingSettingsSearch;
        SettingsSearchView? previousView = _settingsSearchView;
        string previousQuery = _settingsSearchQuery;
        _isShowingSettingsSearch = false;
        _settingsSearchView = null;
        _settingsSearchQuery = string.Empty;
        SetSearchBoxText(string.Empty);

        try
        {
            ShowPage(key, force: wasShowingSearch);
        }
        catch
        {
            _isShowingSettingsSearch = wasShowingSearch;
            _settingsSearchView = previousView;
            _settingsSearchQuery = previousQuery;
            SetSearchBoxText(previousQuery);
            throw;
        }
    }

    private void OnSettingsSearchTextChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressSettingsSearchTextChanged || IsClosing) return;

        string query = _settingsSearchBox?.SearchText.Trim() ?? string.Empty;
        _settingsSearchQuery = query;
        if (query.Length == 0)
        {
            ExitSettingsSearch();
            return;
        }

        RunSettingsSearch(query);
    }

    private void RunSettingsSearch(string query)
    {
        try
        {
            SettingsSearchView view = EnsureSettingsSearchView();
            IReadOnlyList<SettingsSearchDocument> documents = view.ReadDocuments();
            HashSet<int> matches = SettingsSearchScorer.FindMatches(query, documents, view.SynonymMap);
            view.ApplyMatches(matches, query);
            _scrollHost?.SetVerticalOffset(0);
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"{GetType().Name} settings search view failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return;
        }
    }

    private SettingsSearchView EnsureSettingsSearchView()
    {
        if (_isShowingSettingsSearch && _settingsSearchView != null)
            return _settingsSearchView;

        if (_hasShownPage && _scrollHost != null)
            _pageScrollOffsets[CurrentPageKey] = _scrollHost.VerticalOffset;

        UIContentGeneration? previousGeneration = _pageGeneration;
        Dictionary<TPageKey, bool> previousNavSelections = [];
        foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
            previousNavSelections[navKey] = item.IsSelected;

        (UIContentGeneration generation, SettingsSearchView view) replacement = BuildSettingsSearchGeneration();
        try
        {
            _content.Content = replacement.generation.Root;
            _pageGeneration = replacement.generation;
            _settingsSearchView = replacement.view;
            _isShowingSettingsSearch = true;
            foreach (SettingsNavItem item in _navItems.Values)
                item.IsSelected = false;
        }
        catch (Exception exception)
        {
            RestorePageCommitState(previousGeneration?.Root, previousNavSelections, exception);
            replacement.generation.Dispose();
            throw;
        }

        previousGeneration?.Dispose();
        _scrollHost?.SetVerticalOffset(0);
        return replacement.view;
    }

    private (UIContentGeneration generation, SettingsSearchView view) BuildSettingsSearchGeneration()
    {
        UIResourceScope resources = new($"{GetType().Name}.Search");
        _buildingPageResources = resources;
        try
        {
            SettingsPalette palette = Palette;
            StackPanel root = TrayAppDotNETSettingsCards.PageStack(
                L(nameof(CommonStrings.SettingsWindow_SearchResults)),
                palette);
            TextBlock status = TrayAppDotNETSettingsUI.DescriptionText(string.Empty, palette);
            status.IsVisible = false;
            root.Children.Add(status);

            List<SettingsSearchPageSource> sources = new(_pageDescriptors.Count);
            foreach (SettingsPageDescriptor<TPageKey> descriptor in _pageDescriptors)
            {
                Control builtPage = descriptor.BuildPage();
                StackPanel pageRoot;
                if (builtPage is StackPanel stackPanel)
                {
                    pageRoot = stackPanel;
                }
                else
                {
                    pageRoot = TrayAppDotNETSettingsCards.PageStack(descriptor.Label, palette);
                    pageRoot.Children.Add(builtPage);
                }

                root.Children.Add(pageRoot);
                sources.Add(new SettingsSearchPageSource(descriptor.Label, pageRoot));
            }

            OwnDisposablePageControls(root, resources);
            string commonSynonymGroups = CommonStrings.SettingsWindow_SearchSynonymGroups_Common;
            SettingsSearchView view = new(
                status,
                L(nameof(CommonStrings.SettingsWindow_SearchNoMatchesFormat)),
                sources,
                SettingsSearchSynonymMap.Parse(
                    commonSynonymGroups,
                    L(SettingsSearchSynonymMap.AppResourceKey)));
            UIContentGeneration generation = new($"{GetType().Name}.Search", root, resources);
            return (generation, view);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
        finally
        {
            _buildingPageResources = null;
        }
    }

    private void ExitSettingsSearch()
    {
        if (!_isShowingSettingsSearch) return;

        SettingsSearchView? previousView = _settingsSearchView;
        _settingsSearchView = null;
        _isShowingSettingsSearch = false;
        try
        {
            ShowPage(CurrentPageKey, force: true);
        }
        catch
        {
            _settingsSearchView = previousView;
            _isShowingSettingsSearch = true;
            throw;
        }
    }

    private void RestoreSettingsSearchAfterShellRebuild(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        _settingsSearchQuery = query;
        SetSearchBoxText(query);
        RunSettingsSearch(query);
    }

    private void SetSearchBoxText(string text)
    {
        SettingsSearchBox? searchBox = _settingsSearchBox;
        if (searchBox == null || string.Equals(searchBox.SearchText, text, StringComparison.Ordinal)) return;

        _suppressSettingsSearchTextChanged = true;
        try
        {
            searchBox.SearchText = text;
        }
        finally
        {
            _suppressSettingsSearchTextChanged = false;
        }
    }

    private void DisposeSettingsSearch()
    {
        _settingsSearchView = null;
        _settingsSearchBox = null;
        _settingsSearchQuery = string.Empty;
        _isShowingSettingsSearch = false;
    }
}
