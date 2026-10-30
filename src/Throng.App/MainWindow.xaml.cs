using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Throng.App.Services;
using Throng.App.ViewModels;
using Throng.App.Views;
using Throng.Core.Geometry;
using Throng.Render.Scene;

namespace Throng.App;

/// <summary>
/// Main window. Thin code-behind: wires the view model to the viewport and handles window lifetime.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly string? _startupFile;
    private Point? _libraryPress;

    /// <summary>Creates the window, optionally opening a study passed on the command line.</summary>
    public MainWindow(string? startupFile = null)
    {
        InitializeComponent();
        _startupFile = startupFile;
        _viewModel = new MainViewModel(new DialogService(this));
        DataContext = _viewModel;
        Viewport.Editor = _viewModel.Editor;
        Viewport.Agents = _viewModel.FetchAgents;
        Viewport.Heat = _viewModel.FetchHeat;
        Viewport.LiftCars = _viewModel.FetchLiftCars3D;
        _viewModel.CaptureViewport = Viewport.CaptureAsync;
        Viewport.TemplateDropped += OnTemplateDropped;
        Viewport.IsoRotateRequested += (_, step) => _viewModel.RotateIso(step);
        Viewport.BoxFaceDragged += (_, drag) => _viewModel.DragBoxFace(drag.Face, drag.Share);
        _viewModel.WriteCamera = Viewport.WriteCamera;
        _viewModel.ReadCamera = Viewport.ReadCamera;

        _viewModel.SceneInvalidated += (_, _) => RebuildScene();
        _viewModel.ZoomExtentsRequested += (_, _) => Viewport.ZoomExtents();
        _viewModel.ZoomToRequested += (_, bounds) => Viewport.ZoomTo(bounds);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Viewport.Mode = _viewModel.ViewMode;
        Viewport.IsoPreset = _viewModel.IsoPreset;
        Viewport.View = _viewModel.ViewSettings;
        RebuildScene();
        Viewport.ZoomExtents();

        if (_startupFile is not null)
        {
            _viewModel.OpenPath(_startupFile);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ViewMode):
            case "":
                Viewport.Mode = _viewModel.ViewMode;
                Viewport.SetLevelLabel(_viewModel.HudLevelText);
                Viewport.AgentColoring = _viewModel.AgentColoring;
                Viewport.IsoPreset = _viewModel.IsoPreset;
                Viewport.View = _viewModel.ViewSettings;
                break;
            case nameof(MainViewModel.IsoPreset):
                Viewport.IsoPreset = _viewModel.IsoPreset;
                break;
            case nameof(MainViewModel.ViewSettings):
                Viewport.View = _viewModel.ViewSettings;
                break;
            case nameof(MainViewModel.AgentColoring):
                Viewport.AgentColoring = _viewModel.AgentColoring;
                break;
            case nameof(MainViewModel.HudLevelText):
                Viewport.SetLevelLabel(_viewModel.HudLevelText);
                break;
        }
    }

    private void RebuildScene()
    {
        // The net walkable outline (D-039) is drawn over the active level's sketch.
        var walkable = _viewModel.RefreshWalkableArea();
        // The polygon library lets floors show their openings and shafts as holes (D-055, D-056).
        Viewport.ShowScene(SceneBuilder.Build(_viewModel.Study, _viewModel.ActiveLevel?.Id, walkable, _viewModel.Editor.HiddenIds, ClipperPolygonOps.Instance, _viewModel.UnderlayPixelsFor));
        Viewport.SetLevelLabel(_viewModel.HudLevelText);

        // The clip height follows level edits (D-266).
        Viewport.View = _viewModel.ViewSettings;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _viewModel.Shutdown();
        AppLog.Info("Throng closing.");
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Double-clicking a library row uses the template (stairs, escalators and ramps start CONNECT).</summary>
    private void OnLibraryItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: TemplateItemViewModel item })
        {
            _libraryPress = null;
            _viewModel.UseTemplateCommand.Execute(item);
            Viewport.FocusInput();
            e.Handled = true;
            return;
        }

        _libraryPress = e.ClickCount == 1 && sender is IInputElement element ? e.GetPosition(element) : null;

        // A single click on a journey opens it in SELECTION (D-162); on a profile, picks it for EXPORT (D-275).
        if (e.ClickCount == 1 && sender is FrameworkElement { DataContext: TemplateItemViewModel { Template: Throng.Core.Library.JourneyTemplate or Throng.Core.Library.AgentProfileTemplate } item2 })
        {
            _viewModel.SelectLibraryItem(item2);
        }
    }

    /// <summary>Dragging a library row past the system drag distance starts drag-and-drop to the viewport (D-098).</summary>
    private void OnLibraryItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_libraryPress is not { } press || e.LeftButton != MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: TemplateItemViewModel item } element)
        {
            return;
        }

        Vector moved = e.GetPosition(element) - press;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _libraryPress = null;
        if (!_viewModel.IsEditable)
        {
            _viewModel.Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        DragDrop.DoDragDrop(element, new DataObject(ViewportView.TemplateDragFormat, item), DragDropEffects.Copy);
        _viewModel.Editor.EndDragHover();
    }

    private void OnTemplateDropped(object? sender, TemplateDropEventArgs e)
    {
        if (e.Data is TemplateItemViewModel item)
        {
            _viewModel.DropTemplate(item, e.PlanPoint, e.MetresPerDip);
            Viewport.FocusInput();
        }
    }
}
