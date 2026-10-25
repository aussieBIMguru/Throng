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
    /// <param name="region">Owning zone.</param>
    /// <param name="index">Rule position.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="profiles">Agent profile choices.</param>
    /// <param name="journeys">Journey choices, "none" (<see cref="Guid.Empty"/>) first.</param>
    public EmissionRuleViewModel(RegionViewModel region, int index, EmissionRule rule, IReadOnlyList<IdOption> profiles, IReadOnlyList<IdOption> journeys)
    {
        _region = region;
        _index = index;
        Rule = rule;
        ProfileOptions = profiles;
        JourneyOptions = journeys;
        RemoveCommand = new RelayCommand(() => _region.RemoveRule(_index));
        AddPointCommand = new RelayCommand(AddPoint);
        AddLineCommand = new RelayCommand(AddLine);
    }

    /// <summary>The wrapped rule.</summary>
    public EmissionRule Rule { get; }

    /// <summary>"RULE 1", "RULE 2"…</summary>
    public string Header => string.Create(CultureInfo.InvariantCulture, $"RULE {_index + 1}");

    /// <summary>Agent profiles: the study's own, then the built-ins.</summary>
    public IReadOnlyList<IdOption> ProfileOptions { get; }

    /// <summary>Journeys: "none" first, then the study's own and the built-ins (D-154).</summary>
    public IReadOnlyList<IdOption> JourneyOptions { get; }

    /// <summary>Adds a mix line (Commuter, no journey, share 1; D-151).</summary>
    public RelayCommand AddLineCommand { get; }

    /// <summary>The rule's mix lines, one row each (D-151).</summary>
    public IReadOnlyList<MixLineViewModel> MixLines =>
        [.. Rule.Mix.Select((line, k) => new MixLineViewModel(this, k, line))];

    /// <summary>True when the rule has no mix line (it emits no one).</summary>
    public bool HasNoLines => Rule.Mix.Count == 0;

    /// <summary>Sum of the lines' positive shares (for the % column).</summary>
    internal double TotalShare => Rule.Mix.Where(m => m.Share > 0).Sum(m => m.Share);

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

    /// <summary>Replaces mix line <paramref name="k"/> (one undo step).</summary>
    internal void SetLine(int k, MixLine line, string description)
    {
        if (k < 0 || k >= Rule.Mix.Count)
        {
            return;
        }

        List<MixLine> next = [.. Rule.Mix];
        next[k] = line;
        _region.ReplaceRule(_index, Rule with { Mix = next }, description);
    }

    /// <summary>Removes mix line <paramref name="k"/>.</summary>
    internal void RemoveLine(int k)
    {
        if (k < 0 || k >= Rule.Mix.Count)
        {
            return;
        }

        List<MixLine> next = [.. Rule.Mix];
        next.RemoveAt(k);
        _region.ReplaceRule(_index, Rule with { Mix = next }, "Remove mix line");
    }

    /// <summary>Embeds a built-in profile in the study library.</summary>
    internal void EmbedProfile(Guid profileId) => _region.EmbedProfile(profileId);

    /// <summary>Embeds a built-in journey in the study library.</summary>
    internal void EmbedJourney(Guid journeyId) => _region.EmbedJourney(journeyId);

    /// <summary>Shows a validation message.</summary>
    internal void Reject(string message) => _region.Reject(message);

    private void AddLine()
    {
        Guid commuter = TemplateIds.FromKey(StudyTemplates.FallbackProfileKey);
        EmbedProfile(commuter);
        _region.ReplaceRule(_index, Rule with { Mix = [.. Rule.Mix, MixLine.Of(commuter)] }, "Add mix line");
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

/// <summary>
/// One mix line of a rule (D-151): profile, journey, share and spawn tags. Edits replace the line (and the rule) in
/// one undo step.
/// </summary>
public sealed class MixLineViewModel : ObservableObject
{
    private readonly EmissionRuleViewModel _rule;
    private readonly int _index;
    private readonly MixLine _line;

    /// <summary>Wraps line <paramref name="index"/> of a rule's mix.</summary>
    public MixLineViewModel(EmissionRuleViewModel rule, int index, MixLine line)
    {
        _rule = rule;
        _index = index;
        _line = line;
        RemoveCommand = new RelayCommand(() => _rule.RemoveLine(_index));
    }

    /// <summary>Removes this line.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Profile choices.</summary>
    public IReadOnlyList<IdOption> ProfileOptions => _rule.ProfileOptions;

    /// <summary>Journey choices ("none" first).</summary>
    public IReadOnlyList<IdOption> JourneyOptions => _rule.JourneyOptions;

    /// <summary>Profile id.</summary>
    public Guid ProfileId
    {
        get => _line.ProfileId;
        set
        {
            if (value != _line.ProfileId && value != Guid.Empty)
            {
                _rule.EmbedProfile(value);
                _rule.SetLine(_index, _line with { ProfileId = value }, "Change mix line profile");
            }
        }
    }

    /// <summary>Journey id, <see cref="Guid.Empty"/> for none (straight to a sink).</summary>
    public Guid JourneyId
    {
        get => _line.JourneyId ?? Guid.Empty;
        set
        {
            Guid? next = value == Guid.Empty ? null : value;
            if (next != _line.JourneyId)
            {
                if (next is { } id)
                {
                    _rule.EmbedJourney(id);
                }

                _rule.SetLine(_index, _line with { JourneyId = next }, "Change mix line journey");
            }
        }
    }

    /// <summary>Relative share (0–1 000 000).</summary>
    public double Share
    {
        get => _line.Share;
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 1e6)
            {
                _rule.Reject("Shares must be zero or more.");
                OnPropertyChanged();
                return;
            }

            _rule.SetLine(_index, _line with { Share = Math.Round(value, 3) }, "Change mix line share");
        }
    }

    /// <summary>This line's share of the rule, e.g. "70%".</summary>
    public string SharePercent
    {
        get
        {
            double total = _rule.TotalShare;
            return total > 0 && _line.Share > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{_line.Share / total * 100:0.#}%")
                : "0%";
        }
    }

    /// <summary>Spawn tags as text, e.g. "ticket 50%, vip" (no % means everyone; D-150).</summary>
    public string SpawnTagsText
    {
        get => FormatTags(_line.SpawnTags);
        set
        {
            if (!TryParseTags(value, out List<SpawnTag> tags, out string? error))
            {
                _rule.Reject(error!);
                OnPropertyChanged();
                return;
            }

            if (FormatTags(tags) != FormatTags(_line.SpawnTags))
            {
                _rule.SetLine(_index, _line with { SpawnTags = tags }, "Change mix line tags");
            }
        }
    }

    /// <summary>"ticket 50%, vip" for a tag list.</summary>
    public static string FormatTags(IEnumerable<SpawnTag> tags) =>
        string.Join(", ", tags.Select(t => t.Percent >= 100
            ? t.Tag
            : string.Create(CultureInfo.InvariantCulture, $"{t.Tag} {t.Percent:0.##}%")));

    /// <summary>
    /// Parses "tag", "tag 50" or "tag 50%" entries separated by commas or semicolons. Tags are single words; the
    /// percentage is 0–100.
    /// </summary>
    public static bool TryParseTags(string? text, out List<SpawnTag> tags, out string? error)
    {
        tags = [];
        error = null;
        foreach (string raw in (text ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double percent = 100;
            if (parts.Length > 2
                || (parts.Length == 2 && !double.TryParse(parts[1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                || percent < 0 || percent > 100)
            {
                error = $"Couldn't read '{raw}': write tags as \"ticket 50%, vip\" (one word each, 0–100%).";
                return false;
            }

            tags.Add(SpawnTag.Of(parts[0], percent));
        }

        return true;
    }
}
