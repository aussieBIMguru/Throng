using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Editing;
using Throng.App.Services;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.World;
using Throng.IO;
using Throng.Render.Gl;

namespace Throng.App.ViewModels;

/// <summary>
/// Editing half of the main view model (M1): hosts the <see cref="PlanEditor"/>, exposes tools, snap settings,
/// clipboard commands, the CONNECT preset and target level (D-054), Copy to Levels (D-060) and the selection
/// property panels (region, connector, shaft), and computes the net walkable area (D-039).
/// </summary>
public sealed partial class MainViewModel : IEditorHost
{
    private static readonly IReadOnlyList<RegionKind> DrawKinds = [RegionKind.Walkable, RegionKind.Obstacle, RegionKind.Zone];

    private RegionViewModel? _selectedRegion;
    private ConnectorViewModel? _selectedConnector;
    private ShaftViewModel? _selectedShaft;
    private DeviceViewModel? _selectedDevice;
    private UnderlayViewModel? _selectedUnderlay;
    private readonly UnderlayImageCache _underlayImages = new();
    private IReadOnlyList<LevelOption> _levelOptions = [];
    private IReadOnlyList<LevelOption> _connectTargets = [];
    private string _walkableAreaText = "—";
    private int _propertiesTabIndex = PropertiesTabLevel;

    /// <summary>Right-panel tab index: SELECTION (D-045).</summary>
    public const int PropertiesTabSelection = 0;

    /// <summary>Right-panel tab index: LEVEL.</summary>
    public const int PropertiesTabLevel = 1;

    /// <summary>Right-panel tab index: STUDY.</summary>
    public const int PropertiesTabStudy = 2;

    /// <summary>The plan editor (tools, selection, clipboard).</summary>
    public PlanEditor Editor { get; private set; } = null!;

    public RelayCommand CopyCommand { get; private set; } = null!;
    public RelayCommand CutCommand { get; private set; } = null!;
    public RelayCommand PasteCommand { get; private set; } = null!;
    public RelayCommand DuplicateCommand { get; private set; } = null!;
    public RelayCommand DeleteCommand { get; private set; } = null!;
    public RelayCommand SelectAllCommand { get; private set; } = null!;
    public RelayCommand<string?> SetToolCommand { get; private set; } = null!;
    public RelayCommand HideSelectionCommand { get; private set; } = null!;
    public RelayCommand IsolateSelectionCommand { get; private set; } = null!;
    public RelayCommand ResetViewCommand { get; private set; } = null!;
    public RelayCommand CopyToLevelsCommand { get; private set; } = null!;
    public RelayCommand AddUnderlayCommand { get; private set; } = null!;
    public RelayCommand<TemplateItemViewModel?> UseTemplateCommand { get; private set; } = null!;

    // ------------------------------------------------------------------ right panel tabs (D-045)

    /// <summary>Tab labels for the right panel.</summary>
    public IReadOnlyList<string> PropertiesTabs { get; } = ["SELECTION", "LEVEL", "STUDY"];

    /// <summary>Selected right-panel tab. Selecting anything in the viewport switches to SELECTION.</summary>
    public int PropertiesTabIndex
    {
        get => _propertiesTabIndex;
        set
        {
            if (value is < PropertiesTabSelection or > PropertiesTabStudy)
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _propertiesTabIndex, value))
            {
                OnPropertyChanged(nameof(IsSelectionTab));
                OnPropertyChanged(nameof(IsLevelTab));
                OnPropertyChanged(nameof(IsStudyTab));
            }
        }
    }

    public bool IsSelectionTab => _propertiesTabIndex == PropertiesTabSelection;

    public bool IsLevelTab => _propertiesTabIndex == PropertiesTabLevel;

    public bool IsStudyTab => _propertiesTabIndex == PropertiesTabStudy;

    // ------------------------------------------------------------------ temporary hide/isolate (D-046)

    /// <summary>True while anything is temporarily hidden; shows the RESET VIEW chip.</summary>
    public bool HasHiddenRegions => Editor.HiddenCount > 0;

    /// <summary>Label for the RESET VIEW chip.</summary>
    public string ResetViewText => string.Create(CultureInfo.InvariantCulture, $"RESET VIEW ({Editor.HiddenCount} HIDDEN)");

    // ------------------------------------------------------------------ IEditorHost

    /// <inheritdoc />
    public Level? ActiveLevelModel => _activeLevel?.Model;

    /// <inheritdoc />
    public void Execute(IUndoableAction action)
    {
        _undo.Execute(action);
        Ticker = action.Description;
    }

    /// <inheritdoc />
    public void NotifyModelChanged() => OnModelChanged();

    /// <inheritdoc />
    public void ShowMessage(string message) => Ticker = message;

    /// <inheritdoc />
    public void RequestCopyToLevels()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        if (Editor.SelectionCount == 0 || _activeLevel is null)
        {
            Ticker = "Select something to copy to other levels.";
            return;
        }

        Guid activeId = _activeLevel.Id;
        List<LevelOption> targets = [.. _levelOptions.Where(o => o.Id != activeId)];
        if (targets.Count == 0)
        {
            Ticker = "There are no other levels to copy to.";
            return;
        }

        IReadOnlyList<Guid>? picked = _dialogs.PickLevels(
            "Copy to Levels",
            $"Paste the {Editor.SelectionCount} selected object{(Editor.SelectionCount == 1 ? string.Empty : "s")} in place on each ticked level. " +
            "Stairs, escalators and shafts keep their span (one level up stays one level up). One undo step.",
            targets);
        if (picked is { Count: > 0 })
        {
            Ticker = Editor.CopyToLevels(picked);
        }
    }

    // ------------------------------------------------------------------ levels for combos

    /// <summary>Every level, highest first, for level combo boxes.</summary>
    public IReadOnlyList<LevelOption> LevelOptions => _levelOptions;

    /// <summary>Rebuilds <see cref="LevelOptions"/> and the CONNECT targets after level changes.</summary>
    private void RefreshLevelOptions()
    {
        _levelOptions = [.. _study.LevelsByElevation().Select(l => new LevelOption(l.Id, $"{l.Name} ({l.ElevationM.ToString("+0.00;−0.00;0.00", CultureInfo.InvariantCulture)} m)"))];
        OnPropertyChanged(nameof(LevelOptions));
        RefreshConnectTargets();
    }

    // ------------------------------------------------------------------ CONNECT tool (D-054)

    /// <summary>Stair, escalator and ramp presets the CONNECT tool can place (travelators wait for round 005, D-074).</summary>
    public IReadOnlyList<ConnectorTemplate> ConnectPresetOptions { get; } = [.. BuiltInLibrary.Instance.Connectors.Where(c => c.ChangesLevel)];

    /// <summary>Preset the CONNECT tool places.</summary>
    public ConnectorTemplate? SelectedConnectPreset
    {
        get => Editor.ConnectPreset;
        set
        {
            if (ReferenceEquals(value, Editor.ConnectPreset))
            {
                return;
            }

            Editor.ConnectPreset = value;
            OnPropertyChanged();
            if (value is not null)
            {
                Ticker = $"CONNECT ▸ {value.Name}: {value.Description}";
            }
        }
    }

    /// <summary>Levels above the active one, nearest first: where CONNECT can go to.</summary>
    public IReadOnlyList<LevelOption> ConnectTargetOptions => _connectTargets;

    /// <summary>Level the CONNECT tool links the active level to.</summary>
    public Guid? ConnectTargetLevelId
    {
        get => Editor.ConnectTopLevelId;
        set
        {
            if (value == Editor.ConnectTopLevelId)
            {
                return;
            }

            Editor.ConnectTopLevelId = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Rebuilds the CONNECT targets for the active level and keeps the current choice when it is still above it;
    /// otherwise picks the nearest level above.
    /// </summary>
    private void RefreshConnectTargets()
    {
        Level? active = _activeLevel?.Model;
        _connectTargets = active is null
            ? []
            : [.. _study.Levels
                .Where(l => l.ElevationM > active.ElevationM + 1e-6)
                .OrderBy(l => l.ElevationM)
                .Select(l => _levelOptions.FirstOrDefault(o => o.Id == l.Id) ?? new LevelOption(l.Id, l.Name))];
        OnPropertyChanged(nameof(ConnectTargetOptions));

        Guid? current = Editor.ConnectTopLevelId;
        if (current is null || _connectTargets.All(o => o.Id != current))
        {
            Editor.ConnectTopLevelId = _connectTargets.Count > 0 ? _connectTargets[0].Id : null;
        }

        OnPropertyChanged(nameof(ConnectTargetLevelId));
    }

    /// <summary>Double-click on a library item: a stair, escalator or ramp preset starts CONNECT with it.</summary>
    private void UseTemplate(TemplateItemViewModel? item)
    {
        switch (item?.Template)
        {
            case DeviceTemplate { Kind: DeviceKind.Zone } zone:
                Ticker = $"{zone.Name} is an area: draw it as a zone (linking zones to presets arrives in M3).";
                return;

            case DeviceTemplate device:
                SelectedPlacePreset = PlacePresetOptions.FirstOrDefault(p => p.Id == device.Id) ?? device;
                SetTool(EditorTool.Place, true);
                return;

            case ConnectorTemplate { Kind: ConnectorKind.Travelator } travelator:
                SelectedPlacePreset = PlacePresetOptions.FirstOrDefault(p => p.Id == travelator.Id) ?? travelator;
                SetTool(EditorTool.Place, true);
                return;

            case ConnectorTemplate:
                break;

            default:
                Ticker = "Agent profiles, lifts and schedules are set up in later milestones.";
                return;
        }

        var connector = (ConnectorTemplate)item!.Template;

        SelectedConnectPreset = ConnectPresetOptions.FirstOrDefault(p => p.Id == connector.Id) ?? connector;
        SetTool(EditorTool.Connect, true);
    }

    // ------------------------------------------------------------------ tools

    public bool IsSelectTool { get => Editor.Tool == EditorTool.Select; set => SetTool(EditorTool.Select, value); }

    public bool IsRectangleTool { get => Editor.Tool == EditorTool.Rectangle; set => SetTool(EditorTool.Rectangle, value); }

    public bool IsPolygonTool { get => Editor.Tool == EditorTool.Polygon; set => SetTool(EditorTool.Polygon, value); }

    public bool IsCircleTool { get => Editor.Tool == EditorTool.Circle; set => SetTool(EditorTool.Circle, value); }

    public bool IsWallTool { get => Editor.Tool == EditorTool.Wall; set => SetTool(EditorTool.Wall, value); }

    public bool IsConnectTool { get => Editor.Tool == EditorTool.Connect; set => SetTool(EditorTool.Connect, value); }

    public bool IsShaftTool { get => Editor.Tool == EditorTool.Shaft; set => SetTool(EditorTool.Shaft, value); }

    public bool IsPlaceTool { get => Editor.Tool == EditorTool.Place; set => SetTool(EditorTool.Place, value); }

    // ------------------------------------------------------------------ PLACE tool (D-089, D-090)

    /// <summary>Point-device presets plus travelators, for the PLACE tool.</summary>
    public IReadOnlyList<TemplateBase> PlacePresetOptions { get; } =
    [
        .. BuiltInLibrary.Instance.Devices.Where(d => d.Kind == DeviceKind.Point),
        .. BuiltInLibrary.Instance.Connectors.Where(c => c.Kind == ConnectorKind.Travelator),
    ];

    /// <summary>Preset the PLACE tool places.</summary>
    public TemplateBase? SelectedPlacePreset
    {
        get => Editor.PlacePreset;
        set
        {
            if (ReferenceEquals(value, Editor.PlacePreset))
            {
                return;
            }

            Editor.PlacePreset = value;
            OnPropertyChanged();
            if (value is not null)
            {
                Ticker = $"PLACE ▸ {value.Name}: {value.Description}";
            }
        }
    }

    // ------------------------------------------------------------------ underlays (D-083–D-085)

    /// <summary>Underlays on the active level, for the LEVEL tab list.</summary>
    public System.Collections.ObjectModel.ObservableCollection<UnderlayViewModel> UnderlayRows { get; } = [];

    /// <summary>True when the active level has underlays.</summary>
    public bool HasUnderlays => UnderlayRows.Count > 0;

    /// <summary>True when the active level has no underlays (shows the empty hint).</summary>
    public bool HasNoUnderlays => UnderlayRows.Count == 0;

    /// <summary>Decoded pixels for an underlay's image (cached per image buffer), or null when missing or unreadable.</summary>
    public Throng.Render.Scene.UnderlayPixels? UnderlayPixelsFor(Underlay underlay) =>
        _study.UnderlayImages.TryGetValue(underlay.Id, out byte[]? bytes) ? _underlayImages.Get(bytes) : null;

    private void RefreshUnderlayRows()
    {
        Guid? levelId = _activeLevel?.Id;
        List<Underlay> wanted = levelId is { } id ? [.. _study.UnderlaysOn(id)] : [];
        if (wanted.SequenceEqual(UnderlayRows.Select(r => r.Model)))
        {
            foreach (UnderlayViewModel row in UnderlayRows)
            {
                row.Refresh();
            }
        }
        else
        {
            UnderlayRows.Clear();
            foreach (Underlay underlay in wanted)
            {
                UnderlayRows.Add(new UnderlayViewModel(underlay, this));
            }
        }

        OnPropertyChanged(nameof(HasUnderlays));
        OnPropertyChanged(nameof(HasNoUnderlays));
    }

    /// <summary>Imports an image as an underlay on the active level, scaled to the level's extent until calibrated.</summary>
    private void AddUnderlay()
    {
        if (_activeLevel is null)
        {
            Ticker = "Pick a level first.";
            return;
        }

        string? path = _dialogs.PickImagePath();
        if (path is null)
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Couldn't read underlay '{path}': {ex.Message}");
            _dialogs.ShowError($"Couldn't read '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            return;
        }

        var decoded = UnderlayImageCache.Decode(bytes);
        if (!decoded.IsSuccess || decoded.Value is not { } pixels)
        {
            _dialogs.ShowError(decoded.Error ?? "The image couldn't be read.");
            return;
        }

        var underlay = new Underlay
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            LevelId = _activeLevel.Id,
            PixelWidth = pixels.Width,
            PixelHeight = pixels.Height,
        };
        underlay.ImageEntry = ThrongFile.UnderlayEntryName(underlay.Id, path);

        // Until it's calibrated, fit it to the level's drawing (or 1 px = 50 mm on an empty level).
        Bounds2 bounds = _study.GetLevelBounds(_activeLevel.Id);
        if (!bounds.IsEmpty && bounds.Width > 1)
        {
            underlay.MetresPerPixel = bounds.Width / pixels.Width;
            underlay.Centre = bounds.Center;
        }

        _study.UnderlayImages[underlay.Id] = bytes;
        _underlayImages.Put(bytes, pixels);
        Execute(new AddItemsAction<Underlay>($"Add underlay {underlay.Name}", _study.Underlays, [underlay], OnModelChanged));
        if (ViewMode != ViewMode.Plan)
        {
            ViewMode = ViewMode.Plan;
        }

        Editor.Select(underlay.Id);
        Ticker = $"Underlay {underlay.Name} added ▸ press SET SCALE in SELECTION to calibrate it, then lock it.";
    }

    /// <summary>Selects an object by id (e.g. from the LEVEL tab's underlay list).</summary>
    public void SelectObject(Guid id) => Editor.Select(id);

    /// <summary>Kinds offered for new rectangles, polygons and circles.</summary>
    public IReadOnlyList<RegionKind> DrawKindOptions => DrawKinds;

    /// <summary>Kind given to new closed shapes.</summary>
    public RegionKind DrawKind
    {
        get => Editor.DrawKind;
        set => Editor.DrawKind = value;
    }

    // ------------------------------------------------------------------ snapping (editor settings, not undoable)

    public bool SnapGrid { get => Editor.Snap.Grid; set => SetSnap(() => Editor.Snap.Grid = value, value ? "Grid snap on." : "Grid snap off."); }

    public bool SnapEndpoint { get => Editor.Snap.Endpoint; set => SetSnap(() => Editor.Snap.Endpoint = value, value ? "Endpoint snap on." : "Endpoint snap off."); }

    public bool SnapMidpoint { get => Editor.Snap.Midpoint; set => SetSnap(() => Editor.Snap.Midpoint = value, value ? "Midpoint snap on." : "Midpoint snap off."); }

    public bool SnapOrtho { get => Editor.Snap.Ortho; set => SetSnap(() => Editor.Snap.Ortho = value, value ? "Ortho snap on. Shift forces it." : "Ortho snap off. Shift still forces it."); }

    /// <summary>Grid snap step in metres (0.01–10).</summary>
    public double GridStepM
    {
        get => Editor.Snap.GridStepM;
        set
        {
            if (value is < 0.01 or > 10 || double.IsNaN(value))
            {
                Ticker = "Grid step must be between 0.01 and 10 m.";
                OnPropertyChanged();
                return;
            }

            SetSnap(() => Editor.Snap.GridStepM = Math.Round(value, 3), string.Create(CultureInfo.InvariantCulture, $"Grid step {value:0.###} m."));
        }
    }

    // ------------------------------------------------------------------ selection panel

    /// <summary>Property panel for the single selected region, or null.</summary>
    public RegionViewModel? SelectedRegion => _selectedRegion;

    /// <summary>True when exactly one region is selected.</summary>
    public bool HasRegionSelection => _selectedRegion is not null;

    /// <summary>Property panel for the single selected connector, or null (D-063).</summary>
    public ConnectorViewModel? SelectedConnector => _selectedConnector;

    /// <summary>True when exactly one connector is selected.</summary>
    public bool HasConnectorSelection => _selectedConnector is not null;

    /// <summary>Property panel for the single selected shaft, or null.</summary>
    public ShaftViewModel? SelectedShaft => _selectedShaft;

    /// <summary>True when exactly one shaft is selected.</summary>
    public bool HasShaftSelection => _selectedShaft is not null;

    /// <summary>Property panel for the single selected device, or null (D-089).</summary>
    public DeviceViewModel? SelectedDevice => _selectedDevice;

    /// <summary>True when exactly one device is selected.</summary>
    public bool HasDeviceSelection => _selectedDevice is not null;

    /// <summary>Property panel for the single selected underlay, or null.</summary>
    public UnderlayViewModel? SelectedUnderlay => _selectedUnderlay;

    /// <summary>True when exactly one underlay is selected.</summary>
    public bool HasUnderlaySelection => _selectedUnderlay is not null;

    /// <summary>Selection summary for the properties panel.</summary>
    public string SelectionText => Editor.SelectionCount switch
    {
        0 => _activeLevel?.IsLocked == true
            ? "Level locked. Unlock it to select or draw."
            : "Nothing selected. Click a region, stair or shaft, or drag a box: left→right selects inside, right→left selects anything touched.",
        1 => string.Empty,
        int n => string.Create(CultureInfo.InvariantCulture, $"{n} objects selected. Drag to move, Ctrl+D duplicates, Ctrl+Shift+V copies to other levels, Del deletes."),
    };

    /// <summary>Net walkable area of the active level (D-039).</summary>
    public string WalkableAreaText
    {
        get => _walkableAreaText;
        private set => SetProperty(ref _walkableAreaText, value);
    }

    /// <summary>
    /// Recomputes the active level's net walkable polygons for the scene and the readout. Returns null when there
    /// is no active level or the computation failed (logged).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point2>>? RefreshWalkableArea()
    {
        if (_activeLevel is null)
        {
            WalkableAreaText = "—";
            return null;
        }

        var result = WalkableArea.Compute(_study, _activeLevel.Id, ClipperPolygonOps.Instance);
        if (!result.IsSuccess || result.Value is null)
        {
            AppLog.Warn(result.Error ?? "Walkable area failed.");
            WalkableAreaText = "Couldn't compute (see log)";
            return null;
        }

        WalkableAreaText = string.Create(CultureInfo.InvariantCulture, $"{WalkableArea.NetAreaM2(result.Value):0.0} m² net walkable");
        return result.Value;
    }

    // ------------------------------------------------------------------ wiring

    /// <summary>Creates the editor and its commands. Called once from the constructor.</summary>
    private void InitialiseEditing()
    {
        Editor = new PlanEditor(this);
        Editor.SelectionChanged += (_, _) => OnEditorSelectionChanged();
        Editor.ToolChanged += (_, _) => OnToolChanged();

        CopyCommand = new RelayCommand(Editor.Copy);
        CutCommand = new RelayCommand(Editor.Cut);
        PasteCommand = new RelayCommand(Editor.Paste);
        DuplicateCommand = new RelayCommand(Editor.Duplicate);
        DeleteCommand = new RelayCommand(Editor.Delete);
        SelectAllCommand = new RelayCommand(Editor.SelectAll);
        HideSelectionCommand = new RelayCommand(Editor.HideSelection);
        IsolateSelectionCommand = new RelayCommand(Editor.IsolateSelection);
        ResetViewCommand = new RelayCommand(() => Editor.ResetView());
        CopyToLevelsCommand = new RelayCommand(RequestCopyToLevels);
        AddUnderlayCommand = new RelayCommand(AddUnderlay);
        Editor.PlacePreset = PlacePresetOptions.FirstOrDefault();
        UseTemplateCommand = new RelayCommand<TemplateItemViewModel?>(UseTemplate);
        Editor.ConnectPreset = ConnectPresetOptions.FirstOrDefault();
        Editor.ViewFilterChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHiddenRegions));
            OnPropertyChanged(nameof(ResetViewText));
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        };

        SetToolCommand = new RelayCommand<string?>(name =>
        {
            if (Enum.TryParse(name, ignoreCase: true, out EditorTool tool))
            {
                SetTool(tool, true);
            }
        });
    }

    /// <summary>Called after model changes (do/undo/redo) and level switches.</summary>
    private void RefreshEditing()
    {
        Editor.Revalidate();
        _selectedRegion?.Refresh();
        _selectedConnector?.Refresh();
        _selectedShaft?.Refresh();
        _selectedDevice?.Refresh();
        _selectedUnderlay?.Refresh();
        RefreshConnectTargets();
        RefreshUnderlayRows();
        OnPropertyChanged(nameof(SelectionText));
    }

    private void SetTool(EditorTool tool, bool on)
    {
        if (on)
        {
            Editor.Tool = tool;

            // Editing is plan-only (D-026); picking a drawing tool switches to plan.
            if (tool != EditorTool.Select && ViewMode != ViewMode.Plan)
            {
                ViewMode = ViewMode.Plan;
            }
        }

        OnToolChanged();
    }

    private void OnToolChanged()
    {
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsRectangleTool));
        OnPropertyChanged(nameof(IsPolygonTool));
        OnPropertyChanged(nameof(IsCircleTool));
        OnPropertyChanged(nameof(IsWallTool));
        OnPropertyChanged(nameof(IsConnectTool));
        OnPropertyChanged(nameof(IsShaftTool));
        OnPropertyChanged(nameof(IsPlaceTool));
        OnPropertyChanged(nameof(DrawKind));
        Ticker = Editor.Tool switch
        {
            EditorTool.Rectangle => "RECT ▸ click two corners or drag. Type w,h for an exact size.",
            EditorTool.Polygon => "POLY ▸ click points; Enter, double-click or the first point closes. Type a length or dx,dy.",
            EditorTool.Circle => "CIRCLE ▸ click the centre, then the radius. Type a radius.",
            EditorTool.Wall => "WALL ▸ click points; Enter or double-click finishes. Type a length or dx,dy.",
            EditorTool.Connect => "CONNECT ▸ pick a preset and a level above, click the bottom landing, aim and click. Tab flips a switchback.",
            EditorTool.Shaft => "SHAFT ▸ click two corners or drag; it spans every level until you set base and top in SELECTION.",
            EditorTool.Place => "PLACE ▸ pick a device or travelator, click its position, aim the service side (or the belt's far end), click.",
            EditorTool.Calibrate => "SET SCALE ▸ click two points on the image a known distance apart, then type the real distance.",
            _ => "SELECT ▸ click, Shift adds, Ctrl toggles; drag a box or drag a region to move it.",
        };
    }

    private void SetSnap(Action apply, string message)
    {
        apply();
        OnPropertyChanged(nameof(SnapGrid));
        OnPropertyChanged(nameof(SnapEndpoint));
        OnPropertyChanged(nameof(SnapMidpoint));
        OnPropertyChanged(nameof(SnapOrtho));
        OnPropertyChanged(nameof(GridStepM));
        Ticker = message;
    }

    private void OnEditorSelectionChanged()
    {
        bool one = Editor.SelectionCount == 1;
        Region? region = one ? Editor.SelectedRegions.FirstOrDefault() : null;
        if (!ReferenceEquals(region, _selectedRegion?.Model))
        {
            _selectedRegion = region is null ? null : new RegionViewModel(region, this);
            OnPropertyChanged(nameof(SelectedRegion));
            OnPropertyChanged(nameof(HasRegionSelection));
        }

        Connector? connector = one ? Editor.SelectedConnectors.FirstOrDefault() : null;
        if (!ReferenceEquals(connector, _selectedConnector?.Model))
        {
            _selectedConnector = connector is null ? null : new ConnectorViewModel(connector, this);
            OnPropertyChanged(nameof(SelectedConnector));
            OnPropertyChanged(nameof(HasConnectorSelection));
        }

        Device? device = one ? Editor.SelectedDevices.FirstOrDefault() : null;
        if (!ReferenceEquals(device, _selectedDevice?.Model))
        {
            _selectedDevice = device is null ? null : new DeviceViewModel(device, this);
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(HasDeviceSelection));
        }

        Underlay? underlay = one ? Editor.SelectedUnderlays.FirstOrDefault() : null;
        if (!ReferenceEquals(underlay, _selectedUnderlay?.Model))
        {
            _selectedUnderlay = underlay is null ? null : new UnderlayViewModel(underlay, this);
            OnPropertyChanged(nameof(SelectedUnderlay));
            OnPropertyChanged(nameof(HasUnderlaySelection));
        }

        Shaft? shaft = one ? Editor.SelectedShafts.FirstOrDefault() : null;
        if (!ReferenceEquals(shaft, _selectedShaft?.Model))
        {
            _selectedShaft = shaft is null ? null : new ShaftViewModel(shaft, this);
            OnPropertyChanged(nameof(SelectedShaft));
            OnPropertyChanged(nameof(HasShaftSelection));
        }

        // Selecting anything brings the SELECTION tab forward; clearing leaves the current tab alone (D-045).
        if (Editor.SelectionCount > 0)
        {
            PropertiesTabIndex = PropertiesTabSelection;
        }

        OnPropertyChanged(nameof(SelectionText));
    }
}
