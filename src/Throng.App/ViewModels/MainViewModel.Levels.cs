using CommunityToolkit.Mvvm.Input;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>Adding and deleting levels from the LEVELS panel (D-142, D-143).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Adds a level above the active one (splits the storey when there's a level above).</summary>
    public RelayCommand AddLevelAboveCommand { get; private set; } = null!;

    /// <summary>Adds a level below the active one (splits the storey when there's a level below).</summary>
    public RelayCommand AddLevelBelowCommand { get; private set; } = null!;

    /// <summary>Deletes the active level and everything on it, after a confirm.</summary>
    public RelayCommand DeleteLevelCommand { get; private set; } = null!;

    private void InitialiseLevels()
    {
        AddLevelAboveCommand = new RelayCommand(() => AddLevel(above: true));
        AddLevelBelowCommand = new RelayCommand(() => AddLevel(above: false));
        DeleteLevelCommand = new RelayCommand(DeleteLevel);
    }

    private void AddLevel(bool above)
    {
        if (!CanEditLevels())
        {
            return;
        }

        Result<Level> planned = LevelActions.Plan(_study, _activeLevel!.Model, above);
        if (!planned.IsSuccess || planned.Value is not { } level)
        {
            Ticker = planned.Error ?? "The level couldn't be added.";
            return;
        }

        Execute(LevelActions.Add(_study, level, OnModelChanged));
        if (Levels.FirstOrDefault(v => v.Id == level.Id) is { } row)
        {
            ActiveLevel = row;
        }
    }

    private void DeleteLevel()
    {
        if (!CanEditLevels())
        {
            return;
        }

        Level level = _activeLevel!.Model;
        if (level.IsLocked)
        {
            Ticker = $"'{level.Name}' is locked ▸ unlock it to delete it.";
            return;
        }

        Result<IUndoableAction> delete = LevelActions.Delete(_study, level, OnModelChanged);
        if (!delete.IsSuccess || delete.Value is not { } action)
        {
            Ticker = delete.Error ?? "The level couldn't be deleted.";
            return;
        }

        LevelContents contents = LevelActions.ContentsOf(_study, level);
        string message = contents.Total == 0
            ? $"Delete level '{level.Name}'? It's empty."
            : $"Delete level '{level.Name}' and everything on it ({contents.Describe()})? Anything on other levels that links to it is flagged by CHECK. Undo brings it all back.";
        if (!_dialogs.Confirm("Delete level", message, "DELETE"))
        {
            return;
        }

        Editor.Reset();
        Execute(action);
    }

    private bool CanEditLevels()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return false;
        }

        if (_activeLevel is null)
        {
            Ticker = "Pick a level first.";
            return false;
        }

        return true;
    }
}
