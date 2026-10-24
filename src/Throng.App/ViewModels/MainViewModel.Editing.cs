using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Editing;
using Throng.App.Services;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.World;
using Throng.Render.Gl;

namespace Throng.App.ViewModels;

/// <summary>
/// Editing half of the main view model (M1): hosts the <see cref="PlanEditor"/>, exposes tools, snap settings,
/// clipboard commands and the selected-region property panel, and computes the net walkable area (D-039).
/// </summary>
public sealed partial class MainViewModel : IEditorHost
{
    private static readonly IReadOnlyList<RegionKind> DrawKinds = [RegionKind.Walkable, RegionKind.Obstacle, RegionKind.Zone];

    private RegionViewModel? _selectedRegion;
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

    /// <summary>True while any region is temporarily hidden; shows the RESET VIEW chip.</summary>
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

    // ------------------------------------------------------------------ tools

    public bool IsSelectTool { get => Editor.Tool == EditorTool.Select; set => SetTool(EditorTool.Select, value); }

    public bool IsRectangleTool { get => Editor.Tool == EditorTool.Rectangle; set => SetTool(EditorTool.Rectangle, value); }

    public bool IsPolygonTool { get => Editor.Tool == EditorTool.Polygon; set => SetTool(EditorTool.Polygon, value); }

    public bool IsCircleTool { get => Editor.Tool == EditorTool.Circle; set => SetTool(EditorTool.Circle, value); }

    public bool IsWallTool { get => Editor.Tool == EditorTool.Wall; set => SetTool(EditorTool.Wall, value); }

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

    /// <summary>Selection summary for the properties panel.</summary>
    public string SelectionText => Editor.SelectionCount switch
    {
        0 => _activeLevel?.IsLocked == true
            ? "Level locked. Unlock it to select or draw."
            : "Nothing selected. Click a region, or drag a box: left→right selects inside, right→left selects anything touched.",
        1 => string.Empty,
        int n => string.Create(CultureInfo.InvariantCulture, $"{n} regions selected. Drag to move, Ctrl+D duplicates, Del deletes."),
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
        OnPropertyChanged(nameof(DrawKind));
        Ticker = Editor.Tool switch
        {
            EditorTool.Rectangle => "RECT ▸ click two corners or drag. Type w,h for an exact size.",
            EditorTool.Polygon => "POLY ▸ click points; Enter, double-click or the first point closes. Type a length or dx,dy.",
            EditorTool.Circle => "CIRCLE ▸ click the centre, then the radius. Type a radius.",
            EditorTool.Wall => "WALL ▸ click points; Enter or double-click finishes. Type a length or dx,dy.",
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
        IReadOnlyList<Region> selected = Editor.SelectedRegions;
        Region? single = selected.Count == 1 ? selected[0] : null;
        if (!ReferenceEquals(single, _selectedRegion?.Model))
        {
            _selectedRegion = single is null ? null : new RegionViewModel(single, this);
            OnPropertyChanged(nameof(SelectedRegion));
            OnPropertyChanged(nameof(HasRegionSelection));
        }

        // Selecting anything brings the SELECTION tab forward; clearing leaves the current tab alone (D-045).
        if (selected.Count > 0)
        {
            PropertiesTabIndex = PropertiesTabSelection;
        }

        OnPropertyChanged(nameof(SelectionText));
    }
}
