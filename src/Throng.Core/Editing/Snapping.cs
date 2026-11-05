using Throng.Core.Geometry;

namespace Throng.Core.Editing;

/// <summary>Which snaps are on, and the grid step. Defaults: 0.5 m grid with endpoint, midpoint and ortho on.</summary>
public sealed class SnapSettings
{
    /// <summary>Grid spacing in metres.</summary>
    public double GridStepM { get; set; } = 0.5;

    /// <summary>Snap to the grid.</summary>
    public bool Grid { get; set; } = true;

    /// <summary>Snap to existing vertices.</summary>
    public bool Endpoint { get; set; } = true;

    /// <summary>Snap to edge midpoints.</summary>
    public bool Midpoint { get; set; } = true;

    /// <summary>Lock to horizontal/vertical from the reference point when close to it.</summary>
    public bool Ortho { get; set; } = true;
}

/// <summary>What a snapped point locked onto.</summary>
public enum SnapKind
{
    /// <summary>No snap; the raw cursor point.</summary>
    None,

    /// <summary>A grid intersection.</summary>
    Grid,

    /// <summary>An existing vertex.</summary>
    Endpoint,

    /// <summary>The midpoint of an existing edge.</summary>
    Midpoint,

    /// <summary>Horizontal or vertical from the reference point.</summary>
    Ortho,
}

/// <summary>Result of a snap.</summary>
/// <param name="Point">Snapped plan point.</param>
/// <param name="Kind">What it snapped to.</param>
/// <param name="OrthoFrom">Reference point for an ortho snap (for drawing the guide line).</param>
public readonly record struct SnapResult(Point2 Point, SnapKind Kind, Point2? OrthoFrom = null);

/// <summary>
/// Geometry the snapper can lock onto: vertices and edges gathered from regions on the active level and the
/// level below. Locked levels contribute too (D-033).
/// </summary>
public sealed class SnapGeometry
{
    private readonly List<Point2> _endpoints = [];
    private readonly List<(Point2 A, Point2 B)> _segments = [];

    /// <summary>Vertices to snap to.</summary>
    public IReadOnlyList<Point2> Endpoints => _endpoints;

    /// <summary>Edges whose midpoints can be snapped to.</summary>
    public IReadOnlyList<(Point2 A, Point2 B)> Segments => _segments;

    /// <summary>Adds an outline's vertices and edges.</summary>
    public void AddOutline(IReadOnlyList<Point2> outline, bool closed)
    {
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            _endpoints.Add(outline[i]);
        }

        int edges = closed ? n : n - 1;
        for (int i = 0; i < edges && n >= 2; i++)
        {
            _segments.Add((outline[i], outline[(i + 1) % n]));
        }
    }

    /// <summary>Adds a single vertex (e.g. points already placed in a shape being drawn).</summary>
    public void AddPoint(Point2 point) => _endpoints.Add(point);
}

/// <summary>
/// Snaps a cursor point. Priority: endpoint, then midpoint, then ortho (with the free coordinate on the grid),
/// then grid, then the raw point. Tolerance is in metres (the caller converts from screen pixels).
/// </summary>
public static class Snapper
{
    /// <summary>Snaps <paramref name="raw"/>.</summary>
    /// <param name="raw">Cursor point in plan metres.</param>
    /// <param name="geometry">Candidate vertices and edges.</param>
    /// <param name="settings">Which snaps are on.</param>
    /// <param name="tolerance">Capture distance in metres.</param>
    /// <param name="reference">Previous point for ortho (last vertex while drawing), if any.</param>
    /// <param name="forceOrtho">True to lock to horizontal/vertical regardless of distance (Shift held).</param>
    public static SnapResult Snap(Point2 raw, SnapGeometry geometry, SnapSettings settings, double tolerance, Point2? reference = null, bool forceOrtho = false)
    {
        if (settings.Endpoint && TryNearest(raw, geometry.Endpoints, tolerance, out Point2 endpoint))
        {
            return new SnapResult(endpoint, SnapKind.Endpoint);
        }

        if (settings.Midpoint && TryNearestMidpoint(raw, geometry.Segments, tolerance, out Point2 midpoint))
        {
            return new SnapResult(midpoint, SnapKind.Midpoint);
        }

        if (reference is { } origin && (settings.Ortho || forceOrtho))
        {
            Point2 delta = raw - origin;
            bool horizontal = Math.Abs(delta.X) >= Math.Abs(delta.Y);
            var onAxis = horizontal ? new Point2(raw.X, origin.Y) : new Point2(origin.X, raw.Y);
            if (forceOrtho || Point2.Distance(raw, onAxis) <= tolerance)
            {
                if (settings.Grid && settings.GridStepM > 0)
                {
                    onAxis = horizontal
                        ? onAxis with { X = RoundToStep(onAxis.X, settings.GridStepM) }
                        : onAxis with { Y = RoundToStep(onAxis.Y, settings.GridStepM) };
                }

                return new SnapResult(onAxis, SnapKind.Ortho, origin);
            }
        }

        if (settings.Grid && settings.GridStepM > 0)
        {
            return new SnapResult(new Point2(RoundToStep(raw.X, settings.GridStepM), RoundToStep(raw.Y, settings.GridStepM)), SnapKind.Grid);
        }

        return new SnapResult(raw, SnapKind.None);
    }

    /// <summary>Rounds to the nearest multiple of <paramref name="step"/>.</summary>
    public static double RoundToStep(double value, double step) => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    private static bool TryNearest(Point2 raw, IReadOnlyList<Point2> points, double tolerance, out Point2 nearest)
    {
        nearest = raw;
        double best = tolerance;
        bool found = false;
        for (int i = 0; i < points.Count; i++)
        {
            double d = Point2.Distance(raw, points[i]);
            if (d <= best)
            {
                best = d;
                nearest = points[i];
                found = true;
            }
        }

        return found;
    }

    private static bool TryNearestMidpoint(Point2 raw, IReadOnlyList<(Point2 A, Point2 B)> segments, double tolerance, out Point2 nearest)
    {
        nearest = raw;
        double best = tolerance;
        bool found = false;
        for (int i = 0; i < segments.Count; i++)
        {
            Point2 mid = (segments[i].A + segments[i].B) * 0.5;
            double d = Point2.Distance(raw, mid);
            if (d <= best)
            {
                best = d;
                nearest = mid;
                found = true;
            }
        }

        return found;
    }
}
