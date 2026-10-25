using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Geometry;
using Throng.Core.Library;
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
        AddRuleCommand = new RelayCommand(AddRule);
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

    // ------------------------------------------------------------------ spans and heights (D-076–D-088)

    private static readonly IReadOnlyList<RegionHeightMode> AllHeightModes = Enum.GetValues<RegionHeightMode>();

    /// <summary>Height modes for the combo.</summary>
    public IReadOnlyList<RegionHeightMode> HeightModeOptions => AllHeightModes;

    /// <summary>Fixed height or floor to floor (obstacles and walls, D-077).</summary>
    public RegionHeightMode HeightMode
    {
        get => Model.HeightMode;
        set => _owner.Apply(value == RegionHeightMode.FullStorey ? "Make region full storey" : "Make region fixed height",
            () => Model.HeightMode, v => Model.HeightMode = v, value);
    }

    /// <summary>True when the fixed height field applies.</summary>
    public bool IsFixedHeight => HasHeight && Model.HeightMode == RegionHeightMode.Fixed;

    /// <summary>Readout of the floor-to-floor height on the active level (full storey mode).</summary>
    public string StoreyHeightText
    {
        get
        {
            Level? level = _owner.ActiveLevelModel ?? _owner.Study.FindLevel(Model.LevelId);
            return level is null || !HasHeight || Model.HeightMode != RegionHeightMode.FullStorey
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"Floor to floor: {_owner.Study.StoreyHeight(level):0.00} m on {level.Name}");
        }
    }

    /// <summary>"This level only" plus the levels above the region's own, for the Top level combo (D-080).</summary>
    public IReadOnlyList<LevelOption> TopLevelOptions
    {
        get
        {
            var options = new List<LevelOption> { new(Guid.Empty, "This level only") };
            if (_owner.Study.FindLevel(Model.LevelId) is { } baseLevel)
            {
                options.AddRange(_owner.LevelOptions.Where(o =>
                    _owner.Study.FindLevel(o.Id) is { } l && l.ElevationM > baseLevel.ElevationM + 1e-6));
            }

            return options;
        }
    }

    /// <summary>Top of the span; <see cref="Guid.Empty"/> stands for "This level only".</summary>
    public Guid TopLevelId
    {
        get => Model.TopLevelId ?? Guid.Empty;
        set
        {
            Guid? top = value == Guid.Empty ? null : value;
            _owner.Apply(top is null ? "Keep region on this level only" : "Change region top level", () => Model.TopLevelId, v => Model.TopLevelId = v, top);
        }
    }

    /// <summary>Spanned levels with an "on this level" tick each (D-088); empty unless it spans 2+ levels.</summary>
    public IReadOnlyList<LevelToggleViewModel> LevelToggles
    {
        get
        {
            IReadOnlyList<Level> levels = HasHeight ? _owner.Study.SpannedLevels(Model) : [];
            if (levels.Count < 2)
            {
                return [];
            }

            return [.. Enumerable.Reverse(levels).Select(l => new LevelToggleViewModel(
                l.Name,
                () => !Model.ExcludedLevelIds.Contains(l.Id),
                on => _owner.Apply(on ? $"Put {Model.Name} on {l.Name}" : $"Remove {Model.Name} from {l.Name}",
                    () => Model.ExcludedLevelIds, v => Model.ExcludedLevelIds = v, LevelToggleViewModel.Toggle(Model.ExcludedLevelIds, l.Id, exclude: !on))))];
        }
    }

    /// <summary>True when the checklist shows.</summary>
    public bool HasLevelToggles => LevelToggles.Count > 0;

    // ------------------------------------------------------------------ emission rules (D-096, D-099)

    /// <summary>True for spawn zones (Spawn or Spawn + sink), which carry emission rules.</summary>
    public bool IsSpawn => Model.IsSpawn;

    /// <summary>Adds a rule (10 per minute, random, to the nearest sink, Commuter).</summary>
    public RelayCommand AddRuleCommand { get; }

    /// <summary>The zone's rules, one panel each.</summary>
    public IReadOnlyList<EmissionRuleViewModel> EmissionRules
    {
        get
        {
            List<IdOption> profiles = [.. StudyTemplates.AvailableProfiles(_owner.Study).Select(p => new IdOption(p.Id, p.Name))];
            return [.. Model.EmissionRules.Select((rule, i) => new EmissionRuleViewModel(this, i, rule, profiles))];
        }
    }

    /// <summary>True when the zone has no rules yet (shows a hint).</summary>
    public bool HasNoRules => Model.EmissionRules.Count == 0;

    /// <summary>Sink zones a rule can send people to: every sink except this zone, in study order.</summary>
    internal IEnumerable<Region> SinkRegions() => _owner.Study.Regions.Where(r => r.IsSink && r.Id != Model.Id);

    /// <summary>Replaces rule <paramref name="index"/> (one undo step; the list is replaced, never edited).</summary>
    internal void ReplaceRule(int index, EmissionRule rule, string description)
    {
        if (index < 0 || index >= Model.EmissionRules.Count || Model.EmissionRules[index] == rule)
        {
            return;
        }

        List<EmissionRule> next = [.. Model.EmissionRules];
        next[index] = rule;
        _owner.Apply($"{description} ({Model.Name})", () => Model.EmissionRules, v => Model.EmissionRules = v, next);
    }

    /// <summary>Removes rule <paramref name="index"/>.</summary>
    internal void RemoveRule(int index)
    {
        if (index < 0 || index >= Model.EmissionRules.Count)
        {
            return;
        }

        List<EmissionRule> next = [.. Model.EmissionRules];
        next.RemoveAt(index);
        _owner.Apply($"Remove emission rule ({Model.Name})", () => Model.EmissionRules, v => Model.EmissionRules = v, next);
    }

    /// <summary>Embeds a built-in profile in the study library when a rule first uses it, so the file stays self-contained.</summary>
    internal void EmbedProfile(Guid profileId)
    {
        if (_owner.Study.Library.Find(profileId) is null && BuiltInLibrary.Instance.Find(profileId) is AgentProfileTemplate builtIn)
        {
            _owner.Study.Library.AddCopy(builtIn, TemplateSource.Embedded);
        }
    }

    /// <summary>Shows a validation message in the ticker.</summary>
    internal void Reject(string message) => _owner.Ticker = message;

    private void AddRule()
    {
        AgentProfileTemplate profile = StudyTemplates.ProfileOrFallback(_owner.Study, TemplateIds.FromKey(StudyTemplates.FallbackProfileKey));
        EmbedProfile(profile.Id);
        List<EmissionRule> next = [.. Model.EmissionRules, new EmissionRule { ProfileId = profile.Id }];
        _owner.Apply($"Add emission rule ({Model.Name})", () => Model.EmissionRules, v => Model.EmissionRules = v, next);
    }

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
