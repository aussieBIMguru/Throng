namespace Throng.Core.Geometry;

/// <summary>
/// Splits the walls of a vertical prism (a shaft) into panels around rectangular openings (round 015, D-261), so the
/// 3D view can draw shaft walls with the landing doors left open. Pure geometry: shared by the renderer and the tests.
/// </summary>
public static class ShaftWalls
{
    /// <summary>How far an opening's ends may sit off an edge's line and still count as on it, metres.</summary>
    public const double OnEdgeToleranceM = 0.02;

    /// <summary>Panels narrower or shorter than this are left out (slivers from rounding), metres.</summary>
    private const double MinSizeM = 1e-3;

    /// <summary>
    /// The wall panels of the closed <paramref name="outline"/> from <paramref name="z0"/> to <paramref name="z1"/>, with
    /// every opening that lies on an edge cut out. Openings off every edge are ignored; overlapping openings merge.
    /// </summary>
    public static List<WallPanel> Panels(IReadOnlyList<Point2> outline, double z0, double z1, IReadOnlyList<WallOpening> openings)
    {
        var panels = new List<WallPanel>();
        int n = outline.Count;
        if (n < 2 || z1 - z0 < MinSizeM)
        {
            return panels;
        }

        var cuts = new List<(double T0, double T1, double Z0, double Z1)>();
        var breaks = new List<double>();
        for (int e = 0; e < n; e++)
        {
            Point2 a = outline[e], b = outline[(e + 1) % n];
            double length = (b - a).Length;
            if (length < MinSizeM)
            {
                continue;
            }

            Point2 dir = (b - a) * (1 / length);
            cuts.Clear();
            foreach (WallOpening opening in openings)
            {
                if (OnEdge(a, dir, length, opening, out double t0, out double t1))
                {
                    cuts.Add((t0, t1, Math.Max(z0, Math.Min(opening.Z0, opening.Z1)), Math.Min(z1, Math.Max(opening.Z0, opening.Z1))));
                }
            }

            if (cuts.Count == 0)
            {
                panels.Add(new WallPanel(a, b, z0, z1));
                continue;
            }

            // Columns between every opening edge; each column is wall except where an opening covers it.
            breaks.Clear();
            breaks.Add(0);
            breaks.Add(length);
            foreach ((double t0, double t1, _, _) in cuts)
            {
                breaks.Add(t0);
                breaks.Add(t1);
            }

            breaks.Sort();
            for (int k = 0; k + 1 < breaks.Count; k++)
            {
                double ta = breaks[k], tb = breaks[k + 1];
                if (tb - ta < MinSizeM)
                {
                    continue;
                }

                double mid = (ta + tb) * 0.5;
                Point2 pa = a + (dir * ta), pb = a + (dir * tb);
                AddColumn(panels, pa, pb, z0, z1, cuts, mid);
            }
        }

        return panels;
    }

    /// <summary>Wall panels of one column: [z0, z1] minus the height ranges of the openings that span it.</summary>
    private static void AddColumn(
        List<WallPanel> panels, Point2 a, Point2 b, double z0, double z1, List<(double T0, double T1, double Z0, double Z1)> cuts, double mid)
    {
        var holes = new List<(double Z0, double Z1)>();
        foreach ((double t0, double t1, double h0, double h1) in cuts)
        {
            if (mid > t0 && mid < t1 && h1 - h0 >= MinSizeM)
            {
                holes.Add((h0, h1));
            }
        }

        holes.Sort((x, y) => x.Z0.CompareTo(y.Z0));
        double z = z0;
        foreach ((double h0, double h1) in holes)
        {
            if (h0 - z >= MinSizeM)
            {
                panels.Add(new WallPanel(a, b, z, h0));
            }

            z = Math.Max(z, h1);
        }

        if (z1 - z >= MinSizeM)
        {
            panels.Add(new WallPanel(a, b, z, z1));
        }
    }

    /// <summary>True when both ends of the opening lie on the edge's line; returns its range along the edge.</summary>
    private static bool OnEdge(Point2 a, Point2 dir, double length, WallOpening opening, out double t0, out double t1)
    {
        Point2 normal = dir.Perpendicular();
        double offA = Math.Abs(Point2.Dot(opening.A - a, normal));
        double offB = Math.Abs(Point2.Dot(opening.B - a, normal));
        double ta = Point2.Dot(opening.A - a, dir), tb = Point2.Dot(opening.B - a, dir);
        t0 = Math.Clamp(Math.Min(ta, tb), 0, length);
        t1 = Math.Clamp(Math.Max(ta, tb), 0, length);
        return offA <= OnEdgeToleranceM && offB <= OnEdgeToleranceM && t1 - t0 >= MinSizeM;
    }
}
