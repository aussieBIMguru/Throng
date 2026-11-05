using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Throng.App.Editing;
using Throng.Core.Geometry;
using Throng.Render.Gl;
using Throng.Render.Scene;

namespace Throng.App.Views;

/// <summary>
/// Select and drop in 3D and ISO (round 017, D-291; select-only since 017b, D-298). The pointer becomes a ray through
/// the camera; the first visible level floor it meets (honouring explode, the clip and the section box) is the floor
/// under the cursor. A click there makes that level active and hands the plan editor the point on its floor plane,
/// so picking and the hover label work as in plan; the overlay draws them on that plane. Nothing moves in 3D: a right
/// click offers VIEW IN PLAN for the object under the cursor (D-299).
/// </summary>
public partial class ViewportView
{
    private const double MinMetresPerDip3D = 1e-4;
    private const double MaxMetresPerDip3D = 50;

    private bool _left3D;
    private Point _rightPress;

    /// <summary>Raised by the 3D right-click menu's VIEW IN PLAN with the object's id (D-299).</summary>
    public event EventHandler<Guid>? ViewInPlanRequested;
    private Matrix4x4 _overlayViewProjection;

    /// <summary>A level floor under the cursor.</summary>
    /// <param name="LevelId">The level.</param>
    /// <param name="ElevationM">Its true elevation.</param>
    /// <param name="Point">The plan point hit on its floor plane.</param>
    private readonly record struct FloorHit(Guid LevelId, double ElevationM, Point2 Point);

    /// <summary>True when the 3D / ISO view drives the editor: SELECT only (D-292).</summary>
    private bool IsEditing3D => _mode != ViewMode.Plan && _editor is not null && !_failed && _editor.Tool == EditorTool.Select;

    /// <summary>Maps plan metres to overlay DIPs: the plan camera, or the active level's floor plane in 3D.</summary>
    private Point MapToOverlay(Point2 p)
    {
        if (_mode == ViewMode.Plan)
        {
            return PlanToScreen(p);
        }

        double z = _renderer.WarpZ(_editor?.ActiveElevationM ?? 0);
        return ToScreen(new Vector3((float)p.X, (float)p.Y, (float)z)) ?? new Point(-1e5, -1e5);
    }

    /// <summary>Keeps the 3D overlay on the floor while the camera moves (called every frame).</summary>
    private void SyncOverlay3D(Matrix4x4 viewProjection)
    {
        bool on = IsEditing3D;
        if (Overlay.Is3D != on)
        {
            Overlay.Is3D = on;
            if (_editor is not null)
            {
                _editor.Is3D = _mode != ViewMode.Plan;
            }

            Overlay.InvalidateVisual();
        }

        if (on && viewProjection != _overlayViewProjection)
        {
            _overlayViewProjection = viewProjection;
            Overlay.InvalidateVisual();
        }
    }

    /// <summary>The camera ray through a screen point (exploded heights), or false when the matrix can't invert.</summary>
    private bool TryRay(Point screen, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (ActualWidth < 1 || ActualHeight < 1 || !Matrix4x4.Invert(_lastViewProjection, out Matrix4x4 inverse))
        {
            return false;
        }

        float x = (float)((screen.X / ActualWidth * 2) - 1);
        float y = (float)(1 - (screen.Y / ActualHeight * 2));

        // Two depths inside the clip volume under either depth convention.
        Vector4 a = Vector4.Transform(new Vector4(x, y, 0.1f, 1f), inverse);
        Vector4 b = Vector4.Transform(new Vector4(x, y, 0.9f, 1f), inverse);
        if (Math.Abs(a.W) < 1e-9f || Math.Abs(b.W) < 1e-9f)
        {
            return false;
        }

        origin = new Vector3(a.X, a.Y, a.Z) / a.W;
        Vector3 far = new Vector3(b.X, b.Y, b.Z) / b.W;
        Vector3 along = far - origin;
        if (along.LengthSquared() < 1e-12f)
        {
            return false;
        }

        direction = Vector3.Normalize(along);
        return true;
    }

    /// <summary>Where the camera ray meets a level's (exploded) floor plane, as a plan point.</summary>
    private bool TryHitPlane(Point screen, double elevationM, out Point2 point, out float distance)
    {
        point = default;
        distance = float.MaxValue;
        if (!TryRay(screen, out Vector3 origin, out Vector3 direction) || Math.Abs(direction.Z) < 1e-6f)
        {
            return false;
        }

        float z = (float)_renderer.WarpZ(elevationM);
        float t = (z - origin.Z) / direction.Z;
        if (t < 0)
        {
            return false;
        }

        Vector3 hit = origin + (direction * t);
        point = new Point2(hit.X, hit.Y);
        distance = t;
        return true;
    }

    /// <summary>
    /// The nearest visible level floor under a screen point (D-293): levels shown in 3D, not cut by the clip, inside
    /// the section box, and with something at that point (a region or a device).
    /// </summary>
    private bool TryPickFloor(Point screen, out FloorHit hit)
    {
        hit = default;
        if (_editor is null)
        {
            return false;
        }

        (Point3 Min, Point3 Max)? box = _view.SectionBox && !_scene.Bounds.IsEmpty
            ? _view.ClipBox(_scene.Bounds, _scene.MinElevationM, _scene.MaxElevationM)
            : null;
        float best = float.MaxValue;
        bool found = false;
        foreach (LevelScene level in _scene.Levels)
        {
            if (_view.ClipAboveZ is { } clip && level.ElevationM > clip)
            {
                continue;
            }

            if (box is { } b && (level.ElevationM < b.Min.Z - 0.05 || level.ElevationM > b.Max.Z))
            {
                continue;
            }

            if (!TryHitPlane(screen, level.ElevationM, out Point2 point, out float distance) || distance >= best)
            {
                continue;
            }

            if (box is { } inside && (point.X < inside.Min.X || point.X > inside.Max.X || point.Y < inside.Min.Y || point.Y > inside.Max.Y))
            {
                continue;
            }

            if (!_editor.HasFloorAt(level.LevelId, point))
            {
                continue;
            }

            best = distance;
            hit = new FloorHit(level.LevelId, level.ElevationM, point);
            found = true;
        }

        return found;
    }

    /// <summary>Metres per DIP at a point on a floor plane (snap and pick tolerances are in screen pixels).</summary>
    private double MetresPerDipAt(Point2 p, double elevationM)
    {
        float z = (float)_renderer.WarpZ(elevationM);
        if (ToScreen(new Vector3((float)p.X, (float)p.Y, z)) is not { } s0
            || ToScreen(new Vector3((float)p.X + 1, (float)p.Y, z)) is not { } s1
            || ToScreen(new Vector3((float)p.X, (float)p.Y + 1, z)) is not { } s2)
        {
            return _planCamera.MetresPerDip;
        }

        double dips = Math.Max((s1 - s0).Length, (s2 - s0).Length);
        return dips < 1e-9 ? MaxMetresPerDip3D : Math.Clamp(1 / dips, MinMetresPerDip3D, MaxMetresPerDip3D);
    }

    /// <summary>Point on the active level's floor plane under a screen point.</summary>
    private bool TryActivePoint(Point screen, out Point2 point, out double metresPerDip)
    {
        point = default;
        metresPerDip = _planCamera.MetresPerDip;
        if (_editor?.ActiveElevationM is not { } elevation || !TryHitPlane(screen, elevation, out point, out _))
        {
            return false;
        }

        metresPerDip = MetresPerDipAt(point, elevation);
        return true;
    }

    /// <summary>Left press in 3D: a floor under the cursor becomes the active level, then the editor picks there.</summary>
    private void Pointer3DDown(Point position, MouseButtonEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        Point2 point;
        double metresPerDip;
        if (TryPickFloor(position, out FloorHit hit) && _editor.ActivateLevel(hit.LevelId))
        {
            point = hit.Point;
            metresPerDip = MetresPerDipAt(point, hit.ElevationM);
        }
        else if (!TryActivePoint(position, out point, out metresPerDip))
        {
            return;
        }

        _left3D = true;
        _leftDown = true;
        InputLayer.CaptureMouse();
        _editor.PointerDown(point, metresPerDip, Keyboard.Modifiers, e.ClickCount);
    }

    /// <summary>Pointer moved in 3D: hover on the active level only (another level's floor lights nothing).</summary>
    private void Pointer3DMove(Point position)
    {
        if (_editor is null)
        {
            return;
        }

        if (!_left3D && TryPickFloor(position, out FloorHit hover) && hover.LevelId != _editor.ActiveLevelId)
        {
            _editor.PointerLeave();
            return;
        }

        if (TryActivePoint(position, out Point2 point, out double metresPerDip))
        {
            _editor.PointerMove(point, metresPerDip, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
    }

    /// <summary>Left release in 3D: completes the pick (3D never moves anything, D-298).</summary>
    private void Pointer3DUp(Point position)
    {
        _left3D = false;
        if (_editor is null)
        {
            return;
        }

        if (TryActivePoint(position, out Point2 point, out double metresPerDip))
        {
            _editor.PointerUp(point, metresPerDip, Keyboard.Modifiers);
        }
        else
        {
            _editor.CancelDrag();
        }
    }

    /// <summary>
    /// A right click (no drag) in 3D (round 017b, D-299): selects the object under the cursor and offers VIEW IN PLAN,
    /// which opens plan view on its level framed on it.
    /// </summary>
    private void OpenObjectMenu(Point position)
    {
        if (_editor is null || !TryPickFloor(position, out FloorHit hit) || !_editor.ActivateLevel(hit.LevelId))
        {
            return;
        }

        if (_editor.ObjectAt(hit.Point, MetresPerDipAt(hit.Point, hit.ElevationM)) is not { } id)
        {
            return;
        }

        _editor.Select(id);
        var view = new MenuItem { Header = "VIEW IN PLAN" };
        view.Click += (_, _) => ViewInPlanRequested?.Invoke(this, id);
        var menu = new ContextMenu { PlacementTarget = InputLayer, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(view);
        menu.IsOpen = true;
    }

    /// <summary>The floor under a LIBRARY drop in 3D: its level, point and scale; false over nothing.</summary>
    private bool TryDropTarget3D(Point position, out FloorHit hit, out double metresPerDip)
    {
        metresPerDip = _planCamera.MetresPerDip;
        if (!TryPickFloor(position, out hit))
        {
            return false;
        }

        metresPerDip = MetresPerDipAt(hit.Point, hit.ElevationM);
        return true;
    }
}
