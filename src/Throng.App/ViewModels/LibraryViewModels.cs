using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// Library browser (D-021, D-031): a tab strip by category over one filtered list, plus a search box that matches
/// across every category. Each tab shows its match count. The JOURNEYS tab also lists the study's own journeys
/// (D-161), and journey rows can be selected for SELECTION ▸ JOURNEY (D-162).
/// </summary>
public sealed class LibraryBrowserViewModel : ObservableObject
{
    private LibraryTabViewModel _selectedTab;
    private string _searchText = string.Empty;
    private Guid? _selectedId;

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
            new LibraryTabViewModel("JOURNEYS", library.Journeys),
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
                OnPropertyChanged(nameof(IsJourneysTabSelected));
                RefreshVisible();
            }
        }
    }

    /// <summary>
    /// True while JOURNEYS is showing: its rows stay clickable during a run, so a journey's graph (and its run overlay,
    /// round 019) can be opened read-only.
    /// </summary>
    public bool IsJourneysTabSelected => ReferenceEquals(_selectedTab, JourneysTab);

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

    /// <summary>The JOURNEYS tab.</summary>
    public LibraryTabViewModel JourneysTab => Tabs[^1];

    /// <summary>Replaces the AGENTS tab's rows with the study's profiles (embedded and imported, then built-ins; D-275).</summary>
    public void SetProfiles(IEnumerable<AgentProfileTemplate> profiles) => SetTab(Tabs[0], profiles);

    /// <summary>Replaces the DEVICES tab's rows with the study's device definitions (its own and copies first; round 017).</summary>
    public void SetDevices(IEnumerable<DeviceTemplate> devices) => SetTab(Tabs[1], devices);

    /// <summary>
    /// Sets the small note after each row's name (round 017): MODIFIED, YOURS, IMPORTED or nothing, from
    /// <paramref name="note"/>.
    /// </summary>
    public void RefreshNotes(Func<TemplateBase, string?> note)
    {
        foreach (LibraryTabViewModel tab in Tabs)
        {
            foreach (TemplateItemViewModel item in tab.Items)
            {
                item.Note = note(item.Template);
            }
        }
    }

    private void SetTab(LibraryTabViewModel tab, IEnumerable<TemplateBase> templates)
    {
        List<TemplateBase> next = [.. templates];
        if (next.Count == tab.Items.Count && next.Zip(tab.Items).All(pair => ReferenceEquals(pair.First, pair.Second.Template)))
        {
            // Same definitions: re-read names, summaries and swatches (one was edited in place).
            foreach (TemplateItemViewModel item in tab.Items)
            {
                item.Refresh();
            }

            return;
        }

        tab.SetItems(next);
        MarkSelected(_selectedId);
        Refilter();
    }

    /// <summary>
    /// Replaces the JOURNEYS tab's rows (the study's own journeys first, then the built-ins it hasn't embedded) and
    /// re-applies the search.
    /// </summary>
    public void SetJourneys(IEnumerable<JourneyTemplate> journeys)
    {
        List<JourneyTemplate> next = [.. journeys];
        if (next.Count == JourneysTab.Items.Count && next.Zip(JourneysTab.Items).All(pair => ReferenceEquals(pair.First, pair.Second.Template)))
        {
            // Same journeys: just re-read names and summaries (a step or a name was edited).
            foreach (TemplateItemViewModel item in JourneysTab.Items)
            {
                item.Refresh();
            }

            return;
        }

        JourneysTab.SetItems(next);
        MarkSelected(_selectedId);
        Refilter();
    }

    /// <summary>Highlights the row of the template with this id (null clears the highlight).</summary>
    public void MarkSelected(Guid? id)
    {
        _selectedId = id;
        foreach (LibraryTabViewModel tab in Tabs)
        {
            foreach (TemplateItemViewModel item in tab.Items)
            {
                item.IsSelected = id is { } selected && item.Template.Id == selected;
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
            OnPropertyChanged(nameof(IsJourneysTabSelected));
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
    public IReadOnlyList<TemplateItemViewModel> Items { get; private set; }

    /// <summary>Replaces the category's templates (the caller re-applies the filter).</summary>
    public void SetItems(IEnumerable<TemplateBase> templates)
    {
        Items = [.. templates.Select(t => new TemplateItemViewModel(t))];
        _matches = Items;
    }

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
public sealed class TemplateItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string? _note;
    private Brush? _swatch;
    private string _swatchHex = string.Empty;

    /// <summary>Wraps a template.</summary>
    public TemplateItemViewModel(TemplateBase template)
    {
        Template = template;
    }

    /// <summary>The template.</summary>
    public TemplateBase Template { get; }

    /// <summary>Display name.</summary>
    public string Name => Template.Name;

    /// <summary>Key values on one line.</summary>
    public string Summary => Template.Summarise();

    /// <summary>Long description for the tooltip.</summary>
    public string Description => Template.Description;

    /// <summary>Where the template came from (embedded copies of built-ins count as built-in).</summary>
    public string SourceText => Template switch
    {
        _ when Note == "MINE" => "In your library (MINE) · click to see it; using it copies it into this study · right-click to rename or delete",
        _ when Note == "DIFFERS" => "In your library, and this study's copy differs · click for UPDATE FROM MY LIBRARY · right-click to rename or delete",
        JourneyTemplate when BuiltInLibrary.Instance.Find(Template.Id) is not null || Template.Source == TemplateSource.BuiltIn => "Built-in · read-only",
        JourneyTemplate => "Your journey (in this study) · click to edit",
        DeviceTemplate or AgentProfileTemplate when Note == "MODIFIED" => "Built-in, modified in this study · click to edit",
        DeviceTemplate or AgentProfileTemplate when BuiltInLibrary.Instance.Find(Template.Id) is not null => "Built-in · click to edit for this study",
        DeviceTemplate or AgentProfileTemplate => $"{(Template.Source == TemplateSource.Imported ? "Imported" : "Yours")} (in this study) · click to edit",
        _ when Template.Source == TemplateSource.BuiltIn || BuiltInLibrary.Instance.Find(Template.Id) is not null => "Built-in · read-only",
        _ => Template.Source.ToString(),
    };

    /// <summary>Small note after the name (round 017; round 020): MINE, DIFFERS, MODIFIED, YOURS, IMPORTED, or null.</summary>
    public string? Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value))
            {
                OnPropertyChanged(nameof(HasNote));
                OnPropertyChanged(nameof(IsMine));
                OnPropertyChanged(nameof(SourceText));
            }
        }
    }

    /// <summary>True when <see cref="Note"/> is shown.</summary>
    public bool HasNote => !string.IsNullOrEmpty(_note);

    /// <summary>True for your library's items (and the study's copies of them): the row's right-click menu (round 020, D-345).</summary>
    public bool IsMine => _note is "MINE" or "DIFFERS";

    /// <summary>Re-reads the name, summary and description (after an edit).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>True for the row shown in SELECTION (journeys, D-162).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Colour swatch (follows colour edits).</summary>
    public Brush Swatch
    {
        get
        {
            if (_swatch is null || !string.Equals(_swatchHex, Template.ColorHex, StringComparison.OrdinalIgnoreCase))
            {
                _swatchHex = Template.ColorHex;
                _swatch = CreateBrush(Template.ColorHex);
            }

            return _swatch;
        }
    }

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
