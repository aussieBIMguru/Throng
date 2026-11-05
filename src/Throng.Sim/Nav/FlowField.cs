using Throng.Core.Geometry;

namespace Throng.Sim.Nav;

/// <summary>
/// Nominal seconds from every cell of a set of surfaces to a target (a portal edge or a sink), from a reverse
/// Dijkstra search (round 006 design). Agents walk down its gradient.
/// </summary>
public sealed class FlowField
{
    /// <summary>Creates a field.</summary>
    /// <param name="cost">Per surface index: seconds per cell, or null where the field doesn't reach.</param>
    /// <param name="targetSurface">Surface the target cells are on.</param>
    /// <param name="targetDirection">Direction to push once at the target (through a portal), or zero.</param>
    /// <param name="edge">The portal edge the field leads through, when it leads to a portal.</param>
    public FlowField(float[]?[] cost, int targetSurface, Point2 targetDirection, (Point2 A, Point2 B)? edge = null)
    {
        Cost = cost;
        TargetSurface = targetSurface;
        TargetDirection = targetDirection;
        Edge = edge;
    }

    /// <summary>The portal edge the field leads through (null for a sink field).</summary>
    public (Point2 A, Point2 B)? Edge { get; }

    /// <summary>Seconds per cell for each surface (null outside the field).</summary>
    public float[]?[] Cost { get; }

    /// <summary>Surface holding the target cells.</summary>
    public int TargetSurface { get; }

    /// <summary>Unit push direction at the target (onto the other side of a portal), or zero for a sink.</summary>
    public Point2 TargetDirection { get; }

    /// <summary>
    /// Direction to walk once at the target: towards the nearest point of the portal edge, kept a body radius in
    /// from its ends, and on through it. Agents beside the edge's end slide into the opening instead of pressing
    /// against the connector's side.
    /// </summary>
    public Point2 PushThrough(double x, double y, double radius)
    {
        if (Edge is not { } edge)
        {
            return TargetDirection;
        }

        Point2 ab = edge.B - edge.A;
        double length = ab.Length;
        if (length < 1e-9)
        {
            return TargetDirection;
        }

        Point2 along = ab * (1 / length);
        double margin = Math.Min(radius + 0.05, length * 0.5);
        double t = Math.Clamp(Point2.Dot(new Point2(x, y) - edge.A, along), margin, length - margin);
        Point2 toward = edge.A + (along * t) - new Point2(x, y) + (TargetDirection * 0.5);
        return toward.Length > 1e-9 ? toward.Normalised() : TargetDirection;
    }

    /// <summary>True when the field covers a surface.</summary>
    public bool Covers(int surface) => surface < Cost.Length && Cost[surface] is not null;

    /// <summary>Cost of a cell (+∞ outside the field or unreached).</summary>
    public float At(int surface, int cell) =>
        cell >= 0 && surface < Cost.Length && Cost[surface] is { } c ? c[cell] : float.PositiveInfinity;

    /// <summary>
    /// Cost at a plan point: its cell's cost, or when that is unreached (an agent pressed against a wall), one cell
    /// more than the best of its eight neighbours.
    /// </summary>
    public float Sample(NavSurface surface, double x, double y)
    {
        if (Cost[surface.Index] is not { } c)
        {
            return float.PositiveInfinity;
        }

        int cell = surface.CellAt(x, y);
        if (cell >= 0 && float.IsFinite(c[cell]))
        {
            return c[cell];
        }

        surface.ToLocal(x, y, out double u, out double v);
        int i = (int)Math.Floor(u / surface.CellU);
        int j = (int)Math.Floor(v / surface.CellV);
        float best = float.PositiveInfinity;
        for (int dj = -1; dj <= 1; dj++)
        {
            for (int di = -1; di <= 1; di++)
            {
                int ii = i + di, jj = j + dj;
                if (ii >= 0 && jj >= 0 && ii < surface.Nx && jj < surface.Ny)
                {
                    best = Math.Min(best, c[(jj * surface.Nx) + ii]);
                }
            }
        }

        return best + (float)(surface.CellU / NavWorld.NominalSpeed);
    }

    /// <summary>
    /// Downhill direction at a plan point: minus the gradient of the bilinear interpolation of the four nearest
    /// cell centres (unreached cells count as the highest reached one plus two cells; cells past the grid edge are
    /// extrapolated), falling back to the lowest neighbouring cell. False when nothing nearby is reached.
    /// </summary>
    public bool Direction(NavSurface surface, double x, double y, out Point2 direction)
    {
        direction = Point2.Zero;
        if (Cost[surface.Index] is not { } c)
        {
            return false;
        }

        surface.ToLocal(x, y, out double u, out double v);
        double fu = (u / surface.CellU) - 0.5;
        double fv = (v / surface.CellV) - 0.5;
        int i0 = (int)Math.Floor(fu);
        int j0 = (int)Math.Floor(fv);
        double tx = fu - i0;
        double ty = fv - j0;

        float c00 = Corner(surface, c, i0, j0), c10 = Corner(surface, c, i0 + 1, j0);
        float c01 = Corner(surface, c, i0, j0 + 1), c11 = Corner(surface, c, i0 + 1, j0 + 1);
        float max = MaxFinite(MaxFinite(c00, c10), MaxFinite(c01, c11));
        if (float.IsFinite(max))
        {
            float fill = max + (float)(2 * surface.CellU / NavWorld.NominalSpeed);
            c00 = float.IsFinite(c00) ? c00 : fill;
            c10 = float.IsFinite(c10) ? c10 : fill;
            c01 = float.IsFinite(c01) ? c01 : fill;
            c11 = float.IsFinite(c11) ? c11 : fill;

            double gu = (((c10 - c00) * (1 - ty)) + ((c11 - c01) * ty)) / surface.CellU;
            double gv = (((c01 - c00) * (1 - tx)) + ((c11 - c10) * tx)) / surface.CellV;
            Point2 g = (surface.AxisU * -gu) + (surface.AxisV * -gv);
            if (g.Length > 1e-6)
            {
                direction = g.Normalised();
                return true;
            }
        }

        return LowestNeighbour(surface, c, x, y, out direction);
    }

    /// <summary>The larger finite value of two, or −∞ when neither is finite.</summary>
    private static float MaxFinite(float a, float b)
    {
        bool fa = float.IsFinite(a), fb = float.IsFinite(b);
        return fa && fb ? Math.Max(a, b) : fa ? a : fb ? b : float.NegativeInfinity;
    }

    /// <summary>
    /// A corner value for the bilinear patch. Past the edge of the grid the cost is extrapolated linearly from the
    /// two cells inside, so the gradient keeps leading across an open piece joint instead of turning back at the
    /// edge (walls are cells inside the grid, and balustrades and floor edges have their own forces).
    /// </summary>
    private static float Corner(NavSurface s, float[] c, int i, int j)
    {
        if (i >= 0 && j >= 0 && i < s.Nx && j < s.Ny)
        {
            return c[(j * s.Nx) + i];
        }

        int ii = Math.Clamp(i, 0, s.Nx - 1);
        int jj = Math.Clamp(j, 0, s.Ny - 1);
        float edge = c[(jj * s.Nx) + ii];
        if (!float.IsFinite(edge))
        {
            return float.PositiveInfinity;
        }

        // One step further inside, mirrored through the edge cell.
        int ki = ii - (i - ii), kj = jj - (j - jj);
        if (ki < 0 || kj < 0 || ki >= s.Nx || kj >= s.Ny)
        {
            return edge;
        }

        float inner = c[(kj * s.Nx) + ki];
        return float.IsFinite(inner) ? (2 * edge) - inner : edge;
    }

    private static bool LowestNeighbour(NavSurface s, float[] c, double x, double y, out Point2 direction)
    {
        direction = Point2.Zero;
        s.ToLocal(x, y, out double u, out double v);
        int i = (int)Math.Floor(u / s.CellU);
        int j = (int)Math.Floor(v / s.CellV);
        float best = float.PositiveInfinity;
        int bestCell = -1;
        for (int dj = -1; dj <= 1; dj++)
        {
            for (int di = -1; di <= 1; di++)
            {
                int ii = i + di, jj = j + dj;
                if ((di != 0 || dj != 0) && ii >= 0 && jj >= 0 && ii < s.Nx && jj < s.Ny && c[(jj * s.Nx) + ii] < best)
                {
                    best = c[(jj * s.Nx) + ii];
                    bestCell = (jj * s.Nx) + ii;
                }
            }
        }

        if (bestCell < 0)
        {
            return false;
        }

        direction = (s.CellCentre(bestCell) - new Point2(x, y)).Normalised();
        return direction.Length > 0;
    }
}
