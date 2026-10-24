using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// Property panel for the single selected region. Every setter routes through the undo stack via
/// <see cref="MainViewModel.Apply{T}"/>; getters read straight from the model, so undo/redo refreshes cleanly.
/// </summary>
public sealed class RegionViewModel : ObservableObject
{
    private static readonly IReadOnlyList<RegionKind> ClosedKinds = [RegionKind.Walkable, RegionKind.Obstacle, RegionKind.Zone];
    private static readonly IReadOnlyList<RegionKind> WallKinds = [RegionKind.Wall];

    private readonly MainViewModel _owner;

    /// <summary>Wraps a region model.</summary>
    public RegionViewModel(Region model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    /// <summary>The wrapped region.</summary>
    public Region Model { get; }

    private static readonly IReadOnlyList<ZoneKind> AllZoneKinds = Enum.GetValues<ZoneKind>();

    /// <summary>Zone purposes for the zone combo box (instance property so XAML can bind to it).</summary>
    public IReadOnlyList<ZoneKind> ZoneKindOptions => AllZoneKinds;

    /// <summary>
    /// Kinds this region can switch between. Closed shapes swap freely; walls stay walls because their outline is
    /// an open polyline.
    /// </summary>
    public IReadOnlyList<RegionKind> KindOptions => IsWall ? WallKinds : ClosedKinds;

    /// <summary>True for walls (thickness applies; kind is fixed).</summary>
    public bool IsWall => Model.Kind == RegionKind.Wall;

    /// <summary>True when the kind can be changed.</summary>
    public bool CanChangeKind => !IsWall;

    /// <summary>True for zones (zone purpose applies).</summary>
    public bool IsZone => Model.Kind == RegionKind.Zone;

    /// <summary>True when extrusion height is meaningful (obstacles and walls).</summary>
    public bool HasHeight => Model.Kind is RegionKind.Obstacle or RegionKind.Wall;

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A region needs a name.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rename region", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>What the region means to the simulation.</summary>
    public RegionKind Kind
    {
        get => Model.Kind;
        set
        {
            if (IsWall || value == RegionKind.Wall)
            {
                OnPropertyChanged();
                return;
            }

            _owner.Apply($"Make {Model.Name} {value.ToString().ToLowerInvariant()}", () => Model.Kind, v => Model.Kind = v, value);
        }
    }

    /// <summary>Zone purpose (zones only).</summary>
    public ZoneKind ZoneKind
    {
        get => Model.ZoneKind;
        set => _owner.Apply("Change zone purpose", () => Model.ZoneKind, v => Model.ZoneKind = v, value);
    }

    /// <summary>Extrusion height in metres (must be positive).</summary>
    public double HeightM
    {
        get => Model.HeightM;
        set
        {
            if (!(value > 0))
            {
                _owner.Ticker = "Height must be greater than zero.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Change region height", () => Model.HeightM, v => Model.HeightM = v, Math.Round(value, 3));
        }
    }

    /// <summary>Wall thickness in metres (must be positive).</summary>
    public double ThicknessM
    {
        get => Model.ThicknessM;
        set
        {
            if (!(value > 0))
            {
                _owner.Ticker = "Wall thickness must be greater than zero.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Change wall thickness", () => Model.ThicknessM, v => Model.ThicknessM = v, Math.Round(value, 3));
        }
    }

    /// <summary>Speed multiplier (0.05–2; 1 = no effect).</summary>
    public double SpeedFactor
    {
        get => Model.SpeedFactor;
        set
        {
            if (value is < 0.05 or > 2.0 || double.IsNaN(value))
            {
                _owner.Ticker = "Speed factor must be between 0.05 and 2.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Change speed factor", () => Model.SpeedFactor, v => Model.SpeedFactor = v, Math.Round(value, 3));
        }
    }

    /// <summary>Free-text tag ("Void" marks a floor opening). Blank clears it.</summary>
    public string Tag
    {
        get => Model.Tag ?? string.Empty;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            _owner.Apply("Change region tag", () => Model.Tag, v => Model.Tag = v, trimmed.Length == 0 ? null : trimmed);
        }
    }

    /// <summary>Area (closed shapes) or length (walls) readout.</summary>
    public string SizeText
    {
        get
        {
            if (IsWall)
            {
                double length = 0;
                for (int i = 0; i + 1 < Model.Outline.Count; i++)
                {
                    length += Point2.Distance(Model.Outline[i], Model.Outline[i + 1]);
                }

                return string.Create(CultureInfo.InvariantCulture, $"{length:0.00} m long");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{PolygonMath.Area(Model.Outline):0.00} m²");
        }
    }

    /// <summary>Vertex count readout.</summary>
    public string VertexText => string.Create(CultureInfo.InvariantCulture, $"{Model.Outline.Count} vertices");

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
