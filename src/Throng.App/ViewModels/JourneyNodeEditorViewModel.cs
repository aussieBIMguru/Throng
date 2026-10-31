using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Views;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ JOURNEY ▸ the selected node (D-318–D-324): every setting of the node, each change one undo step.
/// Reads the node live by id, because edits replace it (records).
/// </summary>
public sealed class JourneyNodeEditorViewModel : ObservableObject
{
    private readonly JourneyViewModel _journey;
    private readonly Guid _id;
    private readonly JourneyNode _fallback;

    /// <summary>Wraps the node <paramref name="id"/> of a journey.</summary>
    public JourneyNodeEditorViewModel(JourneyViewModel journey, Guid id)
    {
        _journey = journey;
        _id = id;
        _fallback = JourneyGraph.Find(journey.Journey, id) ?? new JourneyNode { Id = id };
        DeleteCommand = new RelayCommand(() => _journey.RemoveNode(_id));
        PickInPlanCommand = new RelayCommand(() => _journey.StartPick(_id));
        DonePickingCommand = new RelayCommand(_journey.FinishPick);
        ClearPicksCommand = new RelayCommand(() => Replace(Node with { Picks = null }, "Use the node's type / tag"));
        AddOutputCommand = new RelayCommand(AddOutput);
        BuildCandidates();
        BuildRun();
    }

    /// <summary>The node now.</summary>
    public JourneyNode Node => JourneyGraph.Find(_journey.Journey, _id) ?? _fallback;

    /// <summary>Deletes the node (not Spawn).</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>PICK IN PLAN (D-324).</summary>
    public RelayCommand PickInPlanCommand { get; }

    /// <summary>DONE: back to the graph.</summary>
    public RelayCommand DonePickingCommand { get; }

    /// <summary>Back to choosing by type / tag.</summary>
    public RelayCommand ClearPicksCommand { get; }

    /// <summary>Chance nodes: one more output (up to 6).</summary>
    public RelayCommand AddOutputCommand { get; }

    /// <summary>True when edits are allowed.</summary>
    public bool IsEditable => _journey.IsEditable;

    /// <summary>Kind header.</summary>
    public string KindText => JourneyCanvas.KindName(Node);

    /// <summary>Optional display name.</summary>
    public string Label
    {
        get => Node.Label;
        set => Replace(Node with { Label = (value ?? string.Empty).Trim() }, "Rename journey node");
    }

    /// <summary>Kinds, for showing the right fields.</summary>
    public bool IsSpawn => Node.Kind == JourneyNodeKind.Spawn;

    /// <summary>Device nodes.</summary>
    public bool IsDevice => Node.Kind == JourneyNodeKind.Device;

    /// <summary>Pass zone and dwell nodes.</summary>
    public bool IsZone => Node.Kind is JourneyNodeKind.PassZone or JourneyNodeKind.Dwell;

    /// <summary>Dwell nodes.</summary>
    public bool IsDwell => Node.Kind == JourneyNodeKind.Dwell;

    /// <summary>Sinks.</summary>
    public bool IsSink => Node.Kind == JourneyNodeKind.Sink;

    /// <summary>Train sinks.</summary>
    public bool IsTrainSink => IsSink && Node.SinkKind == JourneySinkKind.BoardTrain;

    /// <summary>Chance nodes.</summary>
    public bool IsChance => Node.Kind == JourneyNodeKind.Chance;

    /// <summary>Tag check and Set tag nodes.</summary>
    public bool IsTagNode => Node.Kind is JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag;

    /// <summary>Set tag nodes.</summary>
    public bool IsSetTag => Node.Kind == JourneyNodeKind.SetTag;

    /// <summary>Devices, zones and zone sinks: candidates, pick list, choose-by (D-318, D-319).</summary>
    public bool HasCandidates => Node.HasCandidates;

    /// <summary>Devices and zones: a grants-tag field (D-314).</summary>
    public bool HasGrants => IsDevice || IsZone;

    /// <summary>Everything but Spawn can be deleted.</summary>
    public bool CanDelete => !IsSpawn;

    /// <summary>True while PICK IN PLAN runs.</summary>
    public bool IsPicking => _journey.IsPicking;

    // ------------------------------------------------------------------ target

    /// <summary>Label over the type / tag field.</summary>
    public string TargetLabel => IsDevice ? "Device type (when nothing is picked)"
        : IsTrainSink ? "Train service tag (empty = any)"
        : IsTagNode ? "Tag"
        : IsSink ? "Sink zone tag (empty = any sink; when nothing is picked)"
        : IsDwell ? "Zone tag (empty = any dwell zone; when nothing is picked)"
        : "Zone tag (empty = any zone; when nothing is picked)";

    /// <summary>True when the node has a type / tag field.</summary>
    public bool HasTarget => IsDevice || IsZone || IsTagNode || (IsSink && Node.SinkKind != JourneySinkKind.Destinations);

    /// <summary>Type tag, zone tag, service tag or person tag.</summary>
    public string TargetTag
    {
        get => Node.TargetTag;
        set
        {
            string tag = (value ?? string.Empty).Trim();
            if (IsTagNode && tag.Length == 0)
            {
                _journey.Reject("A tag check or set tag node needs a tag.");
                OnPropertyChanged();
                return;
            }

            Replace(Node with { TargetTag = tag, ZoneId = IsTagNode ? Node.ZoneId : null }, "Change node target");
        }
    }

    /// <summary>Suggestions for <see cref="TargetTag"/>.</summary>
    public IReadOnlyList<string> TargetOptions => IsDevice ? StudyDefinitions.KnownDeviceTypes(_journey.Study)
        : IsTrainSink ? _journey.TrainTags()
        : IsTagNode ? _journey.KnownTags()
        : [string.Empty, .. _journey.ZoneTags(sinksOnly: IsSink)];

    /// <summary>What the node uses now, in words.</summary>
    public string TargetSummary
    {
        get
        {
            JourneyNode node = Node;
            if (node.HasPicks)
            {
                return string.Create(CultureInfo.InvariantCulture, $"Using {node.Picks!.Count} picked {(IsDevice ? "device" : "zone")}{(node.Picks.Count == 1 ? string.Empty : "s")} (the type / tag is ignored).");
            }

            if (node.ZoneId is { } pinned)
            {
                return $"Pinned to zone '{_journey.ZoneName(pinned)}' (from an older journey). Pick zones below to change it.";
            }

            return IsDevice ? "Using every device of the type." : "Using every zone with the tag.";
        }
    }

    /// <summary>The devices or zones this node may pick (D-318, D-324): tick to add, with a weight each.</summary>
    public ObservableCollection<JourneyPickRowViewModel> Candidates { get; } = [];

    /// <summary>How one candidate is picked (D-319).</summary>
    public JourneyChooseBy ChooseBy
    {
        get => Node.ChooseBy;
        set => Replace(Node with { ChooseBy = value }, "Change how the node chooses");
    }

    /// <summary>Choose-by choices.</summary>
    public IReadOnlyList<LabelledOption<JourneyChooseBy>> ChooseByOptions { get; } =
        [.. Enum.GetValues<JourneyChooseBy>().Select(v => new LabelledOption<JourneyChooseBy>(v, JourneyViewModel.ChooseByLabel(v)))];

    /// <summary>What the rule does, in a sentence.</summary>
    public string ChooseByHint => Node.ChooseBy switch
    {
        JourneyChooseBy.AlongTheWay => "Least extra time compared with walking straight on to the next node.",
        JourneyChooseBy.Nearest => "Shortest walk; queues ignored.",
        JourneyChooseBy.FirstFree => IsDevice ? "Shortest queue; walk breaks ties." : "Least crowded; walk breaks ties.",
        JourneyChooseBy.RandomWeighted => "At random by the weights below (type / tag: all equal).",
        _ => IsDevice ? "Lowest walk + queue (the default)." : "Shortest walk (the default).",
    };

    // ------------------------------------------------------------------ other settings

    /// <summary>Tag given when done (devices, zones).</summary>
    public string GrantsTag
    {
        get => Node.GrantsTag;
        set => Replace(Node with { GrantsTag = (value ?? string.Empty).Trim() }, "Change granted tag");
    }

    /// <summary>Dwell time as text: "60" or "30-180".</summary>
    public string DwellText
    {
        get
        {
            Distribution d = Node.DwellS ?? JourneyGraph.DefaultDwell;
            CultureInfo c = CultureInfo.InvariantCulture;
            return d.Kind == DistributionKind.Fixed ? d.Value.ToString("0.#", c) : $"{d.Min.ToString("0.#", c)}-{d.Max.ToString("0.#", c)}";
        }

        set
        {
            if (!JourneyEdits.TryParseRange(value, out Distribution? range))
            {
                _journey.Reject("Dwell is seconds: 60, or a range like 30-180.");
                OnPropertyChanged();
                return;
            }

            Replace(Node with { DwellS = range }, "Change dwell time");
        }
    }

    /// <summary>When patience runs out (devices).</summary>
    public JourneyGiveUp OnGiveUp
    {
        get => Node.OnGiveUp == JourneyGiveUp.JumpTo ? JourneyGiveUp.Branch : Node.OnGiveUp;
        set => Replace(Node with { OnGiveUp = value }, "Change what happens when patience runs out");
    }

    /// <summary>Give-up choices.</summary>
    public IReadOnlyList<LabelledOption<JourneyGiveUp>> GiveUpOptions { get; } =
    [
        new(JourneyGiveUp.KeepWaiting, "Keep waiting (stress only)"),
        new(JourneyGiveUp.Leave, "Leave the study (counted as gave up)"),
        new(JourneyGiveUp.Branch, "Carry on along 'gave up'"),
    ];

    /// <summary>Balking (devices).</summary>
    public JourneyBalk Balk
    {
        get => Node.Balk;
        set => Replace(Node with { Balk = value }, "Change balking");
    }

    /// <summary>Balk choices.</summary>
    public IReadOnlyList<LabelledOption<JourneyBalk>> BalkOptions { get; } =
    [
        new(JourneyBalk.Default, "Default (the run's setting)"),
        new(JourneyBalk.On, "May switch to a quicker device"),
        new(JourneyBalk.Off, "Stays in the queue they joined"),
    ];

    /// <summary>Patience override in seconds; empty = the profile's.</summary>
    public string PatienceText
    {
        get => Node.PatienceS is { } s ? s.ToString("0", CultureInfo.InvariantCulture) : string.Empty;
        set
        {
            string t = (value ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                Replace(Node with { PatienceS = null }, "Use the profile's patience");
                return;
            }

            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || !(s is >= 5 and <= 7200))
            {
                _journey.Reject("Patience is 5 to 7200 seconds, or empty for the profile's.");
                OnPropertyChanged();
                return;
            }

            Replace(Node with { PatienceS = Math.Round(s) }, "Change node patience");
        }
    }

    /// <summary>How people leave (sinks).</summary>
    public JourneySinkKind SinkKind
    {
        get => Node.SinkKind;
        set
        {
            if (value != Node.SinkKind)
            {
                Replace(Node with { SinkKind = value, TargetTag = string.Empty, Picks = null, ZoneId = null }, "Change how people leave");
            }
        }
    }

    /// <summary>Sink kind choices.</summary>
    public IReadOnlyList<LabelledOption<JourneySinkKind>> SinkKindOptions { get; } =
        [.. Enum.GetValues<JourneySinkKind>().Select(v => new LabelledOption<JourneySinkKind>(v, JourneyViewModel.SinkKindLabel(v)))];

    /// <summary>Set tag: remove instead of grant.</summary>
    public bool RemoveTag
    {
        get => Node.RemoveTag;
        set => Replace(Node with { RemoveTag = value }, value ? "Set tag removes" : "Set tag grants");
    }

    /// <summary>Chance weights as text, e.g. "3, 1, 1" (D-326).</summary>
    public string WeightsText
    {
        get => string.Join(", ", (Node.Weights ?? []).Select(w => w.ToString("0.###", CultureInfo.InvariantCulture)));
        set
        {
            if (!JourneyEdits.TryParseWeights(value, out List<double> weights))
            {
                _journey.Reject("Weights are 2 to 6 numbers of zero or more, e.g. 3, 1, 1.");
                OnPropertyChanged();
                return;
            }

            Replace(Node with { Weights = weights }, "Change chance weights");
        }
    }

    /// <summary>The weights as % per output, e.g. "60 % · 20 % · 20 %".</summary>
    public string WeightsSummary => string.Join(" · ", JourneyGraph.Percentages(Node).Select(p => p.ToString("0", CultureInfo.InvariantCulture) + " %"));

    /// <summary>Re-reads the node.</summary>
    public void Refresh()
    {
        BuildCandidates();
        BuildRun();
        OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ RUN (round 019, D-336)

    /// <summary>The node's run stats line, e.g. "RUN · all profiles ▸ 312 in · 18 s mean / 41 s p95"; empty without a run.</summary>
    public string RunSummary { get; private set; } = string.Empty;

    /// <summary>True when there is a run to report on.</summary>
    public bool HasRun => RunSummary.Length > 0;

    /// <summary>Each candidate device or zone with its count, share and mean wait (D-336).</summary>
    public IReadOnlyList<JourneyRunRow> RunRows { get; private set; } = [];

    /// <summary>True when <see cref="RunRows"/> has rows.</summary>
    public bool HasRunRows => RunRows.Count > 0;

    /// <summary>Re-reads the run counts (a results tick); leaves the settings alone.</summary>
    public void RefreshRun()
    {
        BuildRun();
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(HasRun));
        OnPropertyChanged(nameof(RunRows));
        OnPropertyChanged(nameof(HasRunRows));
    }

    private void BuildRun()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        RunSummary = string.Empty;
        RunRows = [];
        if (_journey.ActualFlow is not { } flow)
        {
            return;
        }

        string head = $"RUN · {_journey.OverlayProfileName} ▸ ";
        if (Node.Kind == JourneyNodeKind.Spawn)
        {
            RunSummary = head + string.Create(c, $"{flow.Started:N0} started");
            return;
        }

        if (flow.NodeOf(_id) is not { } stats)
        {
            RunSummary = head + "not reached on this run (not wired from Spawn, or added after PLAY)";
            return;
        }

        RunSummary = head + JourneyViewModel.NodeLine(Node, stats);
        long total = 0;
        foreach (JourneyCandidateStats candidate in stats.Candidates)
        {
            total += candidate.Count;
        }

        bool device = Node.Kind == JourneyNodeKind.Device;
        var rows = new List<JourneyRunRow>(stats.Candidates.Count);
        foreach (JourneyCandidateStats candidate in stats.Candidates)
        {
            string wait = !device ? string.Empty : candidate.Served > 0 ? string.Create(c, $"{candidate.MeanWaitS:0} s") : "–";
            rows.Add(new JourneyRunRow(
                candidate.Name,
                candidate.Count.ToString("N0", c),
                JourneyViewModel.Percent(total > 0 ? (double)candidate.Count / total : 0),
                wait));
        }

        RunRows = rows;
    }

    internal void Replace(JourneyNode node, string description) => _journey.ReplaceNode(node, description);

    private void AddOutput()
    {
        List<double> weights = [.. Node.Weights ?? []];
        if (weights.Count >= JourneyNode.MaxChanceOutputs)
        {
            _journey.Reject("A chance node has at most 6 outputs.");
            return;
        }

        weights.Add(1);
        Replace(Node with { Weights = weights }, "Add chance output");
    }

    private void BuildCandidates()
    {
        Candidates.Clear();
        JourneyNode node = Node;
        if (!node.HasCandidates)
        {
            return;
        }

        Study study = _journey.Study;
        if (node.Kind == JourneyNodeKind.Device)
        {
            foreach (Device device in study.Devices.OrderBy(d => d.TypeTag, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                string level = study.FindLevel(device.LevelId)?.Name ?? "?";
                Candidates.Add(new JourneyPickRowViewModel(this, device.Id, device.Name, $"{device.TypeTag} · {level}"));
            }
        }
        else
        {
            bool sinksOnly = node.Kind == JourneyNodeKind.Sink;
            foreach (Region zone in study.Regions.Where(r => r.Kind == RegionKind.Zone && (!sinksOnly || r.IsSink)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                string level = study.FindLevel(zone.LevelId)?.Name ?? "?";
                string tag = string.IsNullOrWhiteSpace(zone.Tag) ? zone.ZoneKind.ToString() : zone.Tag.Trim();
                Candidates.Add(new JourneyPickRowViewModel(this, zone.Id, zone.Name, $"{tag} · {level}"));
            }
        }
    }
}

/// <summary>One device or zone in a node's candidate list (D-324): ticked into the pick list, with a weight.</summary>
public sealed class JourneyPickRowViewModel : ObservableObject
{
    private readonly JourneyNodeEditorViewModel _owner;

    /// <summary>Wraps a candidate.</summary>
    public JourneyPickRowViewModel(JourneyNodeEditorViewModel owner, Guid id, string name, string detail)
    {
        _owner = owner;
        Id = id;
        Name = name;
        Detail = detail;
    }

    /// <summary>The device's or zone's id.</summary>
    public Guid Id { get; }

    /// <summary>Its name.</summary>
    public string Name { get; }

    /// <summary>Type or tag, and level.</summary>
    public string Detail { get; }

    /// <summary>True when it's in the pick list.</summary>
    public bool IsPicked
    {
        get => _owner.Node.Picks?.Any(p => p.Id == Id) == true;
        set
        {
            if (value != IsPicked)
            {
                _owner.Replace(JourneyEdits.TogglePick(_owner.Node, Id), value ? $"Pick {Name}" : $"Unpick {Name}");
            }
        }
    }

    /// <summary>Its weight (D-321), when picked.</summary>
    public double Weight
    {
        get => _owner.Node.Picks?.FirstOrDefault(p => p.Id == Id)?.Weight ?? 1;
        set
        {
            if (!IsPicked || !double.IsFinite(value) || value < 0 || value > 1e6)
            {
                OnPropertyChanged();
                return;
            }

            List<JourneyPick> picks = [.. _owner.Node.Picks!.Select(p => p.Id == Id ? p with { Weight = Math.Round(value, 3) } : p)];
            _owner.Replace(_owner.Node with { Picks = picks }, $"Change {Name} weight");
        }
    }
}
