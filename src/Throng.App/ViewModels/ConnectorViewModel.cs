using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>A level choice for combo boxes (bound by <see cref="Id"/>, shown by <see cref="Name"/>).</summary>
/// <param name="Id">Level id.</param>
/// <param name="Name">Level name with its elevation.</param>
public sealed record LevelOption(Guid Id, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// SELECTION-tab properties of the single selected connector (D-063). Every setter is undoable. Values shared by
/// an escalator pair (levels, width, clearance, angle) are changed on both units in one step so the pair stays
/// together (D-059); the direction is per unit.
/// </summary>
public sealed class ConnectorViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ConnectorDirection> EscalatorDirections = [ConnectorDirection.Up, ConnectorDirection.Down];
    private static readonly IReadOnlyList<ConnectorDirection> WalkDirections = [ConnectorDirection.Bidirectional, ConnectorDirection.Up, ConnectorDirection.Down];
    private static readonly IReadOnlyList<ConnectorDirection> TravelatorDirections = [ConnectorDirection.OneWay, ConnectorDirection.Bidirectional];
    private static readonly IReadOnlyList<ConnectorShape> AllShapes = Enum.GetValues<ConnectorShape>();
    private static readonly IReadOnlyList<TurnSide> AllSides = Enum.GetValues<TurnSide>();

    private readonly MainViewModel _owner;

    /// <summary>Wraps a connector.</summary>
    public ConnectorViewModel(Connector model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    /// <summary>The wrapped connector.</summary>
    public Connector Model { get; }

    /// <summary>Partner unit of an escalator pair, if any.</summary>
    public Connector? Partner => _owner.Study.FindPartner(Model);

    /// <summary>True for a unit of an escalator pair.</summary>
    public bool IsPaired => Partner is not null;

    /// <summary>Levels for the bottom/top combos, highest first.</summary>
    public IReadOnlyList<LevelOption> LevelOptions => _owner.LevelOptions;

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A connector needs a name.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rename connector", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>Preset it was placed from.</summary>
    public string PresetText
    {
        get
        {
            TemplateBase? template = Model.TemplateId is { } id ? _owner.Study.Library.Find(id) : null;
            template ??= BuiltInLibrary.Instance.FindByKey(Model.PresetKey);
            return template?.Name ?? (Model.PresetKey.Length > 0 ? Model.PresetKey : Model.Kind.ToString());
        }
    }

    /// <summary>Bottom level (must stay below the top level).</summary>
    public Guid BottomLevelId
    {
        get => Model.BottomLevelId;
        set
        {
            if (value == Model.BottomLevelId)
            {
                return;
            }

            if (!IsBelow(value, Model.TopLevelId))
            {
                Reject("The bottom level must be below the top level.");
                return;
            }

            EditShared("Change connector bottom level", c => c.BottomLevelId = value);
        }
    }

    /// <summary>Top level (must stay above the bottom level).</summary>
    public Guid TopLevelId
    {
        get => Model.TopLevelId;
        set
        {
            if (value == Model.TopLevelId)
            {
                return;
            }

            if (!IsBelow(Model.BottomLevelId, value))
            {
                Reject("The top level must be above the bottom level.");
                return;
            }

            EditShared("Change connector top level", c => c.TopLevelId = value);
        }
    }

    /// <summary>Clear width in metres (0.5–10). A pair is re-spaced to keep its gap.</summary>
    public double WidthM
    {
        get => Model.WidthM;
        set
        {
            if (value is < 0.5 or > 10 || double.IsNaN(value))
            {
                Reject("Width must be between 0.5 and 10 m.");
                return;
            }

            double width = Math.Round(value, 3);
            if (Math.Abs(width - Model.WidthM) > 1e-9)
            {
                EditShared("Change connector width", c => c.WidthM = width, respace: true);
            }
        }
    }

    /// <summary>Directions offered: Up/Down for escalators, one way for travelators, plus Both for stairs and ramps.</summary>
    public IReadOnlyList<ConnectorDirection> DirectionOptions => Model.Kind switch
    {
        ConnectorKind.Escalator => EscalatorDirections,
        ConnectorKind.Travelator => TravelatorDirections,
        _ => WalkDirections,
    };

    /// <summary>True for travelators (one level, a belt length instead of levels, D-090).</summary>
    public bool IsTravelator => Model.IsTravelator;

    /// <summary>True for everything that links two levels.</summary>
    public bool ChangesLevel => !Model.IsTravelator;

    /// <summary>Travelator belt length (1–200 m).</summary>
    public double RunLengthM
    {
        get => Model.RunLengthM;
        set
        {
            if (double.IsNaN(value) || value < 1 || value > 200)
            {
                Reject("Belt length must be between 1 and 200 m.");
                return;
            }

            _owner.Apply("Change travelator length", () => Model.RunLengthM, v => Model.RunLengthM = v, Math.Round(value, 2));
        }
    }

    /// <summary>True when the per-level option applies: a switchback spanning 2+ storeys (D-091).</summary>
    public bool ShowsPerLevelOption => Model.IsSwitchback && StoreyCount() >= 2;

    /// <summary>One circuit per level (D-091).</summary>
    public bool PerLevelCircuits
    {
        get => Model.PerLevelCircuits;
        set => _owner.Apply(value ? "Use one circuit per level" : "Use one long run", () => Model.PerLevelCircuits, v => Model.PerLevelCircuits = v, value);
    }

    /// <summary>Intermediate levels of a per-level stair with an "exit here" tick (D-093, D-094).</summary>
    public IReadOnlyList<LevelToggleViewModel> ExitToggles
    {
        get
        {
            if (!Model.UsesCircuits || StoreyCount() < 2)
            {
                return [];
            }

            Study study = _owner.Study;
            return [.. study.Levels
                .Where(l => study.IsIntermediateLevel(Model, l.Id))
                .OrderByDescending(l => l.ElevationM)
                .Select(l => new LevelToggleViewModel(
                    l.Name,
                    () => !Model.ExcludedLevelIds.Contains(l.Id),
                    on => _owner.Apply(on ? $"Exit {Model.Name} at {l.Name}" : $"No exit from {Model.Name} at {l.Name}",
                        () => Model.ExcludedLevelIds, v => Model.ExcludedLevelIds = v, LevelToggleViewModel.Toggle(Model.ExcludedLevelIds, l.Id, exclude: !on))))];
        }
    }

    /// <summary>True when the exit checklist shows.</summary>
    public bool HasExitToggles => ExitToggles.Count > 0;

    private int StoreyCount()
    {
        Level? bottom = _owner.Study.FindLevel(Model.BottomLevelId);
        Level? top = _owner.Study.FindLevel(Model.TopLevelId);
        return bottom is null || top is null ? 0 : _owner.Study.LevelsBetween(bottom, top).Count() - 1;
    }

    /// <summary>Travel direction (per unit, so each half of a pair can be flipped).</summary>
    public ConnectorDirection Direction
    {
        get => Model.Direction;
        set => _owner.Apply($"Set {Model.Name} {value.ToString().ToLowerInvariant()}", () => Model.Direction, v => Model.Direction = v, value);
    }

    /// <summary>True when the shape can change (stairs and ramps; escalators are straight).</summary>
    public bool CanChangeShape => Model.Kind is ConnectorKind.Stair or ConnectorKind.Ramp;

    /// <summary>Shapes offered.</summary>
    public IReadOnlyList<ConnectorShape> ShapeOptions => AllShapes;

    /// <summary>Straight or switchback (D-070).</summary>
    public ConnectorShape Shape
    {
        get => Model.Shape;
        set
        {
            if (!CanChangeShape)
            {
                OnPropertyChanged();
                return;
            }

            _owner.Apply($"Make {Model.Name} {value.ToString().ToLowerInvariant()}", () => Model.Shape, v => Model.Shape = v, value);
        }
    }

    /// <summary>True when switchback options apply.</summary>
    public bool IsSwitchback => Model.IsSwitchback;

    /// <summary>Sides offered for the return run.</summary>
    public IReadOnlyList<TurnSide> SideOptions => AllSides;

    /// <summary>Return run side (Tab flips it in the viewport, D-058).</summary>
    public TurnSide ReturnSide
    {
        get => Model.ReturnSide;
        set => _owner.Apply("Flip switchback side", () => Model.ReturnSide, v => Model.ReturnSide = v, value);
    }

    /// <summary>Gap between switchback runs in metres (0–2).</summary>
    public double SwitchbackGapM
    {
        get => Model.SwitchbackGapM;
        set
        {
            if (value is < 0 or > 2 || double.IsNaN(value))
            {
                Reject("Switchback gap must be between 0 and 2 m.");
                return;
            }

            _owner.Apply("Change switchback gap", () => Model.SwitchbackGapM, v => Model.SwitchbackGapM = v, Math.Round(value, 3));
        }
    }

    /// <summary>Opening clearance in metres (0–2, D-065).</summary>
    public double ClearanceM
    {
        get => Model.ClearanceM;
        set
        {
            if (value is < 0 or > 2 || double.IsNaN(value))
            {
                Reject("Opening clearance must be between 0 and 2 m.");
                return;
            }

            double clearance = Math.Round(value, 3);
            if (Math.Abs(clearance - Model.ClearanceM) > 1e-9)
            {
                EditShared("Change opening clearance", c => c.ClearanceM = clearance);
            }
        }
    }

    /// <summary>Run direction in degrees. A pair rotates together about its midpoint.</summary>
    public double AngleDeg
    {
        get => Math.Round(Model.AngleDeg, 2);
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Reject("Type an angle in degrees.");
                return;
            }

            double delta = ConnectorFactory.NormaliseAngle(value) - Model.AngleDeg;
            if (Math.Abs(delta) < 1e-9)
            {
                return;
            }

            Connector? partner = Partner;
            Point2 pivot = ConnectorFactory.Pivot(Model, partner);
            var changes = new List<(Connector, Connector)> { (Model, ConnectorFactory.Rotated(Model, pivot, delta)) };
            if (partner is not null)
            {
                changes.Add((partner, ConnectorFactory.Rotated(partner, pivot, delta)));
            }

            _owner.Execute(StateActions.Connectors($"Rotate {Model.Name}", changes, _owner.NotifyModelChanged));
        }
    }

    /// <summary>Derived geometry: risers, flights, length (or why the layout fails).</summary>
    public string LayoutText
    {
        get
        {
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(Model, _owner.Study);
            return layout.IsSuccess && layout.Value is { } l ? l.Describe(Model.Kind) : layout.Error ?? "Layout failed.";
        }
    }

    /// <summary>Rise between the two levels.</summary>
    public string RiseText
    {
        get
        {
            Level? bottom = _owner.Study.FindLevel(Model.BottomLevelId);
            Level? top = _owner.Study.FindLevel(Model.TopLevelId);
            return bottom is null || top is null
                ? "—"
                : string.Create(CultureInfo.InvariantCulture, $"{top.ElevationM - bottom.ElevationM:0.00} m rise");
        }
    }

    /// <summary>
    /// Staff only (D-110): public agents never route on it; staff routing uses it from M3. Per unit for escalator
    /// pairs.
    /// </summary>
    public bool StaffOnly
    {
        get => Model.StaffOnly;
        set => _owner.Apply(value ? $"Make {Model.Name} staff only" : $"Open {Model.Name} to the public", () => Model.StaffOnly, v => Model.StaffOnly = v, value);
    }

    /// <summary>Pair note.</summary>
    public string PairText => Partner is { } p
        ? $"Paired with {p.Name} ({p.Direction.ToString().ToLowerInvariant()}). Levels, width, clearance and angle change on both."
        : string.Empty;

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private bool IsBelow(Guid lowerId, Guid upperId)
    {
        Level? lower = _owner.Study.FindLevel(lowerId);
        Level? upper = _owner.Study.FindLevel(upperId);
        return lower is not null && upper is not null && upper.ElevationM > lower.ElevationM + 1e-6;
    }

    private void Reject(string message)
    {
        _owner.Ticker = message;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Applies a change to this connector and, for a pair, its partner, as one undo step.</summary>
    private void EditShared(string description, Action<Connector> mutate, bool respace = false)
    {
        Connector next = Model.Clone();
        mutate(next);
        var changes = new List<(Connector, Connector)> { (Model, next) };
        if (Partner is { } partner)
        {
            Connector partnerNext = partner.Clone();
            mutate(partnerNext);
            if (respace)
            {
                partnerNext.BottomLanding = ConnectorFactory.RespacedPartner(next, partner, next.WidthM, next.PairGapM);
            }

            changes.Add((partner, partnerNext));
        }

        _owner.Execute(StateActions.Connectors(description, changes, _owner.NotifyModelChanged));
    }
}

/// <summary>SELECTION-tab properties of the single selected shaft (D-056). Every setter is undoable.</summary>
public sealed class ShaftViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps a shaft.</summary>
    public ShaftViewModel(Shaft model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    /// <summary>The wrapped shaft.</summary>
    public Shaft Model { get; }

    /// <summary>Levels for the base/top combos, highest first.</summary>
    public IReadOnlyList<LevelOption> LevelOptions => _owner.LevelOptions;

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A shaft needs a name.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rename shaft", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>Spanned levels with a "stops here" tick (D-087, D-088); empty for a single-level shaft.</summary>
    public IReadOnlyList<LevelToggleViewModel> StopToggles
    {
        get
        {
            Study study = _owner.Study;
            List<Level> levels = [.. study.Levels.Where(l => study.ShaftSpans(Model, l.Id)).OrderByDescending(l => l.ElevationM)];
            if (levels.Count < 2)
            {
                return [];
            }

            return [.. levels.Select(l => new LevelToggleViewModel(
                l.Name,
                () => !Model.NoStopLevelIds.Contains(l.Id),
                on => _owner.Apply(on ? $"{Model.Name} stops at {l.Name}" : $"{Model.Name} doesn't stop at {l.Name}",
                    () => Model.NoStopLevelIds, v => Model.NoStopLevelIds = v, LevelToggleViewModel.Toggle(Model.NoStopLevelIds, l.Id, exclude: !on))))];
        }
    }

    /// <summary>True when the stop checklist shows.</summary>
    public bool HasStopToggles => StopToggles.Count > 0;

    /// <summary>Lowest level the shaft passes through (at or below the top).</summary>
    public Guid BaseLevelId
    {
        get => Model.BaseLevelId;
        set
        {
            if (!InOrder(value, Model.TopLevelId))
            {
                _owner.Ticker = "The base level can't be above the top level.";
                OnPropertyChanged(string.Empty);
                return;
            }

            _owner.Apply("Change shaft base level", () => Model.BaseLevelId, v => Model.BaseLevelId = v, value);
        }
    }

    /// <summary>Highest level the shaft passes through (at or above the base).</summary>
    public Guid TopLevelId
    {
        get => Model.TopLevelId;
        set
        {
            if (!InOrder(Model.BaseLevelId, value))
            {
                _owner.Ticker = "The top level can't be below the base level.";
                OnPropertyChanged(string.Empty);
                return;
            }

            _owner.Apply("Change shaft top level", () => Model.TopLevelId, v => Model.TopLevelId = v, value);
        }
    }

    /// <summary>Free-text tag, e.g. "Lift" or "Atrium". Blank clears it.</summary>
    public string Tag
    {
        get => Model.Tag ?? string.Empty;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            _owner.Apply("Change shaft tag", () => Model.Tag, v => Model.Tag = v, trimmed.Length == 0 ? null : trimmed);
        }
    }

    /// <summary>Plan area and the number of levels it passes through.</summary>
    public string SizeText
    {
        get
        {
            int levels = _owner.Study.Levels.Count(l => _owner.Study.ShaftSpans(Model, l.Id));
            return string.Create(CultureInfo.InvariantCulture,
                $"{PolygonMath.Area(Model.Outline):0.00} m² · through {levels} level{(levels == 1 ? string.Empty : "s")}");
        }
    }

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private bool InOrder(Guid lowerId, Guid upperId)
    {
        Level? lower = _owner.Study.FindLevel(lowerId);
        Level? upper = _owner.Study.FindLevel(upperId);
        return lower is not null && upper is not null && upper.ElevationM >= lower.ElevationM - 1e-6;
    }
}
