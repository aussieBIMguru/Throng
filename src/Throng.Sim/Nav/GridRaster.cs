using Throng.Core.Geometry;

namespace Throng.Sim.Nav;

/// <summary>
/// Rasterises plan polygons onto an axis-aligned grid by cell centre (a cell is inside when its centre is),
/// scanline by scanline with the non-zero winding rule, so outers wound counter-clockwise with clockwise holes
/// (the <see cref="IPolygonOps"/> contract) come out right.
/// </summary>
public static class GridRaster
{
    /// <summary>
    /// Calls <paramref name="mark"/> for every cell whose centre is inside the polygons. The grid starts at
    /// <paramref name="origin"/> with square cells of <paramref name="cell"/> metres, <paramref name="nx"/> ×
    /// <paramref name="ny"/> cells.
    /// </summary>
    /// <param name="polygons">Closed polygons (implicitly closed; at least three points each).</param>
    /// <param name="evenOdd">Use the even-odd rule instead of non-zero (for single, possibly self-touching zones).</param>
    public static void Fill(
        IEnumerable<IReadOnlyList<Point2>> polygons, Point2 origin, double cell, int nx, int ny, Action<int> mark, bool evenOdd = false)
    {
        var edges = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (IReadOnlyList<Point2> polygon in polygons)
        {
            if (polygon.Count < 3)
            {
                continue;
            }

            for (int k = 0; k < polygon.Count; k++)
            {
                Point2 a = polygon[k];
                Point2 b = polygon[(k + 1) % polygon.Count];
                if (a.Y != b.Y)
                {
                    edges.Add((a.X, a.Y, b.X, b.Y));
                }
            }
        }

        if (edges.Count == 0)
        {
            return;
        }

        var crossings = new List<(double X, int Winding)>();
        for (int j = 0; j < ny; j++)
        {
            double y = origin.Y + ((j + 0.5) * cell);
            crossings.Clear();
            foreach ((double x0, double y0, double x1, double y1) in edges)
            {
                // Half-open rule so a vertex exactly on the scanline is counted once.
                if (y0 <= y && y < y1)
                {
                    crossings.Add((x0 + ((y - y0) / (y1 - y0) * (x1 - x0)), +1));
                }
                else if (y1 <= y && y < y0)
                {
                    crossings.Add((x0 + ((y - y0) / (y1 - y0) * (x1 - x0)), -1));
                }
            }

            if (crossings.Count < 2)
            {
                continue;
            }

            crossings.Sort((p, q) => p.X.CompareTo(q.X));
            int winding = 0;
            for (int k = 0; k + 1 < crossings.Count; k++)
            {
                winding = evenOdd ? winding ^ 1 : winding + crossings[k].Winding;
                if (winding == 0)
                {
                    continue;
                }

                // Cells whose centre lies in [crossings[k].X, crossings[k + 1].X).
                double from = crossings[k].X;
                double to = crossings[k + 1].X;
                int i0 = Math.Max(0, (int)Math.Ceiling(((from - origin.X) / cell) - 0.5));
                int i1 = Math.Min(nx - 1, (int)Math.Ceiling(((to - origin.X) / cell) - 0.5) - 1);
                for (int i = i0; i <= i1; i++)
                {
                    mark((j * nx) + i);
                }
            }
        }
    }
}
