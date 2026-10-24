using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// One row in the levels list and the source of the level property panel. Every setter routes through the
/// undo stack via <see cref="MainViewModel.Apply{T}"/>; getters read straight from the model.
/// </summary>
public sealed class LevelViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps a level model.</summary>
    public LevelViewModel(Level model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    /// <summary>The wrapped level.</summary>
    public Level Model { get; }

    /// <summary>Level id.</summary>
    public Guid Id => Model.Id;

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A level needs a name.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rename level", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>Elevation in metres.</summary>
    public double ElevationM
    {
        get => Model.ElevationM;
        set => _owner.Apply("Change level elevation", () => Model.ElevationM, v => Model.ElevationM = v, Math.Round(value, 3));
    }

    /// <summary>Floor-to-floor height in metres (must be positive).</summary>
    public double HeightM
    {
        get => Model.HeightM;
        set
        {
            if (value <= 0)
            {
                _owner.Ticker = "Level height must be greater than zero.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Change level height", () => Model.HeightM, v => Model.HeightM = v, Math.Round(value, 3));
        }
    }

    /// <summary>Shown in views.</summary>
    public bool IsVisible
    {
        get => Model.IsVisible;
        set => _owner.Apply(value ? "Show level" : "Hide level", () => Model.IsVisible, v => Model.IsVisible = v, value);
    }

    /// <summary>Locked against editing.</summary>
    public bool IsLocked
    {
        get => Model.IsLocked;
        set => _owner.Apply(value ? "Lock level" : "Unlock level", () => Model.IsLocked, v => Model.IsLocked = v, value);
    }

    /// <summary>Elevation formatted for the list, e.g. "−5.00 m".</summary>
    public string ElevationText => Model.ElevationM.ToString("+0.00;−0.00;0.00", CultureInfo.InvariantCulture) + " m";

    /// <summary>Number of regions on the level.</summary>
    public int RegionCount => _owner.Study.RegionsOn(Model.Id).Count();

    /// <summary>Secondary line for the list.</summary>
    public string Summary => $"{ElevationText} · {RegionCount} regions";

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
