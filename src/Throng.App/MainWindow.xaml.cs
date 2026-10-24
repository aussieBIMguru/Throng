using System.ComponentModel;
using System.Windows;
using Throng.App.Services;
using Throng.App.ViewModels;
using Throng.Render.Scene;

namespace Throng.App;

/// <summary>
/// Main window. Thin code-behind: wires the view model to the viewport and handles window lifetime.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly string? _startupFile;

    /// <summary>Creates the window, optionally opening a study passed on the command line.</summary>
    public MainWindow(string? startupFile = null)
    {
        InitializeComponent();
        _startupFile = startupFile;
        _viewModel = new MainViewModel(new DialogService(this));
        DataContext = _viewModel;
        Viewport.Editor = _viewModel.Editor;

        _viewModel.SceneInvalidated += (_, _) => RebuildScene();
        _viewModel.ZoomExtentsRequested += (_, _) => Viewport.ZoomExtents();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Viewport.Mode = _viewModel.ViewMode;
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
        Viewport.ShowScene(SceneBuilder.Build(_viewModel.Study, _viewModel.ActiveLevel?.Id, walkable, _viewModel.Editor.HiddenRegionIds));
        Viewport.SetLevelLabel(_viewModel.HudLevelText);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        AppLog.Info("Throng closing.");
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();
}
