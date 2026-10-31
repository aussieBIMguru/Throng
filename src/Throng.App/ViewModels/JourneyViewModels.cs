using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Analysis.Checks;
using Throng.App.Views;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ JOURNEY and the JOURNEY canvas's host (D-154, D-308, D-323–D-328): the journey picked in LIBRARY ▸
/// JOURNEYS, edited on the canvas (move, wire, add, delete, chips) and in SELECTION (the selected node's settings).
/// Every edit is one undo step through <see cref="MainViewModel.EditJourney"/>. Built-ins are edited as an override:
/// the first edit makes the study's copy under the built-in's id (D-328), so the journey is looked up by id on every
/// read, never held.
/// </summary>
public sealed class JourneyViewModel : ObservableObject, IJourneyCanvasHost
{
    private readonly MainViewModel _owner;
    private readonly Guid _id;
    private readonly JourneyTemplate _fallback;
    private Guid? _selectedNodeId;
    private int _revision;
    private JourneyNodeEditorViewModel? _nodeEditor;

    // The run overlay (round 019): mode, profile filter (null = everyone), the built overlay and what it was built from.
    private bool _overlayPredicted;
    private string? _overlayProfile;
    private JourneyOverlay _overlay = JourneyOverlay.Empty;
    private (long Version, SimRunner? Runner, string State, bool Predicted, string? Profile, SimModel? Model)? _overlayKey;

    /// <summary>Wraps a journey.</summary>
    public JourneyViewModel(JourneyTemplate journey, MainViewModel owner)
    {
        _id = journey.Id;
        _fallback = journey;
        _owner = owner;
        JourneyGraph.Ensure(journey);
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateJourney(Journey));
        DeleteCommand = new RelayCommand(() => _owner.DeleteJourney(Journey));
        ShowGraphCommand = new RelayCommand(_owner.ShowJourneyView);
        ResetCommand = new RelayCommand(() => _owner.ResetJourney(_id));
        SaveToMineCommand = new RelayCommand(() => _owner.SaveToMyLibrary(Journey));
        UpdateFromMineCommand = new RelayCommand(() => _owner.UpdateFromMyLibrary(_id));
        RebuildNodes();
        RebuildOverlay();
    }

    /// <summary>The journey: the study's copy when there is one, else yours (round 020), else the built-in (D-328).</summary>
    public JourneyTemplate Journey => StudyTemplates.FindJourney(_owner.Study, _id, _owner.Mine) ?? _fallback;

    /// <summary>The journey's id.</summary>
    public Guid Id => _id;

    /// <summary>Makes a copy with a new name in the study library and selects it (D-161).</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Deletes one of the study's own journeys that nothing uses.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Opens the JOURNEY view (D-308).</summary>
    public RelayCommand ShowGraphCommand { get; }

    /// <summary>RESET TO BUILT-IN (D-328).</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>SAVE TO MY LIBRARY (round 020, D-342): copies this journey (its picks too, D-349) into your library.</summary>
    public RelayCommand SaveToMineCommand { get; }

    /// <summary>UPDATE FROM MY LIBRARY (D-344): your library's version back onto the study's copy (one undo step).</summary>
    public RelayCommand UpdateFromMineCommand { get; }

    /// <summary>True when the study's copy differs from your library's (D-344, D-351).</summary>
    public bool DiffersFromMine => _owner.MineDiffers(_id);

    /// <summary>The line about your library (MINE, DIFFERS…); empty when it isn't in your library.</summary>
    public string MineText => _owner.MineStatusText(_id);

    /// <summary>True when <see cref="MineText"/> shows.</summary>
    public bool HasMineText => MineText.Length > 0;

    /// <summary>True when edits are allowed (not while running).</summary>
    public bool IsEditable => !_owner.IsRunActive && StudyTemplates.CanEditJourney(_owner.Study, Journey, _owner.Mine);

    /// <summary><see cref="IJourneyCanvasHost.CanEdit"/>.</summary>
    public bool CanEdit => IsEditable;

    /// <summary>True for built-ins and their study copies.</summary>
    public bool IsBuiltIn => StudyDefinitions.IsBuiltIn(Journey);

    /// <summary>True when the study's copy of a built-in differs from it (D-328).</summary>
    public bool IsModified => StudyDefinitions.IsModified(_owner.Study, Journey);

    /// <summary>True for the study's own journeys (DELETE).</summary>
    public bool IsOwn => StudyTemplates.IsEditableJourney(_owner.Study, Journey);

    /// <summary>Where it came from.</summary>
    public string SourceText => _owner.IsMineOnly(_id)
        ? "From your library. Editing it makes this study's own copy; SAVE TO MY LIBRARY puts changes back in your library."
        : IsOwn
        ? "Your journey, saved in this study. Edits can be undone."
        : IsModified
            ? "Built-in · MODIFIED for this study. RESET TO BUILT-IN puts it back."
            : "Built-in. Editing it makes this study's own version (RESET TO BUILT-IN puts it back); DUPLICATE makes a separate copy.";

    /// <summary>How many mix lines (and profiles, as their default) use it.</summary>
    public string UsageText
    {
        get
        {
            int uses = _owner.JourneyUseCount(_id);
            int defaults = _owner.JourneyDefaultCount(_id);
            string lines = uses switch
            {
                0 => "Not used by any mix line yet: pick it on a spawn zone's mix line in SELECTION.",
                1 => "Used by 1 mix line.",
                _ => string.Create(CultureInfo.InvariantCulture, $"Used by {uses} mix lines."),
            };
            return defaults switch
            {
                0 => lines,
                1 => lines + " The default journey of 1 profile.",
                _ => lines + string.Create(CultureInfo.InvariantCulture, $" The default journey of {defaults} profiles."),
            };
        }
    }

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Journey.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A journey needs a name.";
                OnPropertyChanged();
                return;
            }

            Edit("Rename journey", j =>
            {
                j.Name = trimmed;
                return true;
            });
        }
    }

    /// <summary>Description (the LIBRARY tooltip).</summary>
    public string Description
    {
        get => Journey.Description;
        set => Edit("Change journey description", j =>
        {
            j.Description = (value ?? string.Empty).Trim();
            return true;
        });
    }

    /// <summary>The whole journey on one line.</summary>
    public string Summary => Journey.Summarise();

    /// <summary>Redraw counter for the canvas (bumped on every refresh).</summary>
    public int Revision => _revision;

    /// <summary>One row per node: those reachable from Spawn first, in order, then any no one reaches.</summary>
    public ObservableCollection<JourneyNodeRowViewModel> Nodes { get; } = [];

    /// <summary>The node clicked on the canvas or in the list (null = none).</summary>
    public Guid? SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            if (SetProperty(ref _selectedNodeId, value))
            {
                foreach (JourneyNodeRowViewModel row in Nodes)
                {
                    row.IsSelected = row.Id == value;
                }

                RebuildEditor();
            }
        }
    }

    /// <summary>The selected node's editor, or null.</summary>
    public JourneyNodeEditorViewModel? SelectedNode => _nodeEditor;

    /// <summary>True when a node is selected.</summary>
    public bool HasSelectedNode => _nodeEditor is not null;

    /// <summary>What CHECK will say about the graph's structure (D-302–D-306), one line each; empty when it's sound.</summary>
    public IReadOnlyList<string> Problems =>
        [.. JourneyGraph.Problems(Journey).Select(p => (p.IsError ? "ERROR · " : "WARNING · ") + Capitalise(p.Message) + ".")];

    /// <summary>True when <see cref="Problems"/> has lines.</summary>
    public bool HasProblems => Problems.Count > 0;

    /// <summary>True while PICK IN PLAN is adding to a node of this journey (D-324).</summary>
    public bool IsPicking => _owner.IsJourneyPicking;

    /// <summary>Re-reads everything and redraws the canvas.</summary>
    public void Refresh()
    {
        JourneyGraph.Ensure(Journey);
        _revision++;
        RebuildNodes();
        RebuildOverlay();
        RebuildEditor();
        OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ run overlay (round 019, D-331–D-338)

    /// <inheritdoc />
    public JourneyOverlay Overlay => _overlay;

    /// <summary>True when the run overlay shows the run (ACTUAL with a run's results); false for PREDICTED or no run.</summary>
    internal bool ShowsActual => !_overlay.IsPredicted && ActualFlow is not null;

    /// <summary>This journey's counts for the filtered profile from the latest results, or null (no run, or not on the run).</summary>
    internal JourneyFlowResult? ActualFlow => _owner.OverlayRunner is null ? null : _owner.OverlayResults.FlowFor(_id, _overlayProfile);

    /// <summary>The profile filter's label for SELECTION ("all profiles" or a name).</summary>
    internal string OverlayProfileName => _overlayProfile ?? "all profiles";

    /// <summary>
    /// Rebuilds the overlay when anything it shows changed (a results tick, the run state, the mode or filter, the
    /// prediction's model) and redraws the canvas only (D-331; no layout).
    /// </summary>
    public void RefreshOverlay()
    {
        var key = (_owner.OverlayResults.Version, _owner.OverlayRunner, _owner.OverlayRunState, _overlayPredicted, _overlayProfile, _owner.PredictionModel);
        if (_overlayKey is { } last && last.Equals(key))
        {
            return;
        }

        RebuildOverlay();
        _revision++;
        OnPropertyChanged(nameof(Revision));
        _nodeEditor?.RefreshRun();
    }

    /// <inheritdoc />
    public void SetOverlayPredicted(bool predicted)
    {
        if (predicted && PredictedShares(out _) is null)
        {
            _owner.Ticker = "PREDICTED ▸ press CHECK or PLAY first: the prediction comes from the compiled study.";
            return;
        }

        _overlayPredicted = predicted;
        RefreshOverlay();
    }

    /// <inheritdoc />
    public IReadOnlyList<JourneyChipOption> OverlayProfileOptions()
    {
        var options = new List<JourneyChipOption> { new("All profiles", string.Empty, _overlayProfile is null) };
        foreach (string name in JourneyProfileNames())
        {
            options.Add(new JourneyChipOption(name, name, string.Equals(name, _overlayProfile, StringComparison.Ordinal)));
        }

        return options;
    }

    /// <inheritdoc />
    public void ChooseOverlayProfile(JourneyChipOption option)
    {
        string name = option.Value as string ?? string.Empty;
        _overlayProfile = name.Length == 0 ? null : name;
        RefreshOverlay();
    }

    /// <summary>The profiles on the mix lines that follow this journey (directly or as their default, D-304), in study order.</summary>
    private List<string> JourneyProfileNames()
    {
        var names = new List<string>();
        foreach (Region region in Study.Regions)
        {
            foreach (EmissionRule rule in region.EmissionRules)
            {
                foreach (MixLine line in rule.Mix ?? [])
                {
                    if (line is not null && StudyTemplates.JourneyIdFor(Study, line) == _id)
                    {
                        string name = StudyTemplates.ProfileOrFallback(Study, line.ProfileId).Name;
                        if (!names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
        }

        return names;
    }

    /// <summary>
    /// The predicted shares for the filtered profile (D-334) from the last compiled model, or null when there's no
    /// model, the journey isn't on it, or no mix line of that profile follows it.
    /// </summary>
    internal JourneyShares? PredictedShares(out SimJourney? journey)
    {
        journey = null;
        if (_owner.PredictionModel is not { } model)
        {
            return null;
        }

        int j = -1;
        for (int k = 0; k < model.Journeys.Count; k++)
        {
            if (model.Journeys[k].JourneyId == _id)
            {
                j = k;
                break;
            }
        }

        int profile = -1;
        if (_overlayProfile is { } name)
        {
            for (int k = 0; k < model.Profiles.Count; k++)
            {
                if (string.Equals(model.Profiles[k].Template.Name, name, StringComparison.Ordinal))
                {
                    profile = k;
                    break;
                }
            }

            if (profile < 0)
            {
                return null;
            }
        }

        if (j < 0)
        {
            return null;
        }

        journey = model.Journeys[j];
        return CapacityCheck.PredictedShares(model, j, profile);
    }

    private void RebuildOverlay()
    {
        _overlayKey = (_owner.OverlayResults.Version, _owner.OverlayRunner, _owner.OverlayRunState, _overlayPredicted, _overlayProfile, _owner.PredictionModel);
        CultureInfo c = CultureInfo.InvariantCulture;
        JourneyTemplate journey = Journey;
        JourneyShares? shares = PredictedShares(out SimJourney? compiled);
        bool predicted = _overlayPredicted && shares is not null && compiled is not null;
        var wires = new Dictionary<(Guid From, int Port), JourneyWireLabel>();
        var lines = new Dictionary<Guid, string>();
        string state = _owner.OverlayRunState;
        if (predicted)
        {
            state = "PREDICTED · FROM " + _owner.PredictionSource;
            if (compiled!.SpawnId != Guid.Empty)
            {
                wires[(compiled.SpawnId, 0)] = new JourneyWireLabel("100%", 1);
            }

            for (int n = 0; n < compiled.Nodes.Length && n < compiled.NodeIds.Length; n++)
            {
                Guid id = compiled.NodeIds[n];
                lines[id] = Percent(shares!.Nodes[n]) + " reach";
                for (int port = 0; port < compiled.Nodes[n].Next.Length; port++)
                {
                    double share = shares.WireAt(n, port);
                    if (share > 0.0005 || JourneyGraph.Next(journey, id, port) is not null)
                    {
                        wires[(id, port)] = new JourneyWireLabel(Percent(share), share);
                    }
                }
            }
        }
        else if (ActualFlow is { } flow)
        {
            long started = flow.Started;
            foreach (JourneyFlowWire wire in flow.Wires)
            {
                if (wire.To == Guid.Empty && wire.Count == 0)
                {
                    // An unwired output no one took: nothing to say.
                    continue;
                }

                double share = started > 0 ? (double)wire.Count / started : 0;
                wires[(wire.From, wire.Port)] = new JourneyWireLabel(string.Create(c, $"{wire.Count:N0} · {Percent(share)}"), share);
            }

            if (JourneyGraph.Spawn(journey) is { } spawn)
            {
                lines[spawn.Id] = string.Create(c, $"{started:N0} started");
            }

            foreach (JourneyNodeStats node in flow.Nodes)
            {
                lines[node.NodeId] = NodeLine(JourneyGraph.Find(journey, node.NodeId), node);
            }
        }

        _overlay = new JourneyOverlay
        {
            StateText = state,
            IsPredicted = predicted,
            HasPrediction = shares is not null,
            ProfileText = (_overlayProfile ?? "All profiles") + " ▾",
            Wires = wires,
            NodeLines = lines,
        };
    }

    /// <summary>A node's stats line (D-335), by kind: devices waits and give-ups, dwell time, zones and sinks finished.</summary>
    internal static string NodeLine(JourneyNode? node, JourneyNodeStats stats)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string reached = string.Create(c, $"{stats.Reached:N0} in");
        switch (node?.Kind)
        {
            case JourneyNodeKind.Device:
            {
                string line = stats.Waited > 0 ? string.Create(c, $"{reached} · {stats.MeanWaitS:0} s mean / {stats.P95WaitS:0} s p95") : reached;
                line += stats.GaveUp > 0 ? string.Create(c, $" · {stats.GaveUp:N0} gave up") : string.Empty;
                line += stats.Balked > 0 ? string.Create(c, $" · {stats.Balked:N0} balked") : string.Empty;
                return line;
            }

            case JourneyNodeKind.Dwell:
                return stats.Dwelled > 0 ? string.Create(c, $"{reached} · {stats.MeanDwellS:0} s mean dwell") : reached;
            case JourneyNodeKind.PassZone:
                return string.Create(c, $"{reached} · {stats.Finished:N0} through");
            case JourneyNodeKind.Sink:
                return string.Create(c, $"{reached} · {stats.Finished:N0} left");
            default:
                return reached;
        }
    }

    /// <summary>A 0–1 share as a whole percent ("38%"; "<1%" for small non-zero shares).</summary>
    internal static string Percent(double share) =>
        share > 0 && share < 0.005 ? "<1%" : (Math.Clamp(share, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Name of a zone in the study, for pinned and picked zones.</summary>
    internal string ZoneName(Guid id) => _owner.Study.FindRegion(id)?.Name ?? "(deleted)";

    /// <summary>Name of a device in the study, for picked devices.</summary>
    internal string DeviceName(Guid id) => _owner.Study.FindDevice(id)?.Name ?? "(deleted)";

    /// <summary>The study, for the node editor's candidate lists.</summary>
    internal Study Study => _owner.Study;

    /// <summary>One undoable edit of the journey (D-328: a built-in's first edit makes the study's copy).</summary>
    internal bool Edit(string description, Func<JourneyTemplate, bool> change) => _owner.EditJourney(_id, description, change);

    /// <summary>Replaces a node's settings (one undo step).</summary>
    internal void ReplaceNode(JourneyNode node, string description) => Edit(description, j => JourneyEdits.Replace(j, node));

    /// <summary>Shows why an edit was refused.</summary>
    internal void Reject(string message)
    {
        _owner.Ticker = message;
        _nodeEditor?.Refresh();
    }

    /// <summary>Starts PICK IN PLAN for a node (D-324).</summary>
    internal void StartPick(Guid nodeId) => _owner.StartJourneyPick(_id, nodeId);

    /// <summary>Ends PICK IN PLAN and returns to the graph.</summary>
    internal void FinishPick() => _owner.FinishJourneyPick();

    // ------------------------------------------------------------------ IJourneyCanvasHost

    /// <inheritdoc />
    public IReadOnlyList<string> ChipTexts(JourneyNode node)
    {
        string tag = node.TargetTag?.Trim() ?? string.Empty;
        switch (node.Kind)
        {
            case JourneyNodeKind.Device:
                return [$"Use: {TargetText(node, tag.Length > 0 ? $"any {tag}" : "type?")} ▾", $"By: {ChooseByLabel(node.ChooseBy)} ▾"];
            case JourneyNodeKind.PassZone or JourneyNodeKind.Dwell:
                return [$"Zone: {TargetText(node, tag.Length > 0 ? $"tagged {tag}" : node.Kind == JourneyNodeKind.Dwell ? "any dwell zone" : "any zone")} ▾", $"By: {ChooseByLabel(node.ChooseBy)} ▾"];
            case JourneyNodeKind.Sink:
                return node.SinkKind switch
                {
                    JourneySinkKind.Zone => [$"Leave: {SinkKindLabel(node.SinkKind)} ▾", $"At: {TargetText(node, tag.Length > 0 ? $"sinks tagged {tag}" : "any sink")} ▾", $"By: {ChooseByLabel(node.ChooseBy)} ▾"],
                    JourneySinkKind.BoardTrain => [$"Leave: {SinkKindLabel(node.SinkKind)} ▾", $"Service: {(tag.Length > 0 ? tag : "any")} ▾"],
                    _ => [$"Leave: {SinkKindLabel(node.SinkKind)} ▾"],
                };
            case JourneyNodeKind.TagCheck:
                return [$"Tag: {(tag.Length > 0 ? tag : "?")} ▾"];
            case JourneyNodeKind.SetTag:
                return [$"{(node.RemoveTag ? "Remove" : "Grant")}: {(tag.Length > 0 ? tag : "?")} ▾"];
            default:
                return [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<JourneyChipOption> ChipOptions(Guid nodeId, int chip)
    {
        if (JourneyGraph.Find(Journey, nodeId) is not { } node)
        {
            return [];
        }

        string tag = node.TargetTag?.Trim() ?? string.Empty;
        bool byTag = !node.HasPicks;
        List<JourneyChipOption> TagOptions(IEnumerable<string> tags, string anyLabel, bool pick, string prefix = "")
        {
            var options = new List<JourneyChipOption>();
            if (anyLabel.Length > 0)
            {
                options.Add(new JourneyChipOption(anyLabel, "tag:", byTag && tag.Length == 0));
            }

            options.AddRange(tags.Select(t => new JourneyChipOption(prefix + t, "tag:" + t, byTag && string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))));
            if (pick)
            {
                options.Add(new JourneyChipOption(node.HasPicks ? $"Picked ({node.Picks!.Count.ToString(CultureInfo.InvariantCulture)}) · PICK IN PLAN…" : "PICK IN PLAN…", "pick", node.HasPicks));
            }

            return options;
        }

        List<JourneyChipOption> ChooseOptions() =>
            [.. Enum.GetValues<JourneyChooseBy>().Select(v => new JourneyChipOption(ChooseByLabel(v), v, v == node.ChooseBy))];

        switch (node.Kind)
        {
            case JourneyNodeKind.Device:
                return chip == 0 ? TagOptions(StudyDefinitions.KnownDeviceTypes(Study), string.Empty, pick: true, prefix: "any ") : ChooseOptions();
            case JourneyNodeKind.PassZone or JourneyNodeKind.Dwell:
                return chip == 0 ? TagOptions(ZoneTags(sinksOnly: false), node.Kind == JourneyNodeKind.Dwell ? "any dwell zone" : "any zone", pick: true) : ChooseOptions();
            case JourneyNodeKind.Sink when chip == 0:
                return [.. Enum.GetValues<JourneySinkKind>().Select(v => new JourneyChipOption(SinkKindLabel(v), v, v == node.SinkKind))];
            case JourneyNodeKind.Sink when node.SinkKind == JourneySinkKind.Zone:
                return chip == 1 ? TagOptions(ZoneTags(sinksOnly: true), "any sink", pick: true) : ChooseOptions();
            case JourneyNodeKind.Sink when node.SinkKind == JourneySinkKind.BoardTrain:
                return TagOptions(TrainTags(), "any", pick: false);
            case JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag:
                return TagOptions(KnownTags(), string.Empty, pick: false);
            default:
                return [];
        }
    }

    /// <inheritdoc />
    public void ChooseChip(Guid nodeId, int chip, JourneyChipOption option)
    {
        if (JourneyGraph.Find(Journey, nodeId) is not { } node)
        {
            return;
        }

        switch (option.Value)
        {
            case "pick":
                SelectedNodeId = nodeId;
                StartPick(nodeId);
                return;
            case string text when text.StartsWith("tag:", StringComparison.Ordinal):
                ReplaceNode(node with { TargetTag = text[4..], Picks = node.Kind is JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag ? node.Picks : null, ZoneId = null }, "Change node target");
                return;
            case JourneyChooseBy rule:
                ReplaceNode(node with { ChooseBy = rule }, "Change how the node chooses");
                return;
            case JourneySinkKind kind:
                ReplaceNode(node with { SinkKind = kind, TargetTag = kind == node.SinkKind ? node.TargetTag : string.Empty, Picks = kind == node.SinkKind ? node.Picks : null, ZoneId = null }, "Change how people leave");
                return;
        }
    }

    /// <inheritdoc />
    public void MoveNode(Guid id, double x, double y) => Edit("Move journey node", j => JourneyEdits.Move(j, id, x, y));

    /// <inheritdoc />
    public void Connect(Guid from, int port, Guid to)
    {
        string? refusal = null;
        Edit("Wire journey nodes", j =>
        {
            refusal = JourneyEdits.Connect(j, from, port, to);
            return refusal is null;
        });
        if (refusal is not null)
        {
            _owner.Ticker = refusal;
        }
    }

    /// <inheritdoc />
    public void Disconnect(Guid from, int port) => Edit("Remove journey wire", j => JourneyEdits.Disconnect(j, from, port));

    /// <inheritdoc />
    public void RemoveNode(Guid id)
    {
        if (JourneyGraph.Find(Journey, id) is { Kind: JourneyNodeKind.Spawn })
        {
            _owner.Ticker = "Spawn stays: every journey starts there.";
            return;
        }

        if (Edit("Delete journey node", j => JourneyEdits.Remove(j, id)) && _selectedNodeId == id)
        {
            SelectedNodeId = null;
        }
    }

    /// <inheritdoc />
    public Guid? AddNode(JourneyNodeKind kind, double x, double y, Guid? wireFrom, int wirePort)
    {
        string device = StudyDefinitions.KnownDeviceTypes(Study).FirstOrDefault() ?? "Gate";
        JourneyNode node = JourneyEdits.Create(kind, Math.Round(x, 1), Math.Round(y, 1), device);
        string? refusal = null;
        bool done = Edit($"Add {JourneyCanvas.KindName(kind).ToLowerInvariant()} node", j =>
        {
            if (JourneyEdits.Add(j, node) is null)
            {
                return false;
            }

            if (wireFrom is { } from)
            {
                refusal = JourneyEdits.Connect(j, from, wirePort, node.Id);
            }

            return true;
        });
        if (refusal is not null)
        {
            _owner.Ticker = refusal;
        }

        return done ? node.Id : null;
    }

    // ------------------------------------------------------------------ labels and option lists

    /// <summary>Menu / chip label of a choose-by rule (D-319).</summary>
    public static string ChooseByLabel(JourneyChooseBy rule) => rule switch
    {
        JourneyChooseBy.AlongTheWay => "Along the way",
        JourneyChooseBy.Nearest => "Nearest",
        JourneyChooseBy.FirstFree => "First free",
        JourneyChooseBy.RandomWeighted => "Random by weight",
        _ => "Quickest",
    };

    /// <summary>Menu / chip label of a sink kind (D-303).</summary>
    public static string SinkKindLabel(JourneySinkKind kind) => kind switch
    {
        JourneySinkKind.Zone => "a sink zone",
        JourneySinkKind.BoardTrain => "on a train",
        _ => "rule's sinks",
    };

    /// <summary>Zone tags in the study (sink zones only when asked).</summary>
    internal IReadOnlyList<string> ZoneTags(bool sinksOnly) =>
        [.. Study.Regions.Where(r => r.Kind == RegionKind.Zone && (!sinksOnly || r.IsSink)).Select(r => r.Tag ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Train service tags (or names when untagged).</summary>
    internal IReadOnlyList<string> TrainTags() =>
        [.. Study.TrainServices.Where(t => t is not null).Select(t => string.IsNullOrWhiteSpace(t.Tag) ? t.Name : t.Tag)
            .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Person tags the study uses: spawn tags, grants, tag checks and set tags across its journeys.</summary>
    internal IReadOnlyList<string> KnownTags() =>
        [.. Study.Regions.SelectMany(r => r.EmissionRules).SelectMany(r => r.Mix ?? []).SelectMany(m => m?.SpawnTags ?? []).Select(t => t?.Tag ?? string.Empty)
            .Concat(StudyTemplates.AvailableJourneys(Study).SelectMany(j => j.Nodes ?? []).Where(n => n is not null)
                .Select(n => n.Kind is JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag ? n.TargetTag : n.GrantsTag))
            .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    private string TargetText(JourneyNode node, string byTag)
    {
        if (!node.HasPicks)
        {
            return node.ZoneId is { } pinned ? ZoneName(pinned) : byTag;
        }

        Guid first = node.Picks![0].Id;
        string name = node.Kind == JourneyNodeKind.Device ? DeviceName(first) : ZoneName(first);
        return node.Picks.Count == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} +{node.Picks.Count - 1}");
    }

    private void RebuildNodes()
    {
        Nodes.Clear();
        JourneyTemplate journey = Journey;
        IReadOnlyList<JourneyNode> reachable = JourneyGraph.Reachable(journey);
        var order = new List<JourneyNode>(reachable);
        order.AddRange(journey.Nodes.Where(n => n is not null && !reachable.Contains(n)));
        foreach (JourneyNode node in order)
        {
            Nodes.Add(new JourneyNodeRowViewModel(this, node, reachable.Contains(node)) { IsSelected = node.Id == _selectedNodeId });
        }

        if (_selectedNodeId is { } id && !Nodes.Any(n => n.Id == id))
        {
            _selectedNodeId = null;
            OnPropertyChanged(nameof(SelectedNodeId));
        }
    }

    private void RebuildEditor()
    {
        _nodeEditor = _selectedNodeId is { } id && JourneyGraph.Find(Journey, id) is not null ? new JourneyNodeEditorViewModel(this, id) : null;
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelectedNode));
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>One node in SELECTION's node list: its kind and title; a click selects it.</summary>
public sealed class JourneyNodeRowViewModel : ObservableObject
{
    private readonly JourneyViewModel _journey;
    private bool _isSelected;

    /// <summary>Wraps a node.</summary>
    public JourneyNodeRowViewModel(JourneyViewModel journey, JourneyNode node, bool isReachable)
    {
        _journey = journey;
        Node = node;
        IsReachable = isReachable;
        SelectCommand = new RelayCommand(() => _journey.SelectedNodeId = Node.Id);
    }

    /// <summary>The node.</summary>
    public JourneyNode Node { get; }

    /// <summary>Its id.</summary>
    public Guid Id => Node.Id;

    /// <summary>Selects it (and highlights it on the canvas).</summary>
    public RelayCommand SelectCommand { get; }

    /// <summary>True when Spawn's wires lead to it.</summary>
    public bool IsReachable { get; }

    /// <summary>True when selected.</summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Kind, upper case (e.g. "TAG CHECK").</summary>
    public string KindText => JourneyCanvas.KindName(Node) + (IsReachable ? string.Empty : " · NOT WIRED FROM SPAWN");

    /// <summary>The node's title.</summary>
    public string Title => Node.Title;
}
