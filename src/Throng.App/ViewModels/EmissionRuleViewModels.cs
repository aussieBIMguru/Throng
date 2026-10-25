using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>A choice for combo boxes bound by id (agent profiles).</summary>
/// <param name="Id">Template id.</param>
/// <param name="Name">Display name.</param>
public sealed record IdOption(Guid Id, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// One emission rule in the SELECTION panel of a spawn zone (D-096, D-099). Rules are immutable records, so every
/// edit builds a new rule and replaces the zone's whole list in one undo step.
/// </summary>
public sealed class EmissionRuleViewModel : ObservableObject
{
    private static readonly IReadOnlyList<EmissionPattern> AllPatterns = Enum.GetValues<EmissionPattern>();
    private static readonly IReadOnlyList<DestinationMode> AllModes = Enum.GetValues<DestinationMode>();

    /// <summary>Curve menu entry shown when the points don't match a preset.</summary>
    private const string CustomCurve = "Custom";

    private static readonly IReadOnlyList<string> AllCurveNames = [.. RateCurves.All.Select(RateCurves.NameOf), CustomCurve];

    /// <summary>Sparkline size in device-independent pixels.</summary>
    private const double SparkWidth = 160, SparkHeight = 28;

    private readonly RegionViewModel _region;
    private readonly int _index;

    /// <summary>Wraps rule <paramref name="index"/> of a region.</summary>
    public EmissionRuleViewModel(RegionViewModel region, int index, EmissionRule rule, IReadOnlyList<IdOption> profiles)
    {
        _region = region;
        _index = index;
        Rule = rule;
        ProfileOptions = profiles;
        RemoveCommand = new RelayCommand(() => _region.RemoveRule(_index));
        AddPointCommand = new RelayCommand(AddPoint);
    }

    /// <summary>The wrapped rule.</summary>
    public EmissionRule Rule { get; }

    /// <summary>"RULE 1", "RULE 2"…</summary>
    public string Header => string.Create(CultureInfo.InvariantCulture, $"RULE {_index + 1}");

    /// <summary>Agent profiles: the study's own, then the built-ins.</summary>
    public IReadOnlyList<IdOption> ProfileOptions { get; }

    /// <summary>Constant or Poisson.</summary>
    public IReadOnlyList<EmissionPattern> PatternOptions => AllPatterns;

    /// <summary>Nearest sink or chosen sinks.</summary>
    public IReadOnlyList<DestinationMode> ModeOptions => AllModes;

    /// <summary>Removes this rule.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Adds a rate curve point 10 minutes after the last one, at the same multiplier (D-129).</summary>
    public RelayCommand AddPointCommand { get; }

    /// <summary>Rate curve presets plus "Custom" (D-129).</summary>
    public IReadOnlyList<string> CurveOptions => AllCurveNames;

    /// <summary>
    /// The preset the curve matches, or "Custom" once its points have been edited. Choosing a preset replaces the
    /// points with its own; choosing "Custom" changes nothing.
    /// </summary>
    public string CurveName
    {
        get
        {
            foreach (RateCurvePreset preset in RateCurves.All)
            {
                if (Rule.RateCurve.SequenceEqual(RateCurves.PointsOf(preset)))
                {
                    return RateCurves.NameOf(preset);
                }
            }

            return CustomCurve;
        }

        set
        {
            if (value == CurveName || value == CustomCurve)
            {
                return;
            }

            foreach (RateCurvePreset preset in RateCurves.All)
            {
                if (RateCurves.NameOf(preset) == value)
                {
                    _region.ReplaceRule(_index, Rule with { RateCurve = RateCurves.PointsOf(preset) }, "Change rule rate curve");
                    return;
                }
            }
        }
    }

    /// <summary>True when the rule has a rate curve (the point list and sparkline show).</summary>
    public bool HasCurve => Rule.RateCurve.Count > 0;

    /// <summary>Editable curve points, in minute order.</summary>
    public IReadOnlyList<RateCurvePointViewModel> CurvePoints =>
        [.. Rule.RateCurve.Select((point, k) => new RateCurvePointViewModel(this, k, point))];

    /// <summary>"0.4–1.8× over 60 min, mean 1.0×" for the curve, or "Flat".</summary>
    public string CurveSummary
    {
        get
        {
            (double[] minutes, double[] multipliers) = RateCurves.Compile(Rule.RateCurve);
            if (minutes.Length == 0)
            {
                return "Flat: the rate above all the way through.";
            }

            double end = Math.Max(minutes[^1], 1);
            double sum = 0;
            const int samples = 120;
            for (int k = 0; k < samples; k++)
            {
                sum += RateCurves.MultiplierAt(minutes, multipliers, (k + 0.5) * end / samples);
            }

            double mean = sum / samples;
            return string.Create(CultureInfo.InvariantCulture,
                $"{multipliers.Min():0.##}–{multipliers.Max():0.##}× over {end:0.#} min, mean {mean:0.##}× ({mean * Rule.RatePerMinute:0.#}/min); holds the last value after.");
        }
    }

    /// <summary>Sparkline of the multiplier over the curve's span (read-only; D-129).</summary>
    public System.Windows.Media.PointCollection CurveSparkline
    {
        get
        {
            var line = new System.Windows.Media.PointCollection();
            (double[] minutes, double[] multipliers) = RateCurves.Compile(Rule.RateCurve);
            if (minutes.Length == 0)
            {
                return line;
            }

            double end = Math.Max(minutes[^1], 1);
            double top = Math.Max(1.0, multipliers.Max());
            const int samples = 64;
            for (int k = 0; k <= samples; k++)
            {
                double minute = k * end / samples;
                double m = RateCurves.MultiplierAt(minutes, multipliers, minute);
                line.Add(new System.Windows.Point(k * SparkWidth / samples, SparkHeight - (m / top * (SparkHeight - 2)) - 1));
            }

            line.Freeze();
            return line;
        }
    }

    /// <summary>Profile id.</summary>
    public Guid ProfileId
    {
        get => Rule.ProfileId;
        set
        {
            if (value != Rule.ProfileId && value != Guid.Empty)
            {
                _region.EmbedProfile(value);
                _region.ReplaceRule(_index, Rule with { ProfileId = value }, "Change rule profile");
            }
        }
    }

    /// <summary>People per minute (0–10 000).</summary>
    public double RatePerMinute
    {
        get => Rule.RatePerMinute;
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 10000)
            {
                _region.Reject("Rate must be between 0 and 10 000 people per minute.");
                OnPropertyChanged();
                return;
            }

            _region.ReplaceRule(_index, Rule with { RatePerMinute = Math.Round(value, 3) }, "Change rule rate");
        }
    }

    /// <summary>Even or random spacing.</summary>
    public EmissionPattern Pattern
    {
        get => Rule.Pattern;
        set => _region.ReplaceRule(_index, Rule with { Pattern = value }, "Change rule pattern");
    }

    /// <summary>Nearest sink or weighted chosen sinks.</summary>
    public DestinationMode DestinationMode
    {
        get => Rule.DestinationMode;
        set => _region.ReplaceRule(_index, Rule with { DestinationMode = value }, "Change rule destination");
    }

    /// <summary>True when the sink checklist applies.</summary>
    public bool IsChosenSinks => Rule.DestinationMode == DestinationMode.ChosenSinks;

    /// <summary>Every sink in the study except the zone itself, with its tick and weight for this rule.</summary>
    public IReadOnlyList<SinkChoiceViewModel> Sinks =>
        [.. _region.SinkRegions().Select(sink => new SinkChoiceViewModel(this, sink))];

    /// <summary>True when the study has no other sink zone.</summary>
    public bool HasNoSinks => Sinks.Count == 0;

    /// <summary>Replaces curve point <paramref name="k"/>, keeping the list in minute order.</summary>
    internal void SetPoint(int k, RateCurvePoint point)
    {
        if (!RateCurves.IsValid(point) || point.Minute > 24 * 60 || point.Multiplier > 100)
        {
            _region.Reject("Curve points need a minute from 0 to 1440 and a multiplier from 0 to 100.");
            return;
        }

        List<RateCurvePoint> next = [.. Rule.RateCurve];
        next[k] = point;
        _region.ReplaceRule(_index, Rule with { RateCurve = [.. next.OrderBy(p => p.Minute)] }, "Change rate curve point");
    }

    /// <summary>Removes curve point <paramref name="k"/> (removing the last one makes the rate flat).</summary>
    internal void RemovePoint(int k)
    {
        List<RateCurvePoint> next = [.. Rule.RateCurve];
        next.RemoveAt(k);
        _region.ReplaceRule(_index, Rule with { RateCurve = next }, "Remove rate curve point");
    }

    private void AddPoint()
    {
        RateCurvePoint? last = Rule.RateCurve.Count > 0 ? Rule.RateCurve.MaxBy(p => p.Minute) : null;
        List<RateCurvePoint> next = [.. Rule.RateCurve];
        next.Add(last is null ? RateCurvePoint.At(0, 1) : RateCurvePoint.At(Math.Min(24 * 60, last.Minute + 10), last.Multiplier));
        _region.ReplaceRule(_index, Rule with { RateCurve = [.. next.OrderBy(p => p.Minute)] }, "Add rate curve point");
    }

    /// <summary>Ticks or unticks a sink (a new sink gets weight 1).</summary>
    internal void SetSink(Guid sinkId, bool on)
    {
        List<SinkWeight> next = [.. Rule.Destinations.Where(d => d.SinkId != sinkId)];
        if (on)
        {
            next.Add(new SinkWeight { SinkId = sinkId, Weight = 1 });
        }

        _region.ReplaceRule(_index, Rule with { Destinations = next }, on ? "Add rule sink" : "Remove rule sink");
    }

    /// <summary>Sets a ticked sink's weight.</summary>
    internal void SetWeight(Guid sinkId, double weight)
    {
        if (double.IsNaN(weight) || weight < 0 || weight > 1e6)
        {
            _region.Reject("Weights must be zero or more.");
            return;
        }

        List<SinkWeight> next = [.. Rule.Destinations.Select(d => d.SinkId == sinkId ? d with { Weight = Math.Round(weight, 3) } : d)];
        _region.ReplaceRule(_index, Rule with { Destinations = next }, "Change rule sink weight");
    }
}

/// <summary>A sink tick and weight in a rule's checklist.</summary>
public sealed class SinkChoiceViewModel : ObservableObject
{
    private readonly EmissionRuleViewModel _rule;
    private readonly Region _sink;

    /// <summary>Wraps a sink for a rule.</summary>
    public SinkChoiceViewModel(EmissionRuleViewModel rule, Region sink)
    {
        _rule = rule;
        _sink = sink;
    }

    /// <summary>Sink name.</summary>
    public string Name => _sink.Name;

    private SinkWeight? Entry => _rule.Rule.Destinations.FirstOrDefault(d => d.SinkId == _sink.Id);

    /// <summary>True when the rule sends people here.</summary>
    public bool IsOn
    {
        get => Entry is not null;
        set
        {
            if (value != IsOn)
            {
                _rule.SetSink(_sink.Id, value);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Relative weight (only meaningful when ticked).</summary>
    public double Weight
    {
        get => Entry?.Weight ?? 0;
        set
        {
            if (Entry is null)
            {
                OnPropertyChanged();
                return;
            }

            _rule.SetWeight(_sink.Id, value);
            OnPropertyChanged();
        }
    }
}

/// <summary>One editable point of a rule's rate curve (D-129).</summary>
public sealed class RateCurvePointViewModel : ObservableObject
{
    private readonly EmissionRuleViewModel _rule;
    private readonly int _index;
    private readonly RateCurvePoint _point;

    /// <summary>Wraps point <paramref name="index"/> of a rule's curve.</summary>
    public RateCurvePointViewModel(EmissionRuleViewModel rule, int index, RateCurvePoint point)
    {
        _rule = rule;
        _index = index;
        _point = point;
        RemoveCommand = new RelayCommand(() => _rule.RemovePoint(_index));
    }

    /// <summary>Removes this point.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Minutes from the start of the run.</summary>
    public double Minute
    {
        get => _point.Minute;
        set
        {
            _rule.SetPoint(_index, _point with { Minute = Math.Round(value, 2) });
            OnPropertyChanged();
        }
    }

    /// <summary>Multiplier on the rule's rate.</summary>
    public double Multiplier
    {
        get => _point.Multiplier;
        set
        {
            _rule.SetPoint(_index, _point with { Multiplier = Math.Round(value, 3) });
            OnPropertyChanged();
        }
    }
}
