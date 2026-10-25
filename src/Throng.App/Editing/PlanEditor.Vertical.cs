using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>
/// Vertical-circulation half of the plan editor (round 004): the CONNECT tool (D-054, D-058, D-059), the SHAFT tool
/// (D-056), the connector rotate grip (D-063, D-073), the clipboard for mixed selections and Copy to Levels (D-060).
/// </summary>
public sealed partial class PlanEditor
{
    /// <summary>Distance of a connector's rotate grip beyond its far end, in DIPs.</summary>
    public const double RotateGripOffsetPx = 18;

    /// <summary>Angle step for aiming and rotating connectors, in degrees (D-073). Shift turns it off.</summary>
    public const double AngleStepDeg = 15;

    private readonly List<Region> _clipRegions = [];
    private readonly List<Connector> _clipConnectors = [];
    private readonly List<Shaft> _clipShafts = [];
    private readonly List<Device> _clipDevices = [];
    private Guid? _clipLevelId;

    private ConnectorTemplate? _connectPreset;
    private Guid? _connectTopLevelId;
    private TurnSide _connectSide = TurnSide.Left;
    private double _aimAngle;
    private List<ConnectorPreview>? _connectPreview;
    private string? _connectPreviewText;

    private Connector? _rotateConnector;
    private Connector? _rotatePartner;
    private Device? _rotateDevice;
    private Underlay? _rotateUnderlay;
    private double _rotateBaseAngle;
    private Point2 _rotatePivot;
    private double _rotateStartAngle;
    private double _rotateDelta;
    private Dictionary<Guid, IReadOnlyList<Point2>>? _rotatePreview;

    /// <summary>Preset placed by the CONNECT tool (stairs, escalators and ramps only, D-074).</summary>
    public ConnectorTemplate? ConnectPreset
    {
        get => _connectPreset;
        set
        {
            _connectPreset = value is { ChangesLevel: true } ? value : null;
            RefreshConnectPreview();
        }
    }

    /// <summary>Level the CONNECT tool links the active level to (must be above it).</summary>
    public Guid? ConnectTopLevelId
    {
        get => _connectTopLevelId;
        set
        {
            _connectTopLevelId = value;
            RefreshConnectPreview();
        }
    }

    /// <summary>Side the next switchback's return run goes (Tab flips it while placing, D-058).</summary>
    public TurnSide ConnectSide => _connectSide;

    /// <summary>Connector(s) previewed at the cursor while placing; empty when nothing to show.</summary>
    public IReadOnlyList<ConnectorPreview> ConnectPreviews => (IReadOnlyList<ConnectorPreview>?)_connectPreview ?? Array.Empty<ConnectorPreview>();

    /// <summary>One-line description of the previewed connector (risers, flights, length) or why it can't be placed.</summary>
    public string? ConnectPreviewText => _tool == EditorTool.Connect ? _connectPreviewText : null;

    /// <summary>True once the bottom landing has been clicked and the CONNECT tool is aiming.</summary>
    public bool IsAiming => _tool == EditorTool.Connect && _points.Count == 1;

    /// <summary>Footprints of connectors being rotated (by id), or null when not rotating.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Point2>>? RotatePreview => _gesture == Gesture.Rotate ? _rotatePreview : null;

    /// <summary>The new angle while rotating, for the readout.</summary>
    public double? RotateAngle => _gesture == Gesture.Rotate ? ConnectorFactory.NormaliseAngle(_rotateBaseAngle + _rotateDelta) : null;

    /// <summary>
    /// Rotate grip of the single selected connector, device or unlocked underlay (D-063, D-084, D-090): the grip
    /// and the point it hangs from.
    /// </summary>
    public (Point2 Grip, Point2 Anchor)? RotateGrip
    {
        get
        {
            if (!CanEdit || _selection.Count != 1)
            {
                return null;
            }

            Guid id = _selection.First();
            double offset = RotateGripOffsetPx * _metresPerDip;
            Study study = _host.Study;
            if (study.FindConnector(id) is { } connector)
            {
                return ConnectorLayout.Compute(connector, study) is { IsSuccess: true, Value: { } layout }
                    ? (layout.FarEnd + (layout.BottomAxis * offset), layout.FarEnd)
                    : null;
            }

            if (study.FindDevice(id) is { } device)
            {
                Point2 front = DeviceGeometry.ServicePoint(device);
                return (front + (device.Facing() * (offset + 0.3)), front);
            }

            if (study.FindUnderlay(id) is { IsLocked: false } underlay)
            {
                Point2[] c = UnderlayGeometry.Corners(underlay);
                Point2 right = (c[1] + c[2]) * 0.5;
                Point2 axis = (c[1] - c[0]).Normalised();
                return (right + (axis * offset), right);
            }

            return null;
        }
    }

    /// <summary>True when the clipboard holds anything.</summary>
    public bool HasClipboard => _clipRegions.Count + _clipConnectors.Count + _clipShafts.Count + _clipDevices.Count > 0;

    // ------------------------------------------------------------------ CONNECT tool (D-054)

    /// <summary>Checks a preset and a target level above the active one are chosen; flashes why not.</summary>
    private bool CheckConnectReady()
    {
        if (_connectPreset is null)
        {
            RaiseFlash("PICK A STAIR, ESCALATOR OR RAMP PRESET");
            return false;
        }

        Level? active = _host.ActiveLevelModel;
        Level? top = _connectTopLevelId is { } id ? _host.Study.FindLevel(id) : null;
        if (active is null || top is null || top.ElevationM <= active.ElevationM + 1e-6)
        {
            RaiseFlash("PICK A TARGET LEVEL ABOVE THIS ONE ▸ CONNECT STARTS ON THE LOWER LEVEL");
            return false;
        }

        return true;
    }

    private void ConnectDown(Point2 raw, bool shift)
    {
        if (!CheckConnectReady())
        {
            return;
        }

        if (_points.Count == 0)
        {
            _points.Add(SnapDrawPoint(raw, shift).Point);
            UpdateConnectAim(raw, shift);
            _host.ShowMessage("CONNECT ▸ aim up the run and click. Tab flips a switchback's return side; type an angle for an exact direction.");
            return;
        }

        CommitConnector(_aimAngle);
    }

    /// <summary>
    /// Updates the aim (15° steps unless Shift, D-073) and the preview. Before the first click the preview follows
    /// the cursor at the current aim, so the footprint can be judged before committing to a landing point.
    /// </summary>
    private void UpdateConnectAim(Point2 cursor, bool shift)
    {
        if (_tool != EditorTool.Connect)
        {
            return;
        }

        if (_points.Count == 1)
        {
            Point2 toward = cursor - _points[0];
            if (toward.Length > 2 * _metresPerDip)
            {
                double angle = ConnectorFactory.AngleOf(toward);
                _aimAngle = shift ? angle : ConnectorFactory.SnapAngle(angle, AngleStepDeg);
            }
        }

        BuildConnectPreview(_points.Count == 1 ? _points[0] : cursor);
    }

    private void RefreshConnectPreview()
    {
        if (_tool == EditorTool.Connect && _hasCursor)
        {
            BuildConnectPreview(_points.Count == 1 ? _points[0] : _snap.Point);
        }
        else
        {
            ClearConnectPreview();
        }

        RaiseOverlay();
    }

    private void ClearConnectPreview()
    {
        _connectPreview = null;
        _connectPreviewText = null;
    }

    private void BuildConnectPreview(Point2 at)
    {
        ClearConnectPreview();
        Level? active = _host.ActiveLevelModel;
        Level? top = _connectTopLevelId is { } id ? _host.Study.FindLevel(id) : null;
        if (_connectPreset is null || active is null || top is null)
        {
            _connectPreviewText = _connectPreset is null ? "Pick a preset in the DRAW strip or double-click one in LIBRARY ▸ VERTICAL." : "Pick a target level above this one.";
            return;
        }

        List<Connector> connectors = ConnectorFactory.FromTemplate(_host.Study, _connectPreset, active.Id, top.Id, at, _aimAngle, _connectSide);
        var previews = new List<ConnectorPreview>(connectors.Count);
        foreach (Connector connector in connectors)
        {
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, _host.Study);
            if (!layout.IsSuccess || layout.Value is null)
            {
                _connectPreviewText = layout.Error;
                return;
            }

            previews.Add(new ConnectorPreview(connector, layout.Value));
        }

        _connectPreview = previews;
        ConnectorLayout first = previews[0].Layout;
        string angle = string.Create(CultureInfo.InvariantCulture, $"{_aimAngle:0.#}°");
        string side = connectors[0].IsSwitchback ? $" · returns {(_connectSide == TurnSide.Left ? "left" : "right")}" : string.Empty;
        _connectPreviewText = $"{_connectPreset.Name} ▸ {top.Name} · {first.Describe(_connectPreset.Kind)} · {angle}{side}";
    }

    /// <summary>Places the previewed connector(s) at the bottom landing with the given angle.</summary>
    private bool CommitConnector(double angleDeg)
    {
        if (!CheckConnectReady() || _points.Count != 1 || _host.ActiveLevelModel is not { } active || _connectTopLevelId is not { } topId)
        {
            return false;
        }

        ConnectorTemplate preset = _connectPreset!;
        List<Connector> placed = ConnectorFactory.FromTemplate(_host.Study, preset, active.Id, topId, _points[0], angleDeg, _connectSide);
        foreach (Connector connector in placed)
        {
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, _host.Study);
            if (!layout.IsSuccess)
            {
                RaiseFlash((layout.Error ?? "CAN'T PLACE THIS CONNECTOR").ToUpperInvariant());
                return false;
            }
        }

        // Studies embed copies of the templates they use (D-016). Embedding is idempotent and harmless to keep
        // after an undo, so it sits outside the undo step.
        TemplateBase embedded = _host.Study.Library.AddCopy(preset, TemplateSource.Embedded);
        foreach (Connector connector in placed)
        {
            connector.TemplateId = embedded.Id;
        }

        string what = placed.Count == 1 ? placed[0].Name : $"{placed[0].Name} + {placed[1].Name}";
        _host.Execute(new AddItemsAction<Connector>($"Place {what}", _host.Study.Connectors, placed, _host.NotifyModelChanged));
        _points.Clear();
        _aimAngle = ConnectorFactory.NormaliseAngle(angleDeg);
        SetSelection(placed.Select(c => c.Id));
        RefreshConnectPreview();
        return true;
    }

    /// <summary>Tab: flips the switchback side while placing, or of the single selected switchback. False when unused.</summary>
    private bool FlipSide()
    {
        if (_tool == EditorTool.Connect)
        {
            _connectSide = _connectSide == TurnSide.Left ? TurnSide.Right : TurnSide.Left;
            RefreshConnectPreview();
            _host.ShowMessage(_connectSide == TurnSide.Left ? "Switchback returns on the LEFT." : "Switchback returns on the RIGHT.");
            return true;
        }

        if (CanEdit && _selection.Count == 1 && _host.Study.FindConnector(_selection.First()) is { IsSwitchback: true } connector)
        {
            Connector next = connector.Clone();
            next.ReturnSide = connector.ReturnSide == TurnSide.Left ? TurnSide.Right : TurnSide.Left;
            _host.Execute(StateActions.Connectors($"Flip {connector.Name}", [(connector, next)], _host.NotifyModelChanged));
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ SHAFT tool (D-056)

    /// <summary>
    /// Adds a shaft drawn as a rectangle. New shafts span every level (lowest to highest); base and top are then
    /// set in the SELECTION panel.
    /// </summary>
    private bool CommitShaft(IReadOnlyList<Point2> outline)
    {
        IReadOnlyList<Level> levels = _host.Study.LevelsAscending();
        if (levels.Count == 0)
        {
            return false;
        }

        Result<List<Point2>> valid = ShapeValidator.Validate(outline, closed: true);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return false;
        }

        Shaft shaft = ConnectorFactory.CreateShaft(_host.Study, levels[0].Id, levels[^1].Id, valid.Value);
        _host.Execute(new AddItemsAction<Shaft>($"Draw {shaft.Name}", _host.Study.Shafts, [shaft], _host.NotifyModelChanged));
        _points.Clear();
        _gesture = Gesture.None;
        SetSelection([shaft.Id]);
        _host.ShowMessage($"{shaft.Name} spans every level ▸ set its base and top levels in SELECTION.");
        return true;
    }

    // ------------------------------------------------------------------ rotate grip (D-063, D-073)

    private bool TryStartRotate(Point2 raw)
    {
        if (RotateGrip is not { } grip || Point2.Distance(grip.Grip, raw) > GripTolerancePx * _metresPerDip)
        {
            return false;
        }

        Study study = _host.Study;
        Guid id = _selection.First();
        _rotateConnector = study.FindConnector(id);
        _rotatePartner = _rotateConnector is null ? null : study.FindPartner(_rotateConnector);
        _rotateDevice = study.FindDevice(id);
        _rotateUnderlay = study.FindUnderlay(id);
        if (_rotateConnector is { } connector)
        {
            _rotatePivot = ConnectorFactory.Pivot(connector, _rotatePartner);
            _rotateBaseAngle = connector.AngleDeg;
        }
        else if (_rotateDevice is { } device)
        {
            _rotatePivot = device.Position;
            _rotateBaseAngle = device.AngleDeg;
        }
        else if (_rotateUnderlay is { } underlay)
        {
            _rotatePivot = underlay.Centre;
            _rotateBaseAngle = underlay.AngleDeg;
        }
        else
        {
            return false;
        }

        _rotateStartAngle = ConnectorFactory.AngleOf(grip.Grip - _rotatePivot);
        _rotateDelta = 0;
        _rotatePreview = null;
        _gesture = Gesture.Rotate;
        return true;
    }

    private void UpdateRotate(Point2 raw, bool shift)
    {
        if (Point2.Distance(raw, _rotatePivot) < 1e-6)
        {
            return;
        }

        double target = _rotateBaseAngle + (ConnectorFactory.AngleOf(raw - _rotatePivot) - _rotateStartAngle);
        double angle = shift ? ConnectorFactory.NormaliseAngle(target) : ConnectorFactory.SnapAngle(target, AngleStepDeg);
        double delta = angle - _rotateBaseAngle;
        _rotateDelta = delta > 180 ? delta - 360 : delta <= -180 ? delta + 360 : delta;

        _rotatePreview = new Dictionary<Guid, IReadOnlyList<Point2>>();
        foreach (Connector c in RotatingConnectors())
        {
            if (ConnectorLayout.Compute(ConnectorFactory.Rotated(c, _rotatePivot, _rotateDelta), _host.Study) is { IsSuccess: true, Value: { } layout })
            {
                _rotatePreview[c.Id] = layout.Footprint;
            }
        }

        if (_rotateDevice is { } device)
        {
            _rotatePreview[device.Id] = DeviceGeometry.Footprint(DeviceFactory.Rotated(device, _rotatePivot, _rotateDelta));
        }

        if (_rotateUnderlay is { } underlay)
        {
            Underlay next = underlay.Clone();
            next.AngleDeg = ConnectorFactory.NormaliseAngle(underlay.AngleDeg + _rotateDelta);
            _rotatePreview[underlay.Id] = UnderlayGeometry.Corners(next);
        }
    }

    private void CommitRotate()
    {
        double delta = _rotateDelta;
        List<Connector> connectors = RotatingConnectors();
        Device? device = _rotateDevice;
        Underlay? underlay = _rotateUnderlay;
        _rotateConnector = null;
        _rotatePartner = null;
        _rotateDevice = null;
        _rotateUnderlay = null;
        _rotatePreview = null;
        if (Math.Abs(delta) < 1e-6)
        {
            return;
        }

        string angle = string.Create(CultureInfo.InvariantCulture, $"{delta:+0.#;−0.#}°");
        if (connectors.Count > 0)
        {
            _host.Execute(StateActions.Connectors($"Rotate {connectors[0].Name} {angle}", connectors.Select(c => (c, ConnectorFactory.Rotated(c, _rotatePivot, delta))), _host.NotifyModelChanged));
        }
        else if (device is not null)
        {
            _host.Execute(StateActions.Devices($"Rotate {device.Name} {angle}", [(device, DeviceFactory.Rotated(device, _rotatePivot, delta))], _host.NotifyModelChanged));
        }
        else if (underlay is not null)
        {
            Underlay next = underlay.Clone();
            next.AngleDeg = ConnectorFactory.NormaliseAngle(underlay.AngleDeg + delta);
            _host.Execute(StateActions.Underlays($"Rotate {underlay.Name} {angle}", [(underlay, next)], _host.NotifyModelChanged));
        }
    }

    private List<Connector> RotatingConnectors()
    {
        var list = new List<Connector>(2);
        if (_rotateConnector is not null)
        {
            list.Add(_rotateConnector);
        }

        if (_rotatePartner is not null)
        {
            list.Add(_rotatePartner);
        }

        return list;
    }

    // ------------------------------------------------------------------ clipboard (D-041) and Copy to Levels (D-060)

    /// <summary>
    /// Copies the selection (regions, connectors with their partners, shafts, devices) to the editor clipboard.
    /// Underlays aren't copied (their images belong to one level).
    /// </summary>
    public void Copy()
    {
        if (_selection.Count == 0 || _host.ActiveLevelModel is not { } level)
        {
            _host.ShowMessage("Nothing selected to copy.");
            return;
        }

        _clipRegions.Clear();
        _clipConnectors.Clear();
        _clipShafts.Clear();
        _clipDevices.Clear();
        _clipRegions.AddRange(SelectedRegions.Select(r => RegionFactory.Copy(r, r.LevelId, Point2.Zero)));
        _clipConnectors.AddRange(WithPartners(SelectedConnectors).Select(c => c.Clone()));
        _clipShafts.AddRange(SelectedShafts.Select(s => s.Clone()));
        _clipDevices.AddRange(SelectedDevices.Select(d => d.Clone()));
        _clipLevelId = level.Id;
        int count = _clipRegions.Count + _clipConnectors.Count + _clipShafts.Count + _clipDevices.Count;
        _host.ShowMessage(Plural(count, "object") + " copied. Ctrl+V pastes in place on the active level; Ctrl+Shift+V copies to several levels.");
    }

    /// <summary>Copies then deletes the selection.</summary>
    public void Cut()
    {
        if (_selection.Count == 0 || !CheckEditable())
        {
            return;
        }

        Copy();
        RemoveSelection("Cut");
    }

    /// <summary>
    /// Pastes the clipboard at the same plan position on the active level (D-041). Spanning objects keep their
    /// level span relative to the level they were copied from.
    /// </summary>
    public void Paste()
    {
        if (!HasClipboard || !CheckEditable() || _host.ActiveLevelModel is not { } level || _clipLevelId is not { } from)
        {
            return;
        }

        CopySet copies = BuildCopies(new SourceSet(_clipRegions, _clipConnectors, _clipShafts, _clipDevices), from, level.Id, Point2.Zero);
        AddCopies("Paste", [copies]);
        SetSelection(copies.Ids);
        ReportSkipped(copies.Skipped);
    }

    /// <summary>Duplicates the selection on the same levels, offset by one grid step (D-041).</summary>
    public void Duplicate()
    {
        if (_selection.Count == 0 || !CheckEditable() || _host.ActiveLevelModel is not { } level)
        {
            return;
        }

        double step = Snap.GridStepM > 0 ? Snap.GridStepM : 0.5;
        CopySet copies = BuildCopies(SelectionSource(), level.Id, level.Id, new Point2(step, -step));
        AddCopies("Duplicate", [copies]);
        SetSelection(copies.Ids);
    }

    /// <summary>
    /// Pastes the selection in place on each of the given levels as one undo step (D-060). Spanning objects keep
    /// their span; copies that would need a level that doesn't exist are skipped. Returns a ticker message.
    /// </summary>
    public string CopyToLevels(IReadOnlyCollection<Guid> levelIds)
    {
        if (IsLocked)
        {
            return "RUNNING ▸ RESET TO EDIT";
        }

        if (_selection.Count == 0 || _host.ActiveLevelModel is not { } source)
        {
            return "Select something to copy to other levels.";
        }

        SourceSet selection = SelectionSource();
        var sets = new List<CopySet>();
        int skipped = 0;
        foreach (Guid levelId in levelIds)
        {
            if (levelId == source.Id || _host.Study.FindLevel(levelId) is null)
            {
                continue;
            }

            CopySet copies = BuildCopies(selection, source.Id, levelId, Point2.Zero);
            skipped += copies.Skipped;
            sets.Add(copies);
        }

        int added = sets.Sum(s => s.Count);
        if (added == 0)
        {
            return skipped > 0 ? $"Nothing copied: {Plural(skipped, "connector or shaft", "connectors or shafts")} would need a level that doesn't exist." : "Nothing copied.";
        }

        AddCopies($"Copy to {Plural(sets.Count, "level")}", sets);
        string message = $"Copied {Plural(added, "object")} to {Plural(sets.Count, "level")}.";
        return skipped > 0 ? $"{message} Skipped {Plural(skipped, "connector or shaft", "connectors or shafts")} with no level to span." : message;
    }

    /// <summary>Objects to copy.</summary>
    private sealed record SourceSet(IReadOnlyList<Region> Regions, IReadOnlyList<Connector> Connectors, IReadOnlyList<Shaft> Shafts, IReadOnlyList<Device> Devices);

    /// <summary>Copies of a set of objects for one target level.</summary>
    private sealed record CopySet(List<Region> Regions, List<Connector> Connectors, List<Shaft> Shafts, List<Device> Devices, int Skipped)
    {
        public int Count => Regions.Count + Connectors.Count + Shafts.Count + Devices.Count;

        public IEnumerable<Guid> Ids => Regions.Select(r => r.Id).Concat(Connectors.Select(c => c.Id))
            .Concat(Shafts.Select(s => s.Id)).Concat(Devices.Select(d => d.Id));
    }

    private SourceSet SelectionSource() => new(SelectedRegions, WithPartners(SelectedConnectors), SelectedShafts, SelectedDevices);

    private CopySet BuildCopies(SourceSet source, Guid fromLevelId, Guid toLevelId, Point2 offset)
    {
        Study study = _host.Study;
        int skipped = 0;
        List<Region> newRegions = [.. source.Regions.Select(r => ConnectorFactory.CopyRegion(study, r, fromLevelId, toLevelId, offset))];

        List<Guid> ShiftAll(IEnumerable<Guid> ids) =>
            [.. ids.Select(id => ConnectorFactory.ShiftLevel(study, id, fromLevelId, toLevelId)).Where(id => id is not null).Select(id => id!.Value)];

        var pairs = new Dictionary<Guid, Guid>();
        var newConnectors = new List<Connector>();
        foreach (Connector connector in source.Connectors)
        {
            if (ConnectorFactory.ShiftLevels(study, connector.BottomLevelId, connector.TopLevelId, fromLevelId, toLevelId) is not { } span)
            {
                skipped++;
                continue;
            }

            Guid? pairId = null;
            if (connector.PairId is { } oldPair)
            {
                if (!pairs.TryGetValue(oldPair, out Guid newPair))
                {
                    newPair = Guid.NewGuid();
                    pairs[oldPair] = newPair;
                }

                pairId = newPair;
            }

            Connector copy = ConnectorFactory.Copy(connector, span.Bottom, span.Top, offset, pairId);
            copy.ExcludedLevelIds = ShiftAll(connector.ExcludedLevelIds);
            newConnectors.Add(copy);
        }

        var newShafts = new List<Shaft>();
        foreach (Shaft shaft in source.Shafts)
        {
            if (ConnectorFactory.ShiftLevels(study, shaft.BaseLevelId, shaft.TopLevelId, fromLevelId, toLevelId) is not { } span)
            {
                skipped++;
                continue;
            }

            Shaft copy = ConnectorFactory.CopyShaft(shaft, span.Bottom, span.Top, offset);
            copy.NoStopLevelIds = ShiftAll(shaft.NoStopLevelIds);
            newShafts.Add(copy);
        }

        List<Device> newDevices = [.. source.Devices.Select(d => DeviceFactory.Copy(d, toLevelId, offset))];

        // A pair that lost one unit (its partner was skipped) is no longer a pair.
        foreach (Connector connector in newConnectors)
        {
            if (connector.PairId is { } pair && newConnectors.Count(c => c.PairId == pair) < 2)
            {
                connector.PairId = null;
            }
        }

        return new CopySet(newRegions, newConnectors, newShafts, newDevices, skipped);
    }

    private void AddCopies(string verb, IReadOnlyList<CopySet> sets)
    {
        Study study = _host.Study;
        List<Region> regions = [.. sets.SelectMany(s => s.Regions)];
        List<Connector> connectors = [.. sets.SelectMany(s => s.Connectors)];
        List<Shaft> shafts = [.. sets.SelectMany(s => s.Shafts)];
        List<Device> devices = [.. sets.SelectMany(s => s.Devices)];
        int count = regions.Count + connectors.Count + shafts.Count + devices.Count;
        if (count == 0)
        {
            return;
        }

        var actions = new List<IUndoableAction>();
        if (regions.Count > 0)
        {
            actions.Add(new AddRegionsAction(verb, study, regions, null));
        }

        if (connectors.Count > 0)
        {
            actions.Add(new AddItemsAction<Connector>(verb, study.Connectors, connectors, null));
        }

        if (shafts.Count > 0)
        {
            actions.Add(new AddItemsAction<Shaft>(verb, study.Shafts, shafts, null));
        }

        if (devices.Count > 0)
        {
            actions.Add(new AddItemsAction<Device>(verb, study.Devices, devices, null));
        }

        _host.Execute(new CompositeAction($"{verb} {Plural(count, "object")}", actions, _host.NotifyModelChanged));
    }

    private void ReportSkipped(int skipped)
    {
        if (skipped > 0)
        {
            RaiseFlash($"SKIPPED {skipped} ▸ NO LEVEL FOR THEIR SPAN");
        }
    }

    private static string Plural(int count, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural)}");
}
