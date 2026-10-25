using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.World;

/// <summary>What a <see cref="VerticalCut"/> is on its level.</summary>
public enum VerticalCutKind
{
    /// <summary>A connector's footprint on its bottom level (the floor it stands on).</summary>
    Footprint,

    /// <summary>A connector's linked opening on its top level (or any level it passes through, D-055).</summary>
    Opening,

    /// <summary>A shaft passing through the level (D-056).</summary>
    Shaft,
}

/// <summary>One polygon removed from a level's walkable area by a connector or shaft.</summary>
/// <param name="Kind">What made the cut.</param>
/// <param name="SourceId">Connector or shaft id.</param>
/// <param name="Polygons">Closed polygons (outers counter-clockwise).</param>
public sealed record VerticalCut(VerticalCutKind Kind, Guid SourceId, IReadOnlyList<IReadOnlyList<Point2>> Polygons);

/// <summary>
/// Areas that connectors and shafts remove from each level's walkable area (D-055, D-056). Derived on demand,
/// never stored, so openings always follow their connectors.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Bottom level: the connector's footprint (people step on at the bottom edge, never walk under it).</item>
/// <item>Top level: the opening — every part of the connector without <see cref="HeadroomM"/> under the upper
/// floor, grown by the connector's clearance on all sides except the top exit edge, which stays open so people
/// can step off.</item>
/// <item>Levels between the two: the whole footprint plus clearance.</item>
/// <item>Per-level stairs (D-091): each circuit's footprint on the level it starts from (from the second
/// circuit on, only the area its pieces actually cover, so the previous circuit's exit stays floor) and its
/// opening on the level it arrives at. Excluded levels are cut the same way; they just have no exit (D-093).</item>
/// <item>Shafts: the outline on every level from base to top, inclusive; "no stop" levels too (D-087).</item>
/// </list>
/// </remarks>
public static class VerticalCuts
{
    /// <summary>
    /// Clear height needed under the upper floor, in metres, including an allowance for the slab. Parts of a
    /// connector closer than this to the upper floor are cut out of it.
    /// </summary>
    public const double HeadroomM = 2.2;

    /// <summary>Every cut on a level. Connectors whose layout fails (bad levels or values) are skipped.</summary>
    public static IReadOnlyList<VerticalCut> ForLevel(Study study, Guid levelId, IPolygonOps ops)
    {
        var cuts = new List<VerticalCut>();
        Level? level = study.FindLevel(levelId);
        if (level is null)
        {
            return cuts;
        }

        foreach (Connector connector in study.Connectors)
        {
            Level? bottom = study.FindLevel(connector.BottomLevelId);
            Level? top = study.FindLevel(connector.TopLevelId);
            if (bottom is null || top is null)
            {
                continue;
            }

            bool isBottom = connector.BottomLevelId == levelId;
            bool isTop = connector.TopLevelId == levelId;
            bool between = !isBottom && !isTop && level.ElevationM > bottom.ElevationM && level.ElevationM < top.ElevationM;
            if (!isBottom && !isTop && !between)
            {
                continue;
            }

            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, study);
            if (!layout.IsSuccess || layout.Value is not { } l)
            {
                continue;
            }

            if (l.Circuits.Count > 1)
            {
                AddCircuitCuts(cuts, connector, l, levelId, ops);
                continue;
            }

            if (isBottom)
            {
                cuts.Add(new VerticalCut(VerticalCutKind.Footprint, connector.Id, [l.Footprint]));
            }
            else if (isTop)
            {
                cuts.Add(new VerticalCut(VerticalCutKind.Opening, connector.Id, Opening(connector, l, ops)));
            }
            else
            {
                cuts.Add(new VerticalCut(VerticalCutKind.Opening, connector.Id, ops.Offset([l.Footprint], Math.Max(0, connector.ClearanceM))));
            }
        }

        foreach (Shaft shaft in study.ShaftsSpanning(levelId))
        {
            if (shaft.Outline.Count >= 3)
            {
                cuts.Add(new VerticalCut(VerticalCutKind.Shaft, shaft.Id, [shaft.Outline]));
            }
        }

        return cuts;
    }

    private static void AddCircuitCuts(List<VerticalCut> cuts, Connector connector, ConnectorLayout layout, Guid levelId, IPolygonOps ops)
    {
        var footprint = new List<IReadOnlyList<Point2>>();
        var openings = new List<IReadOnlyList<Point2>>();
        foreach (ConnectorCircuit circuit in layout.Circuits)
        {
            if (circuit.BottomLevelId == levelId)
            {
                if (circuit.Index == 0)
                {
                    footprint.Add(circuit.Footprint);
                }
                else
                {
                    footprint.AddRange(ops.Union(circuit.Pieces.Select(p => (IReadOnlyList<Point2>)p.Outline())));
                }
            }

            if (circuit.TopLevelId == levelId)
            {
                openings.AddRange(Opening(connector, circuit, ops));
            }
        }

        if (footprint.Count > 0)
        {
            cuts.Add(new VerticalCut(VerticalCutKind.Footprint, connector.Id, footprint));
        }

        if (openings.Count > 0)
        {
            cuts.Add(new VerticalCut(VerticalCutKind.Opening, connector.Id, openings));
        }
    }

    /// <summary>The linked opening of a single-circuit connector in its top level.</summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> Opening(Connector connector, ConnectorLayout layout, IPolygonOps ops) =>
        Opening(connector, layout.Circuits[^1], ops);

    /// <summary>
    /// A circuit's linked opening in the level it arrives at (D-055, D-065): pieces (or the parts of them) higher
    /// than its rise − <see cref="HeadroomM"/>, grown by the clearance, minus a strip just beyond the exit edge.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> Opening(Connector connector, ConnectorCircuit circuit, IPolygonOps ops)
    {
        double threshold = circuit.BaseZ + Math.Max(0, circuit.RiseM - HeadroomM);
        var parts = new List<IReadOnlyList<Point2>>();
        foreach (ConnectorPiece piece in circuit.Pieces)
        {
            if (Math.Max(piece.Z0, piece.Z1) <= threshold + 1e-9 || piece.LengthM <= 1e-9)
            {
                continue;
            }

            double start = piece.IsSloped ? piece.StationAt(threshold) : 0;
            if (piece.LengthM - start > 1e-6)
            {
                parts.Add(piece.Outline(start, piece.LengthM));
            }
        }

        if (parts.Count == 0)
        {
            return [];
        }

        double clearance = Math.Max(0, connector.ClearanceM);
        IReadOnlyList<IReadOnlyList<Point2>> grown = clearance > 1e-6 ? ops.Offset(parts, clearance) : ops.Union(parts);

        // Keep the floor directly beyond the top exit edge, so the opening never separates the connector from
        // the floor it arrives on.
        Point2 axis = circuit.TopAxis;
        Point2 across = axis.Perpendicular();
        double half = (connector.WidthM * 0.5) + clearance + 0.01;
        double depth = clearance + 0.01;
        Point2 c = circuit.TopLanding;
        Point2[] exitStrip =
        [
            c + (across * -half),
            c + (axis * depth) + (across * -half),
            c + (axis * depth) + (across * half),
            c + (across * half),
        ];

        return ops.Difference(grown, [exitStrip]);
    }

    /// <summary>
    /// Gaps between connector footprints narrower than this are closed (D-126): the strip between the units of an
    /// escalator pair, or a stair right beside an escalator, is decking or cladding in a real building, not a walkway.
    /// </summary>
    public const double ClosedGapM = 1.0;

    /// <summary>
    /// All cut polygons on a level, flattened (for subtracting from walkable). Footprints are closed together first
    /// (grown by half <see cref="ClosedGapM"/>, then shrunk back), which fills narrow gaps between them without
    /// moving their outer edges. Openings are left as they are: their exit notches must stay open.
    /// </summary>
    public static List<IReadOnlyList<Point2>> PolygonsForLevel(Study study, Guid levelId, IPolygonOps ops)
    {
        var polygons = new List<IReadOnlyList<Point2>>();
        var footprints = new List<IReadOnlyList<Point2>>();
        foreach (VerticalCut cut in ForLevel(study, levelId, ops))
        {
            (cut.Kind == VerticalCutKind.Footprint ? footprints : polygons).AddRange(cut.Polygons);
        }

        if (footprints.Count > 0)
        {
            double half = ClosedGapM * 0.5;
            polygons.AddRange(ops.Offset(ops.Offset(footprints, half), -half));
        }

        return polygons;
    }
}
