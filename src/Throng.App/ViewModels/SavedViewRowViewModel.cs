using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>One row of STUDY ▸ VIEW ▸ VIEWS (round 016, D-276): click to restore, edit the name, DELETE.</summary>
public sealed class SavedViewRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isActive;

    /// <summary>Creates a row over a saved view.</summary>
    public SavedViewRowViewModel(SavedView view, MainViewModel owner)
    {
        View = view;
        _owner = owner;
        RestoreCommand = new RelayCommand(() => _owner.RestoreView(this));
        DeleteCommand = new RelayCommand(() => _owner.DeleteView(this));
    }

    /// <summary>The saved view.</summary>
    public SavedView View { get; }

    /// <summary>Name (undoable rename on commit).</summary>
    public string Name
    {
        get => View.Name;
        set => _owner.RenameView(this, value);
    }

    /// <summary>One line: mode, cut and explode.</summary>
    public string Summary
    {
        get
        {
            string mode = View.Mode switch
            {
                "Plan" => "PLAN",
                "Iso" => "ISO " + Throng.Render.Cameras.OrbitCamera.IsoPresetNames[((View.IsoPreset % 4) + 4) % 4],
                _ => "3D",
            };
            var parts = new List<string> { mode };
            if (View.ClipEnabled)
            {
                parts.Add("clip");
            }

            if (View.SectionBox)
            {
                parts.Add("box");
            }

            if (View.ExplodeM > 0)
            {
                parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"+{View.ExplodeM:0.#} m"));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The view last restored or saved, until the view is changed.</summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    /// <summary>Restore the view.</summary>
    public RelayCommand RestoreCommand { get; }

    /// <summary>Delete the view (undoable).</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Re-reads name and summary after an undo or rename.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
    }
}
