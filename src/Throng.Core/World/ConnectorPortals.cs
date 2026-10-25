using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.World;

/// <summary>Which end edge of a connector piece (u = 0 or u = length).</summary>
public enum PieceEnd
{
    /// <summary>The piece's start edge (u = 0), facing back along its axis.</summary>
    Start,

    /// <summary>The piece's end edge (u = length), facing along its axis.</summary>
    End,
}

/// <summary>An end edge of one connector piece, in plan, with its absolute height and outward direction.</summary>
/// <param name="Piece">Index into <see cref="ConnectorLayout.Pieces"/>.</param>
/// <param name="End">Which end.</param>
/// <param name="A">First corner (plan).</param>
/// <param name="B">Second corner (plan).</param>
/// <param name="Outward">Unit plan direction leaving the piece through this edge.</param>
/// <param name="ElevationM">Absolute height of the edge.</param>
public readonly record struct PieceEdge(int Piece, PieceEnd End, Point2 A, Point2 B, Point2 Outward, double ElevationM);

/// <summary>Two pieces of one connector joined along part of an end edge each (flight to landing, flat to incline).</summary>
/// <param name="EdgeA">The edge on the first piece.</param>
/// <param name="EdgeB">The edge on the second piece.</param>
/// <param name="A">Start of the shared stretch (plan).</param>
/// <param name="B">End of the shared stretch (plan).</param>
public readonly record struct PieceJoint(PieceEdge EdgeA, PieceEdge EdgeB, Point2 A, Point2 B);

/// <summary>A piece end edge lying at a level's floor height: where people step between the floor and the connector.</summary>
/// <param name="Edge">The piece edge.</param>
/// <param name="LevelId">Level whose floor the edge sits on.</param>
/// <param name="IsServed">
/// False for an excluded intermediate level of a per-level stair (D-093): people pass over that floor between
/// circuits but don't get on or off there.
/// </param>
/// <param name="IsLowerEnd">True for the end nearer the connector's bottom (a travelator's start).</param>
public readonly record struct FloorEnd(PieceEdge Edge, Guid LevelId, bool IsServed, bool IsLowerEnd)
{
    /// <summary>True for an end on the connector's top level (one-way stairs and ramps, round 007).</summary>
    public bool IsUpperEnd { get; init; }
}

/// <summary>
/// Derives how a connector's pieces join each other and the floors (M2, §3.3): which end edges are shared
/// between pieces and which open onto a level. Used by <see cref="WorldCompiler"/> for portals and by the
/// simulation's navigation world.
/// </summary>
/// <remarks>
/// Only the end edges (u = 0 and u = length) of pieces are open; their long sides are balustrades. Two edges join
/// when they are collinear, face each other, overlap by more than <see cref="MinOverlapM"/> and are at the same
/// height. An end edge that joins nothing and lies at a level's floor height opens onto that level.
/// </remarks>
public static class ConnectorPortals
{
    /// <summary>Shortest shared stretch that counts as a joint, in metres.</summary>
    public const double MinOverlapM = 0.1;

    private const double LineToleranceM = 0.02;
    private const double HeightToleranceM = 0.02;

    /// <summary>All end edges of a layout's pieces, in absolute heights from the connector's bottom level.</summary>
    public static IReadOnlyList<PieceEdge> Edges(ConnectorLayout layout, double bottomElevationM)
    {
        var edges = new List<PieceEdge>(layout.Pieces.Count * 2);
        for (int i = 0; i < layout.Pieces.Count; i++)
        {
            ConnectorPiece p = layout.Pieces[i];
            double h = p.WidthM * 0.5;
            edges.Add(new PieceEdge(i, PieceEnd.Start, p.At(0, -h), p.At(0, h), p.Axis * -1, bottomElevationM + p.Z0));
            edges.Add(new PieceEdge(i, PieceEnd.End, p.At(p.LengthM, -h), p.At(p.LengthM, h), p.Axis, bottomElevationM + p.Z1));
        }

        return edges;
    }

    /// <summary>Joints between pieces of the same connector.</summary>
    public static IReadOnlyList<PieceJoint> Joints(ConnectorLayout layout, double bottomElevationM) =>
        Joints(Edges(layout, bottomElevationM));

    /// <summary>Floor ends of a connector: its piece edges that open onto a level.</summary>
    public static IReadOnlyList<FloorEnd> FloorEnds(Study study, Connector connector, ConnectorLayout layout)
    {
        Level? bottom = study.FindLevel(connector.BottomLevelId);
        if (bottom is null || layout.Pieces.Count == 0)
        {
            return [];
        }

        IReadOnlyList<PieceEdge> edges = Edges(layout, bottom.ElevationM);
        IReadOnlyList<PieceJoint> joints = Joints(edges);
        var joined = new HashSet<(int, PieceEnd)>();
        foreach (PieceJoint joint in joints)
        {
            joined.Add((joint.EdgeA.Piece, joint.EdgeA.End));
            joined.Add((joint.EdgeB.Piece, joint.EdgeB.End));
        }

        Level? top = study.FindLevel(connector.TopLevelId);
        var ends = new List<FloorEnd>();
        foreach (PieceEdge edge in edges)
        {
            if (joined.Contains((edge.Piece, edge.End)))
            {
                continue;
            }

            if (connector.IsTravelator)
            {
                // A belt's two ends both sit on its own level.
                ends.Add(new FloorEnd(edge, bottom.Id, IsServed: true, IsLowerEnd: edge.End == PieceEnd.Start));
                continue;
            }

            Level? level = LevelAt(study, edge.ElevationM, bottom, top);
            if (level is null)
            {
                continue;
            }

            bool served = study.ConnectorServes(connector, level.Id);
            bool passOver = !served && study.IsIntermediateLevel(connector, level.Id);
            if (served || passOver)
            {
                ends.Add(new FloorEnd(edge, level.Id, served, IsLowerEnd: Math.Abs(level.ElevationM - bottom.ElevationM) < HeightToleranceM)
                {
                    IsUpperEnd = top is not null && Math.Abs(level.ElevationM - top.ElevationM) < HeightToleranceM,
                });
            }
        }

        return ends;
    }

    /// <summary>
    /// True when people may step from the floor onto the connector at this end (one-way belts, D-107; stairs and
    /// ramps marked Up or Down are one-way too, D-115: no getting on at the end they lead to).
    /// </summary>
    public static bool AllowsEntry(Connector connector, FloorEnd end) => connector.Kind switch
    {
        ConnectorKind.Escalator => end.IsLowerEnd == BeltRunsUp(connector),
        ConnectorKind.Travelator => end.IsLowerEnd,
        _ => connector.Direction switch
        {
            ConnectorDirection.Up => !end.IsUpperEnd,
            ConnectorDirection.Down => !end.IsLowerEnd,
            _ => true,
        },
    };

    /// <summary>True when people may step off the connector onto the floor at this end.</summary>
    public static bool AllowsExit(Connector connector, FloorEnd end) => connector.Kind switch
    {
        ConnectorKind.Escalator => end.IsLowerEnd != BeltRunsUp(connector),
        ConnectorKind.Travelator => !end.IsLowerEnd,
        _ => connector.Direction switch
        {
            ConnectorDirection.Up => !end.IsLowerEnd,
            ConnectorDirection.Down => !end.IsUpperEnd,
            _ => true,
        },
    };

    /// <summary>
    /// True when an escalator's belt runs up its pieces' axes. Down runs down; anything else (a standalone
    /// reversible unit) runs up until direction schedules arrive.
    /// </summary>
    public static bool BeltRunsUp(Connector connector) => connector.Direction != ConnectorDirection.Down;

    private static List<PieceJoint> Joints(IReadOnlyList<PieceEdge> edges)
    {
        var joints = new List<PieceJoint>();
        for (int i = 0; i < edges.Count; i++)
        {
            for (int j = i + 1; j < edges.Count; j++)
            {
                PieceEdge a = edges[i];
                PieceEdge b = edges[j];
                if (a.Piece == b.Piece
                    || Math.Abs(a.ElevationM - b.ElevationM) > HeightToleranceM
                    || Point2.Dot(a.Outward, b.Outward) > -0.99)
                {
                    continue;
                }

                // Collinear: b's corners lie on a's line.
                Point2 along = (a.B - a.A).Normalised();
                if (Math.Abs(Point2.Cross(along, b.A - a.A)) > LineToleranceM || Math.Abs(Point2.Cross(along, b.B - a.A)) > LineToleranceM)
                {
                    continue;
                }

                double a0 = 0, a1 = (a.B - a.A).Length;
                double b0 = Point2.Dot(b.A - a.A, along), b1 = Point2.Dot(b.B - a.A, along);
                double lo = Math.Max(a0, Math.Min(b0, b1));
                double hi = Math.Min(a1, Math.Max(b0, b1));
                if (hi - lo > MinOverlapM)
                {
                    joints.Add(new PieceJoint(a, b, a.A + (along * lo), a.A + (along * hi)));
                }
            }
        }

        return joints;
    }

    private static Level? LevelAt(Study study, double elevationM, Level bottom, Level? top)
    {
        double max = top?.ElevationM ?? bottom.ElevationM;
        foreach (Level level in study.Levels)
        {
            if (Math.Abs(level.ElevationM - elevationM) < HeightToleranceM
                && level.ElevationM >= bottom.ElevationM - HeightToleranceM
                && level.ElevationM <= max + HeightToleranceM)
            {
                return level;
            }
        }

        return null;
    }
}
