using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Editing;
using Throng.Core.Model;
using Throng.Render.Gl;

namespace Throng.App.ViewModels;

/// <summary>
/// Saved named views (round 016, D-276, format v10): STUDY ▸ VIEW ▸ SAVE VIEW stores the mode and level, camera, ISO
/// preset, clip, section box and explode in <see cref="Study.Views"/>; clicking a row restores it; names edit in place;
/// DELETE removes it. Saving, renaming and deleting are undoable. They don't touch the simulation, so they work while
/// running. The active view's name goes in the PNG title block.
/// </summary>
public sealed partial class MainViewModel
{
    private Guid? _activeViewId;
    private bool _restoringView;
    private RelayCommand? _saveViewCommand;

    /// <summary>Copies the viewport's camera into a view (set by the main window).</summary>
    public Action<SavedView>? WriteCamera { get; set; }

    /// <summary>Puts a view's camera back on the viewport (set by the main window).</summary>
    public Action<SavedView>? ReadCamera { get; set; }

    /// <summary>The study's saved views.</summary>
    public ObservableCollection<SavedViewRowViewModel> ViewRows { get; } = [];

    /// <summary>True when the study has no saved views.</summary>
    public bool HasNoViews => ViewRows.Count == 0;

    /// <summary>VIEW ▸ SAVE VIEW.</summary>
    public RelayCommand SaveViewCommand => _saveViewCommand ??= new RelayCommand(SaveView);

    /// <summary>Name of the view last restored or saved while nothing has changed since, or null.</summary>
    public string? ActiveViewName => _activeViewId is { } id ? _study.Views.FirstOrDefault(v => v.Id == id)?.Name : null;

    private void SaveView()
    {
        var names = _study.Views.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = "View 1";
        for (int n = 2; names.Contains(name); n++)
        {
            name = string.Create(CultureInfo.InvariantCulture, $"View {n}");
        }

        var view = new SavedView
        {
            Name = name,
            Mode = _viewMode switch { ViewMode.Plan => "Plan", ViewMode.Iso => "Iso", _ => "Orbit" },
            LevelId = ActiveLevel?.Id,
            IsoPreset = _isoPreset,
            ClipEnabled = _clipEnabled,
            ClipLevelId = _clipLevelId,
            SectionBox = _sectionBox,
            Box = [_boxWest, _boxEast, _boxSouth, _boxNorth, _boxBottom, _boxTop],
            ExplodeM = _explodeM,
        };
        WriteCamera?.Invoke(view);
        Execute(new AddItemsAction<SavedView>($"Save view {name}", _study.Views, [view], OnModelChanged));
        _activeViewId = view.Id;
        RefreshViewRows();
        Ticker = $"SAVED ▸ {name} ▸ click it in STUDY ▸ VIEW to come back here; click the name to rename it.";
    }

    /// <summary>Restores a saved view: mode, level, ISO preset, cutaway, explode, then the camera.</summary>
    public void RestoreView(SavedViewRowViewModel row)
    {
        SavedView view = row.View;
        _restoringView = true;
        try
        {
            ViewMode = view.Mode switch { "Plan" => ViewMode.Plan, "Iso" => ViewMode.Iso, _ => ViewMode.Orbit };
            if (view.LevelId is { } levelId && Levels.FirstOrDefault(l => l.Id == levelId) is { } level && !ReferenceEquals(level, ActiveLevel))
            {
                ActiveLevel = level;
            }

            IsoPreset = view.IsoPreset;
            _clipEnabled = view.ClipEnabled;
            _clipLevelId = view.ClipLevelId;
            _sectionBox = view.SectionBox;
            double[] box = view.Box is { Length: 6 } b ? b : [0, 100, 0, 100, 0, 100];
            (_boxWest, _boxEast, _boxSouth, _boxNorth, _boxBottom, _boxTop) = (box[0], box[1], box[2], box[3], box[4], box[5]);
            _explodeM = Math.Clamp(view.ExplodeM, 0, MaxExplodeM);
            OnPropertyChanged(nameof(ClipEnabled));
            OnPropertyChanged(nameof(ClipLevel));
            OnPropertyChanged(nameof(SectionBoxEnabled));
            OnPropertyChanged(nameof(BoxWest));
            OnPropertyChanged(nameof(BoxEast));
            OnPropertyChanged(nameof(BoxSouth));
            OnPropertyChanged(nameof(BoxNorth));
            OnPropertyChanged(nameof(BoxBottom));
            OnPropertyChanged(nameof(BoxTop));
            OnPropertyChanged(nameof(ExplodeM));
            OnPropertyChanged(nameof(ExplodeText));
            OnPropertyChanged(nameof(ViewSettings));
            ReadCamera?.Invoke(view);
        }
        finally
        {
            _restoringView = false;
        }

        _activeViewId = view.Id;
        MarkActiveView();
        Ticker = $"VIEW ▸ {view.Name}";
    }

    /// <summary>Renames a view (undoable); empty names are refused.</summary>
    public void RenameView(SavedViewRowViewModel row, string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            Ticker = "A view needs a name.";
            row.Refresh();
            return;
        }

        SavedView view = row.View;
        Apply("Rename view", () => view.Name, v => view.Name = v, trimmed);
    }

    /// <summary>Deletes a view (undoable).</summary>
    public void DeleteView(SavedViewRowViewModel row)
    {
        Execute(new RemoveItemsAction<SavedView>($"Delete view {row.View.Name}", _study.Views, [row.View], OnModelChanged));
        if (_activeViewId == row.View.Id)
        {
            _activeViewId = null;
        }
    }

    /// <summary>Called when the user changes the view: it's no longer the saved one.</summary>
    private void ViewSettingsChanged()
    {
        OnPropertyChanged(nameof(ViewSettings));
        if (!_restoringView && _activeViewId is not null)
        {
            _activeViewId = null;
            MarkActiveView();
        }
    }

    /// <summary>Rows follow the study's list (after save, delete, undo, a new study).</summary>
    private void RefreshViewRows()
    {
        if (!_study.Views.SequenceEqual(ViewRows.Select(r => r.View)))
        {
            ViewRows.Clear();
            foreach (SavedView view in _study.Views)
            {
                ViewRows.Add(new SavedViewRowViewModel(view, this));
            }

            OnPropertyChanged(nameof(HasNoViews));
        }
        else
        {
            foreach (SavedViewRowViewModel row in ViewRows)
            {
                row.Refresh();
            }
        }

        MarkActiveView();
    }

    private void MarkActiveView()
    {
        foreach (SavedViewRowViewModel row in ViewRows)
        {
            row.IsActive = row.View.Id == _activeViewId;
        }
    }
}
