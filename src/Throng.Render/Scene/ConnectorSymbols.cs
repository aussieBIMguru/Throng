using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Render.Scene;

/// <summary>Which part of a connector a level shows in plan (D-062).</summary>
public enum ConnectorPlanPart
{
    /// <summary>Bottom level: the part below mid-rise.</summary>
    Lower,

    /// <summary>Top level: the part above mid-rise.</summary>
    Upper,

    /// <summary>A level the connector passes through: the whole connector, faint.</summary>
    Through,
}

/// <summary>A short text label drawn over the plan by the WPF overlay (the GL renderer has no text).</summary>
/// <param name="Position">Plan position of the label's centre.</param>
/// <param name="Text">Label text, e.g. "UP".</param>
/// <param name="Color">Text colour.</param>
public sealed record PlanLabel(Point2 Position, string Text, ColorF Color);

/// <summary>
/// Builds connector geometry for the scene: architectural plan symbols cut at mid-rise (D-062) and true
/// sloped 3D pieces (D-054). Pure CPU work over a <see cref="ConnectorLayout"/>.
/// </summary>
public static class ConnectorSymbols
{
    private const double Epsilon = 1e-6;
    private const double StringerDepthM = 0.25;
    private const double HandrailHeightM = 0.9;

    /// <summary>Adds the plan symbol for one part of a connector.</summary>
    /// <param name="lines">Plan-only line builder for the level.</param>
    /// <param name="labels">Labels for the level (only collected for the active level; may be null).</param>
    /// <param name="connector">Connector.</param>
    /// <param name="layout">Its layout.</param>
    /// <param name="part">Which part to draw.</param>
    /// <param name="z">Elevation to draw at.</param>
    public static void AddPlan(MeshBuilder lines, List<PlanLabel>? labels, Connector connector, ConnectorLayout layout, ConnectorPlanPart part, double z) =>
        AddPlan(lines, labels, connector, layout.Circuits[0], part, z);

    /// <summary>Adds the plan symbol for one part of one circuit (per-level stairs draw a circuit per level, D-091).</summary>
    public static void AddPlan(MeshBuilder lines, List<PlanLabel>? labels, Connector connector, ConnectorCircuit circuit, ConnectorPlanPart part, double z)
    {
        ColorF color = part == ConnectorPlanPart.Through
            ? RenderPalette.ConnectorThrough
            : connector.Kind is ConnectorKind.Escalator or ConnectorKind.Travelator ? RenderPalette.Escalator : RenderPalette.Connector;
        double mid = circuit.BaseZ + (circuit.RiseM * 0.5);

        foreach (ConnectorPiece piece in circuit.Pieces)
        {
            if (!VisibleRange(piece, part, mid, out double s0, out double s1))
            {
                continue;
            }

            Point2[] outline = piece.Outline(s0, s1);
            if (part == ConnectorPlanPart.Through)
            {
                for (int i = 0; i < 4; i++)
                {
                    lines.DashedLine(Point3.FromPlan(outline[i], z), Point3.FromPlan(outline[(i + 1) % 4], z), 0.4, 0.25, color);
                }

                continue;
            }

            lines.PolygonOutline(outline, z, color);
            double h = piece.WidthM * 0.5;
            switch (piece.Kind)
            {
                case ConnectorPieceKind.Flight when piece.Risers > 2:
                    // Nosing lines between treads.
                    double going = piece.LengthM / (piece.Risers - 1);
                    for (int k = 1; k < piece.Risers - 1; k++)
                    {
                        double s = k * going;
                        if (s > s0 + Epsilon && s < s1 - Epsilon)
                        {
                            lines.Line(piece.At(s, -h), piece.At(s, h), z, color);
                        }
                    }

                    break;

                case ConnectorPieceKind.Incline or ConnectorPieceKind.Flat or ConnectorPieceKind.Belt:
                    // Dashed belt line down the middle (D-062).
                    lines.DashedLine(Point3.FromPlan(piece.At(s0, 0), z), Point3.FromPlan(piece.At(s1, 0), z), 0.35, 0.2, color);
                    break;
            }
        }

        if (part != ConnectorPlanPart.Through)
        {
            AddCutLine(lines, circuit.Pieces, mid, z, color);
            AddArrow(lines, labels, connector, circuit.Centreline, part, mid, z, color);
        }
    }

    /// <summary>Adds the 3D pieces: sloped walking surfaces, stringers, nosings and handrail lines.</summary>
    public static void Add3D(MeshBuilder solids, MeshBuilder lines, Connector connector, ConnectorLayout layout, double baseZ)
    {
        ColorF color = connector.Kind is ConnectorKind.Escalator or ConnectorKind.Travelator ? RenderPalette.Escalator : RenderPalette.Connector;
        ColorF surface = color.Shade(0.7f);
        ColorF side = color.Shade(0.42f);
        ColorF under = color.Shade(0.3f);
        ColorF edge = color;
        ColorF rail = color.WithAlpha(0.55f);

        foreach (ConnectorPiece piece in layout.Pieces)
        {
            double h = piece.WidthM * 0.5;
            double z0 = baseZ + piece.Z0, z1 = baseZ + piece.Z1;
            Point3 a0 = Point3.FromPlan(piece.At(0, -h), z0), a1 = Point3.FromPlan(piece.At(piece.LengthM, -h), z1);
            Point3 b0 = Point3.FromPlan(piece.At(0, h), z0), b1 = Point3.FromPlan(piece.At(piece.LengthM, h), z1);
            var down = new Point3(0, 0, -StringerDepthM);

            solids.Quad(a0, a1, b1, b0, surface);
            solids.Quad(a0 + down, a1 + down, b1 + down, b0 + down, under);
            solids.Quad(a0, a1, a1 + down, a0 + down, side);
            solids.Quad(b0, b1, b1 + down, b0 + down, side);

            lines.Line(a0, a1, edge);
            lines.Line(b0, b1, edge);
            lines.Line(a0, b0, edge);
            lines.Line(a1, b1, edge);

            var up = new Point3(0, 0, HandrailHeightM);
            lines.Line(a0 + up, a1 + up, rail);
            lines.Line(b0 + up, b1 + up, rail);

            if (piece.Kind == ConnectorPieceKind.Flight && piece.Risers > 2)
            {
                double going = piece.LengthM / (piece.Risers - 1);
                for (int k = 1; k < piece.Risers - 1; k++)
                {
                    double s = k * going;
                    double zs = baseZ + piece.ZAt(s) + 0.005;
                    lines.Line(Point3.FromPlan(piece.At(s, -h), zs), Point3.FromPlan(piece.At(s, h), zs), edge.WithAlpha(0.6f));
                }
            }
        }
    }

    /// <summary>Station range of a piece shown for a plan part; false when none of it shows.</summary>
    private static bool VisibleRange(ConnectorPiece piece, ConnectorPlanPart part, double mid, out double s0, out double s1)
    {
        s0 = 0;
        s1 = piece.LengthM;
        switch (part)
        {
            case ConnectorPlanPart.Lower when piece.IsSloped:
                if (piece.Z0 >= mid - Epsilon)
                {
                    return false;
                }

                s1 = piece.StationAt(mid);
                break;
            case ConnectorPlanPart.Lower:
                if (piece.Z0 > mid + Epsilon)
                {
                    return false;
                }

                break;
            case ConnectorPlanPart.Upper when piece.IsSloped:
                if (piece.Z1 <= mid + Epsilon)
                {
                    return false;
                }

                s0 = piece.StationAt(mid);
                break;
            case ConnectorPlanPart.Upper:
                if (piece.Z0 < mid - Epsilon)
                {
                    return false;
                }

                break;
        }

        return s1 - s0 > Epsilon;
    }

    /// <summary>Break line across the sloped piece at mid-rise.</summary>
    private static void AddCutLine(MeshBuilder lines, IReadOnlyList<ConnectorPiece> pieces, double mid, double z, ColorF color)
    {
        foreach (ConnectorPiece piece in pieces)
        {
            if (!piece.IsSloped || piece.Z0 > mid || piece.Z1 < mid)
            {
                continue;
            }

            double s = piece.StationAt(mid);
            double h = piece.WidthM * 0.5;
            double jog = Math.Min(0.2, piece.LengthM * 0.25);
            Point2[] zigzag =
            [
                piece.At(s, -h),
                piece.At(s, -0.25 * h),
                piece.At(s + jog, -0.1 * h),
                piece.At(s - jog, 0.1 * h),
                piece.At(s, 0.25 * h),
                piece.At(s, h),
            ];
            lines.Polyline(zigzag, z, color);
            return;
        }
    }

    /// <summary>Direction arrow along the centreline with an UP / DN label (D-062).</summary>
    private static void AddArrow(MeshBuilder lines, List<PlanLabel>? labels, Connector connector, IReadOnlyList<CentrelinePoint> centreline, ConnectorPlanPart part, double mid, double z, ColorF color)
    {
        List<Point2> path = SplitCentreline(centreline, mid, part == ConnectorPlanPart.Lower);
        if (path.Count < 2)
        {
            return;
        }

        lines.Polyline(path, z, color);
        double size = Math.Min(0.45, connector.WidthM * 0.35);

        // Lower half: arrow runs up from the bottom landing. Upper half: the arrow points the way people travel
        // on it (down for stairs and down escalators, up for up escalators).
        bool headAtEnd = part == ConnectorPlanPart.Lower
            ? connector.Direction != ConnectorDirection.Down
            : connector.Direction == ConnectorDirection.Up;
        if (headAtEnd)
        {
            AddHead(lines, path[^2], path[^1], size, z, color);
        }
        else
        {
            AddHead(lines, path[1], path[0], size, z, color);
        }

        if (labels is null || connector.IsTravelator)
        {
            return;
        }

        string text = part == ConnectorPlanPart.Lower
            ? connector.Direction == ConnectorDirection.Down ? "DN" : "UP"
            : connector.Direction == ConnectorDirection.Up ? "UP" : "DN";
        Point2 anchor = part == ConnectorPlanPart.Lower ? path[0] : path[^1];
        Point2 inward = part == ConnectorPlanPart.Lower ? (path[1] - path[0]).Normalised() : (path[^2] - path[^1]).Normalised();
        labels.Add(new PlanLabel(anchor + (inward * 0.7), text, color));
    }

    private static void AddHead(MeshBuilder lines, Point2 from, Point2 tip, double size, double z, ColorF color)
    {
        Point2 back = (from - tip).Normalised();
        if (back == Point2.Zero)
        {
            return;
        }

        Point2 side = back.Perpendicular();
        lines.Line(tip, tip + (back * size) + (side * (size * 0.6)), z, color);
        lines.Line(tip, tip + (back * size) - (side * (size * 0.6)), z, color);
    }

    /// <summary>The centreline below (or above) <paramref name="mid"/>, with the crossing point interpolated.</summary>
    private static List<Point2> SplitCentreline(IReadOnlyList<CentrelinePoint> line, double mid, bool lower)
    {
        var result = new List<Point2>();
        for (int i = 0; i < line.Count; i++)
        {
            CentrelinePoint p = line[i];
            bool inside = lower ? p.Z <= mid + Epsilon : p.Z >= mid - Epsilon;
            if (i > 0)
            {
                CentrelinePoint q = line[i - 1];
                bool crosses = (q.Z - mid) * (p.Z - mid) < 0;
                if (crosses)
                {
                    double t = (mid - q.Z) / (p.Z - q.Z);
                    result.Add(q.Point + ((p.Point - q.Point) * t));
                }
            }

            if (inside)
            {
                result.Add(p.Point);
            }
        }

        // Drop zero-length steps so the arrow head always has a direction.
        var cleaned = new List<Point2>(result.Count);
        foreach (Point2 p in result)
        {
            if (cleaned.Count == 0 || Point2.Distance(cleaned[^1], p) > 1e-4)
            {
                cleaned.Add(p);
            }
        }

        return cleaned;
    }
}
