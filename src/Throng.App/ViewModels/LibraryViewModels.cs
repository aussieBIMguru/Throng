using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// Library browser (D-021, D-031): a tab strip by category over one filtered list, plus a search box that matches
/// across every category. Each tab shows its match count. Read-only until M3.
/// </summary>
public sealed class LibraryBrowserViewModel : ObservableObject
{
    private LibraryTabViewModel _selectedTab;
    private string _searchText = string.Empty;

    /// <summary>Builds the browser over a library.</summary>
    public LibraryBrowserViewModel(TemplateLibrary library)
    {
        Tabs =
        [
            new LibraryTabViewModel("AGENTS", library.AgentProfiles),
            new LibraryTabViewModel("DEVICES", library.Devices),
            new LibraryTabViewModel("VERTICAL", library.Connectors),
            new LibraryTabViewModel("LIFTS", library.Lifts),
            new LibraryTabViewModel("SCHEDULES", library.Schedules),
        ];
        _selectedTab = Tabs[0];
        Refilter();
    }

    /// <summary>Category tabs in catalogue order.</summary>
    public IReadOnlyList<LibraryTabViewModel> Tabs { get; }

    /// <summary>Tab whose (filtered) items are listed.</summary>
    public LibraryTabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is not null && SetProperty(ref _selectedTab, value))
            {
                RefreshVisible();
            }
        }
    }

    /// <summary>Search text: every word must appear in the name, summary or description (case-insensitive).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsSearching));
                Refilter();
            }
        }
    }

    /// <summary>True while a search is active.</summary>
    public bool IsSearching => _searchText.Trim().Length > 0;

    /// <summary>Items shown under the tab strip.</summary>
    public ObservableCollection<TemplateItemViewModel> VisibleItems { get; } = [];

    /// <summary>Shown when the selected tab has nothing to list.</summary>
    public string EmptyText => IsSearching ? $"No matches in {_selectedTab.Label}." : "Nothing here yet.";

    /// <summary>True when <see cref="VisibleItems"/> is empty.</summary>
    public bool IsEmpty => VisibleItems.Count == 0;

    private void Refilter()
    {
        string[] words = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (LibraryTabViewModel tab in Tabs)
        {
            tab.ApplyFilter(words);
        }

        // If the current tab has nothing but another does, jump to the first tab with matches.
        if (_selectedTab.MatchCount == 0 && Tabs.FirstOrDefault(t => t.MatchCount > 0) is { } withMatches)
        {
            _selectedTab = withMatches;
            OnPropertyChanged(nameof(SelectedTab));
        }

        RefreshVisible();
    }

    private void RefreshVisible()
    {
        VisibleItems.Clear();
        foreach (TemplateItemViewModel item in _selectedTab.Matches)
        {
            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}

/// <summary>One tab of the library browser with its current search matches.</summary>
public sealed class LibraryTabViewModel : ObservableObject
{
    private IReadOnlyList<TemplateItemViewModel> _matches;

    /// <summary>Creates a tab over a category's templates.</summary>
    public LibraryTabViewModel(string label, IEnumerable<TemplateBase> templates)
    {
        Label = label;
        Items = [.. templates.Select(t => new TemplateItemViewModel(t))];
        _matches = Items;
    }

    /// <summary>Tab label (VT323 caps).</summary>
    public string Label { get; }

    /// <summary>All templates in the category.</summary>
    public IReadOnlyList<TemplateItemViewModel> Items { get; }

    /// <summary>Templates matching the current search (all of them when not searching).</summary>
    public IReadOnlyList<TemplateItemViewModel> Matches => _matches;

    /// <summary>Number of matches.</summary>
    public int MatchCount => _matches.Count;

    /// <summary>Count shown on the tab.</summary>
    public string CountText => MatchCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Re-filters against the search words (empty = everything).</summary>
    public void ApplyFilter(IReadOnlyList<string> words)
    {
        _matches = words.Count == 0 ? Items : [.. Items.Where(i => i.Matches(words))];
        OnPropertyChanged(nameof(Matches));
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>One template row in the library browser.</summary>
public sealed class TemplateItemViewModel
{
    /// <summary>Wraps a template.</summary>
    public TemplateItemViewModel(TemplateBase template)
    {
        Template = template;
        Swatch = CreateBrush(template.ColorHex);
    }

    /// <summary>The template.</summary>
    public TemplateBase Template { get; }

    /// <summary>Display name.</summary>
    public string Name => Template.Name;

    /// <summary>Key values on one line.</summary>
    public string Summary => Template.Summarise();

    /// <summary>Long description for the tooltip.</summary>
    public string Description => Template.Description;

    /// <summary>Where the template came from.</summary>
    public string SourceText => Template.Source == TemplateSource.BuiltIn ? "Built-in · read-only" : Template.Source.ToString();

    /// <summary>Colour swatch.</summary>
    public Brush Swatch { get; }

    /// <summary>True when every word appears in the name, summary or description.</summary>
    public bool Matches(IReadOnlyList<string> words)
    {
        foreach (string word in words)
        {
            if (!Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !Summary.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !Description.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static Brush CreateBrush(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
            // Fall through to the neutral swatch.
        }

        return Brushes.Gray;
    }
}
