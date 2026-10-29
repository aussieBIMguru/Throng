using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Throng.Core.Common;
using OpenTK.Wpf;
using Throng.App.Editing;
using Throng.App.Services;
using Throng.Core.Geometry;
using Throng.Render.Cameras;
using Throng.Render.Gl;
using Throng.Render.Scene;

namespace Throng.App.Views;

/// <summary>Supplies the agents to draw this frame (D-102): fills the buffer and returns the count.</summary>
/// <param name="mode">Current view mode (plan shows the active level only).</param>
/// <param name="buffer">Buffer to fill (grown by the callee when needed).</param>
public delegate int AgentFeed(ViewMode mode, ref float[] buffer);

/// <summary>Supplies the live heatmap layers to drape this frame (D-202), or null for none.</summary>
/// <param name="mode">Current view mode (plan shows the active level only).</param>
/// <param name="version">Changes whenever the layers' pixels change.</param>
public delegate IReadOnlyList<HeatLayerDraw>? HeatFeed(ViewMode mode, out long version);

/// <summary>
/// Supplies the lift cars to draw in 3D this frame (round 013b), or null for none. Return the same list instance while
/// nothing changed so the renderer doesn't rebuild its meshes.
/// </summary>
public delegate IReadOnlyList<LiftCarDraw>? LiftCarFeed();

/// <summary>A LIBRARY row dropped on the viewport (D-098).</summary>
/// <param name="Data">The dragged item.</param>
/// <param name="PlanPoint">Plan point under the drop in plan view; null when dropped on the 3D view.</param>
/// <param name="MetresPerDip">Plan zoom at the drop.</param>
public sealed record TemplateDropEventArgs(object Data, Point2? PlanPoint, double MetresPerDip);

/// <summary>
/// Hosts the OpenGL viewport (GLWpfControl) plus WPF overlays, owns the cameras and mouse navigation, and routes
/// left-button and keyboard input to the <see cref="PlanEditor"/> in plan view.
/// </summary>
/// <remarks>
/// Plan: wheel zooms at the cursor, middle/right-drag pans, left button edits.
/// 3D: wheel zooms, right-drag orbits, middle-drag or Shift+right-drag pans (view only until M6, D-026).
/// </remarks>
public partial class ViewportView : UserControl
{
    private const double WheelZoomStep = 1.15;
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(2.2);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(4);
    private const int CaptureSamples = 4;

    private readonly SceneRenderer _renderer = new();
    private readonly PlanCamera _planCamera = new();
    private readonly OrbitCamera _orbitCamera = new();
    private readonly GLWpfControl? _gl;
    private readonly DispatcherTimer _flashTimer;
    private SceneData _scene = SceneData.Empty;
    private ViewMode _mode = ViewMode.Orbit;
    private bool _failed;
    private bool _zoomExtentsPending = true;
    private MouseButton? _dragButton;
    private bool _leftDown;
    private Point _lastMouse;
    private PlanEditor? _editor;
    private EditorTool _lastTool = EditorTool.Select;
    private float[] _agentBuffer = new float[640];
    private TaskCompletionSource<Result<byte[]>>? _capture;
    private int _captureWidth, _captureHeight;
    private double _captureScale = 1;

    /// <summary>Creates the viewport and starts the GL control.</summary>
    public ViewportView()
    {
        InitializeComponent();

        try
        {
            _gl = new GLWpfControl();
            Root.Children.Insert(0, _gl);
            // 4× MSAA for crisp 2 px lines and edges (D-049).
            _gl.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3, RenderContinuously = true, Samples = 4 });
            _gl.Render += OnGlRender;
        }
        catch (Exception ex)
        {
            // Boundary: GL hosting can fail on unusual drivers / remote sessions. Show it in the viewport instead of crashing.
            AppLog.Error("OpenGL viewport failed to start", ex);
            Fail($"OpenGL couldn't start: {ex.Message}");
        }

        _flashTimer = new DispatcherTimer { Interval = FlashDuration };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            FlashText.Visibility = Visibility.Collapsed;
        };

        Overlay.PlanToScreen = PlanToScreen;

        InputLayer.MouseDown += OnMouseDown;
        InputLayer.MouseMove += OnMouseMove;
        InputLayer.MouseUp += OnMouseUp;
        InputLayer.MouseWheel += OnMouseWheel;
        InputLayer.MouseLeave += OnMouseLeave;
        InputLayer.KeyDown += OnKeyDown;
        InputLayer.TextInput += OnTextInput;
        InputLayer.LostMouseCapture += OnLostMouseCapture;
        TypedInput.PreviewKeyDown += OnTypedInputKeyDown;
        TypedInput.LostKeyboardFocus += (_, _) => CloseTypedBox(refocus: false);

        // LIBRARY drag-and-drop (D-098).
        InputLayer.AllowDrop = true;
        InputLayer.DragOver += OnDragOver;
        InputLayer.DragLeave += OnDragLeave;
        InputLayer.Drop += OnDrop;
        UpdateHint();
    }

    /// <summary>Raised when a LIBRARY row is dropped on the viewport.</summary>
    public event EventHandler<TemplateDropEventArgs>? TemplateDropped;

    /// <summary>Format name of dragged LIBRARY rows.</summary>
    public const string TemplateDragFormat = "Throng.LibraryTemplate";

    /// <summary>Agents source, read every frame (null = none).</summary>
    public AgentFeed? Agents { get; set; }

    /// <summary>Live heatmap source, read every frame (null = none).</summary>
    public HeatFeed? Heat { get; set; }

    /// <summary>Lift cars for the 3D view, read every frame (null = none; plan shows cars in the overlay).</summary>
    public LiftCarFeed? LiftCars { get; set; }

    /// <summary>What agent bodies are coloured by (D-146).</summary>
    public AgentColoring AgentColoring
    {
        get => _renderer.AgentColoring;
        set => _renderer.AgentColoring = value;
    }

    /// <summary>Plan editor driven by this viewport (set once by the main window).</summary>
    public PlanEditor? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null)
            {
                _editor.OverlayChanged -= OnEditorOverlayChanged;
                _editor.Flash -= OnEditorFlash;
                _editor.ToolChanged -= OnEditorToolChanged;
                _editor.TypedInputRequested -= OnEditorTypedInputRequested;
            }

            _editor = value;
            Overlay.Editor = value;
            if (value is not null)
            {
                value.OverlayChanged += OnEditorOverlayChanged;
                value.Flash += OnEditorFlash;
                value.ToolChanged += OnEditorToolChanged;
                value.TypedInputRequested += OnEditorTypedInputRequested;
                _lastTool = value.Tool;
            }

            UpdateHint();
            Overlay.InvalidateVisual();
        }
    }

    /// <summary>Current presentation mode.</summary>
    public ViewMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            ModeText.Text = value == ViewMode.Plan ? "PLAN" : "3D ORBIT";
            Overlay.IsPlanActive = value == ViewMode.Plan;
            if (value != ViewMode.Plan)
            {
                CloseTypedBox(refocus: false);
            }

            UpdateHint();
            Overlay.InvalidateVisual();
        }
    }

    /// <summary>Replaces the displayed scene.</summary>
    public void ShowScene(SceneData scene)
    {
        _scene = scene;
        _renderer.SetScene(scene);
        Overlay.Labels = scene.PlanLabels;
        LevelScene? active = scene.Levels.FirstOrDefault(l => l.IsActive);
        if (active is not null)
        {
            _planCamera.ElevationM = active.ElevationM;
        }

        Overlay.InvalidateVisual();
    }

    /// <summary>Updates the HUD level label.</summary>
    public void SetLevelLabel(string text) => LevelText.Text = text;

    /// <summary>Frames the content in both cameras (deferred until the viewport has a size).</summary>
    public void ZoomExtents() => _zoomExtentsPending = true;

    /// <summary>
    /// Captures the view at <paramref name="scale"/> × its on-screen pixel size (PNG export, D-234): the GL scene is
    /// drawn again into an offscreen framebuffer on the next frame, and the plan overlay (labels, queues, trains) is
    /// drawn over it. The image's DPI is set so its size in DIPs matches the viewport. Never throws.
    /// </summary>
    /// <param name="scale">Multiple of the on-screen size (2 for the export).</param>
    public async Task<Result<BitmapSource>> CaptureAsync(double scale)
    {
        if (_failed || _gl is null)
        {
            return Result<BitmapSource>.Fail("The viewport is offline, so there's no view to export.");
        }

        if (_capture is not null)
        {
            return Result<BitmapSource>.Fail("An export is already in progress.");
        }

        double width = ActualWidth, height = ActualHeight;
        if (width < 1 || height < 1 || !IsVisible)
        {
            return Result<BitmapSource>.Fail("The viewport isn't showing, so there's no view to export.");
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double dpiX = 96.0 * dpi.DpiScaleX * scale, dpiY = 96.0 * dpi.DpiScaleY * scale;
        int pixelWidth = (int)Math.Round(width * dpi.DpiScaleX * scale);
        int pixelHeight = (int)Math.Round(height * dpi.DpiScaleY * scale);
        var capture = new TaskCompletionSource<Result<byte[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _captureWidth = pixelWidth;
        _captureHeight = pixelHeight;
        _captureScale = scale;
        _capture = capture;
        Task finished = await Task.WhenAny(capture.Task, Task.Delay(CaptureTimeout));
        _capture = null;
        if (finished != capture.Task)
        {
            return Result<BitmapSource>.Fail("The viewport didn't draw a frame in time. Make sure it's showing and try again.");
        }

        Result<byte[]> pixels = await capture.Task;
        if (!pixels.IsSuccess || pixels.Value is null)
        {
            return Result<BitmapSource>.Fail(pixels.Error ?? "The view couldn't be captured.");
        }

        try
        {
            BitmapSource frame = BitmapSource.Create(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Bgra32, null, pixels.Value, pixelWidth * 4);
            frame.Freeze();
            var overlay = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            overlay.Render(Overlay);
            overlay.Freeze();

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var area = new Rect(0, 0, width, height);
                dc.DrawImage(frame, area);
                dc.DrawImage(overlay, area);
            }

            var image = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            image.Render(visual);
            image.Freeze();
            return Result<BitmapSource>.Ok(image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Viewport capture failed", ex);
            return Result<BitmapSource>.Fail($"The view couldn't be captured: {ex.Message}");
        }
    }

    /// <summary>Gives the viewport keyboard focus so tool shortcuts work.</summary>
    public void FocusInput()
    {
        if (IsVisible && TypedBox.Visibility != Visibility.Visible)
        {
            InputLayer.Focus();
        }
    }

    // ------------------------------------------------------------------ rendering

    private void OnGlRender(TimeSpan delta)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (!_renderer.IsInitialised)
            {
                var init = _renderer.Initialise();
                if (!init.IsSuccess)
                {
                    Fail(init.Error ?? "Renderer failed to initialise.");
                    return;
                }
            }

            double width = ActualWidth, height = ActualHeight;
            if (width < 1 || height < 1)
            {
                return;
            }

            if (_zoomExtentsPending)
            {
                ApplyZoomExtents(width, height);
                _zoomExtentsPending = false;
                Overlay.InvalidateVisual();
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = (int)Math.Round(width * dpi.DpiScaleX);
            int pixelHeight = (int)Math.Round(height * dpi.DpiScaleY);
            Matrix4x4 viewProjection = _mode == ViewMode.Plan
                ? _planCamera.ViewProjection(width, height)
                : _orbitCamera.ViewProjection(width, height);

            int agents = Agents?.Invoke(_mode, ref _agentBuffer) ?? 0;
            _renderer.SetAgents(_agentBuffer, agents);
            long heatVersion = -1;
            IReadOnlyList<HeatLayerDraw>? heat = Heat?.Invoke(_mode, out heatVersion);
            _renderer.SetHeatmap(heat, heatVersion);
            _renderer.SetLiftCars(_mode == ViewMode.Plan ? null : LiftCars?.Invoke());
            _renderer.Render(pixelWidth, pixelHeight, viewProjection, _mode, (float)dpi.DpiScaleX);
            if (_capture is { Task.IsCompleted: false } capture)
            {
                capture.TrySetResult(CaptureFrame(viewProjection, (float)(dpi.DpiScaleX * _captureScale)));
            }
        }
        catch (Exception ex)
        {
            // Boundary: a GL fault stops rendering with a message rather than throwing every frame.
            AppLog.Error("Viewport render failed", ex);
            Fail($"Rendering stopped: {ex.Message}");
        }
    }

    /// <summary>Draws the current frame offscreen at the capture size (GL thread); problems come back as a failure.</summary>
    private Result<byte[]> CaptureFrame(Matrix4x4 viewProjection, float pixelsPerDip)
    {
        try
        {
            int max = OffscreenCapture.MaxSize();
            if (max > 0 && (_captureWidth > max || _captureHeight > max))
            {
                return Result<byte[]>.Fail($"The image would be {_captureWidth} × {_captureHeight} pixels; this graphics card can make at most {max} × {max}. Make the window smaller and try again.");
            }

            int width = _captureWidth, height = _captureHeight;
            return OffscreenCapture.Capture(width, height, CaptureSamples, () => _renderer.Render(width, height, viewProjection, _mode, pixelsPerDip));
        }
        catch (Exception ex)
        {
            // Boundary: a failed capture must not take the live viewport down with it.
            AppLog.Error("Offscreen capture failed", ex);
            return Result<byte[]>.Fail($"The view couldn't be captured: {ex.Message}");
        }
    }

    private void ApplyZoomExtents(double width, double height)
    {
        Bounds2 plan = _scene.ActiveBounds.IsEmpty ? _scene.Bounds : _scene.ActiveBounds;
        _planCamera.Fit(plan, width, height);
        _orbitCamera.Fit(_scene.Bounds, _scene.MinElevationM, _scene.MaxElevationM);
    }

    private Point PlanToScreen(Point2 p)
    {
        (double x, double y) = _planCamera.PlanToScreen(p, ActualWidth, ActualHeight);
        return new Point(x, y);
    }

    private Point2 ScreenToPlan(Point p) => _planCamera.ScreenToPlan(p.X, p.Y, ActualWidth, ActualHeight);

    private bool IsEditing => _mode == ViewMode.Plan && _editor is not null && !_failed;

    // ------------------------------------------------------------------ mouse

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        InputLayer.Focus();
        Point position = e.GetPosition(InputLayer);

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _dragButton = e.ChangedButton;
            _lastMouse = position;
            InputLayer.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && IsEditing && _dragButton is null)
        {
            CloseTypedBox(refocus: false);
            _leftDown = true;
            InputLayer.CaptureMouse();
            _editor!.PointerDown(ScreenToPlan(position), _planCamera.MetresPerDip, Keyboard.Modifiers, e.ClickCount);
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(InputLayer);

        if (_dragButton is not null)
        {
            double dx = position.X - _lastMouse.X;
            double dy = position.Y - _lastMouse.Y;
            _lastMouse = position;

            if (_mode == ViewMode.Plan)
            {
                _planCamera.Pan(dx, dy);
            }
            else if (_dragButton == MouseButton.Middle || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _orbitCamera.Pan(dx, dy);
            }
            else
            {
                _orbitCamera.Orbit(dx, dy);
            }
        }

        if (IsEditing)
        {
            _editor!.PointerMove(ScreenToPlan(position), _planCamera.MetresPerDip, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (_dragButton is not null && _mode == ViewMode.Plan)
        {
            Overlay.InvalidateVisual();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragButton == e.ChangedButton)
        {
            _dragButton = null;
            if (!_leftDown)
            {
                InputLayer.ReleaseMouseCapture();
            }

            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _leftDown)
        {
            _leftDown = false;
            if (_dragButton is null)
            {
                InputLayer.ReleaseMouseCapture();
            }

            if (IsEditing)
            {
                _editor!.PointerUp(ScreenToPlan(e.GetPosition(InputLayer)), _planCamera.MetresPerDip, Keyboard.Modifiers);
            }

            e.Handled = true;
        }
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture lost mid-gesture (Alt+Tab, a dialog): abandon the drag rather than leave it half-done.
        _dragButton = null;
        if (_leftDown)
        {
            _leftDown = false;
            _editor?.CancelDrag();
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!InputLayer.IsMouseCaptured)
        {
            _editor?.PointerLeave();
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.0 / WheelZoomStep : WheelZoomStep;
        Point p = e.GetPosition(InputLayer);
        if (_mode == ViewMode.Plan)
        {
            _planCamera.ZoomAt(p.X, p.Y, ActualWidth, ActualHeight, factor);
            if (IsEditing)
            {
                // Re-snap at the new zoom (tolerances are in screen pixels).
                _editor!.PointerMove(ScreenToPlan(p), _planCamera.MetresPerDip, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }

            Overlay.InvalidateVisual();
        }
        else
        {
            _orbitCamera.Zoom(factor);
        }

        e.Handled = true;
    }

    // ------------------------------------------------------------------ drag and drop (D-098)

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TemplateDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;

        // Ghost of what would drop here (D-144).
        if (e.Data.GetData(TemplateDragFormat) is Throng.App.ViewModels.TemplateItemViewModel item)
        {
            Point2? point = _mode == ViewMode.Plan ? ScreenToPlan(e.GetPosition(InputLayer)) : null;
            _editor?.DragHover(item.Template, point, _planCamera.MetresPerDip);
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => _editor?.EndDragHover();

    private void OnDrop(object sender, DragEventArgs e)
    {
        _editor?.EndDragHover();
        if (e.Data.GetData(TemplateDragFormat) is not { } data)
        {
            return;
        }

        InputLayer.Focus();
        Point2? point = _mode == ViewMode.Plan ? ScreenToPlan(e.GetPosition(InputLayer)) : null;
        TemplateDropped?.Invoke(this, new TemplateDropEventArgs(data, point, _planCamera.MetresPerDip));
        e.Handled = true;
    }

    // ------------------------------------------------------------------ keyboard and typed dimensions

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsEditing && _editor!.HandleKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsEditing || !_editor!.AcceptsTypedInput || e.Text.Length != 1)
        {
            return;
        }

        char c = e.Text[0];
        if (char.IsAsciiDigit(c) || c is '.' or '-')
        {
            OpenTypedBox(e.Text);
            e.Handled = true;
        }
    }

    private void OpenTypedBox(string initialText)
    {
        if (_editor is null)
        {
            return;
        }

        TypedHint.Text = (_editor.Tool, _editor.DrawPoints.Count) switch
        {
            (EditorTool.Connect, 0) => "BOTTOM LANDING  X,Y",
            (EditorTool.Calibrate, _) => "REAL DISTANCE  m",
            (EditorTool.Place, 0) => "POSITION  X,Y",
            (_, 0) => "FIRST POINT  X,Y",
            (EditorTool.Connect, _) => "ANGLE °  or  DX,DY",
            (EditorTool.Place, _) => _editor.IsPlacingTravelator ? "LENGTH  or  DX,DY" : "FACING °  or  DX,DY",
            (EditorTool.Rectangle or EditorTool.Shaft, _) => "SIZE  W,H",
            (EditorTool.Circle, _) => "RADIUS",
            _ => "LENGTH  or  DX,DY",
        };

        Point at = Mouse.GetPosition(Root);
        TypedBox.Visibility = Visibility.Visible;
        TypedBox.UpdateLayout();
        double left = Math.Clamp(at.X + 18, 4, Math.Max(4, ActualWidth - TypedBox.ActualWidth - 4));
        double top = Math.Clamp(at.Y + 18, 4, Math.Max(4, ActualHeight - TypedBox.ActualHeight - 4));
        Canvas.SetLeft(TypedBox, left);
        Canvas.SetTop(TypedBox, top);

        TypedInput.Text = initialText;
        TypedInput.Focus();
        TypedInput.CaretIndex = TypedInput.Text.Length;
    }

    private void OnTypedInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_editor is not null && _editor.ApplyTypedInput(TypedInput.Text))
                {
                    CloseTypedBox(refocus: true);
                }
                else
                {
                    TypedInput.SelectAll();
                }

                e.Handled = true;
                break;
            case Key.Escape:
                CloseTypedBox(refocus: true);
                e.Handled = true;
                break;
        }
    }

    private void CloseTypedBox(bool refocus)
    {
        if (TypedBox.Visibility != Visibility.Visible)
        {
            return;
        }

        TypedBox.Visibility = Visibility.Collapsed;
        TypedInput.Text = string.Empty;
        if (refocus)
        {
            InputLayer.Focus();
        }
    }

    // ------------------------------------------------------------------ editor events

    private void OnEditorOverlayChanged(object? sender, EventArgs e) => Overlay.InvalidateVisual();

    private void OnEditorTypedInputRequested(object? sender, string hint)
    {
        if (IsEditing && TypedBox.Visibility != Visibility.Visible)
        {
            OpenTypedBox(string.Empty);
        }
    }

    private void OnEditorFlash(object? sender, string message)
    {
        FlashText.Text = message;
        FlashText.Visibility = Visibility.Visible;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void OnEditorToolChanged(object? sender, EventArgs e)
    {
        UpdateHint();
        if (_editor is not null && _editor.Tool != _lastTool)
        {
            _lastTool = _editor.Tool;
            CloseTypedBox(refocus: false);
            FocusInput();
        }
    }

    private void UpdateHint()
    {
        if (_mode != ViewMode.Plan)
        {
            HintText.Text = "Wheel: zoom   ·   Right-drag: orbit   ·   Middle or Shift+Right-drag: pan   ·   Home: zoom extents   ·   F2: plan to edit";
            return;
        }

        string tool = _editor?.Tool switch
        {
            EditorTool.Rectangle => "RECT: click two corners or drag · type W,H",
            EditorTool.Polygon => "POLY: click points · Enter/double-click/first point closes · Backspace undoes a point · type a length",
            EditorTool.Circle => "CIRCLE: click centre, then radius · type a radius",
            EditorTool.Wall => "WALL: click points · Enter/double-click finishes · type a length",
            EditorTool.Connect => "CONNECT: click the bottom landing, aim up the run, click · 15° steps (Shift: free) · Tab flips a switchback · type an angle",
            EditorTool.Shaft => "SHAFT: click two corners or drag · type W,H · spans every level until you set base/top",
            EditorTool.Place => "PLACE: click the position, aim the service side (travelator: click the far end), click · 15° steps (Shift: free) · type an angle",
            EditorTool.Calibrate => "SET SCALE: click two points on the image a known distance apart, type that distance, Enter · Shift squares the second point",
            EditorTool.QueuePath => "DRAW QUEUE: click points out from the service side · Enter/double-click finishes · Backspace undoes a point · type a length · one snake feeds every lane",
            _ => "SELECT: click · Shift adds · Ctrl toggles · drag box (→ window, ← crossing) · drag to move · grips/arrows reshape · round grip rotates a connector · H hide · I isolate",
        };

        HintText.Text = $"{tool}   ·   Shift: ortho   ·   Esc: cancel   ·   Wheel: zoom   ·   Middle/Right-drag: pan";
    }

    private void Fail(string message)
    {
        _failed = true;
        ErrorText.Text = message + "\n\nThe rest of the app still works. Details are in the log:\n" + AppPaths.LogFile;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
