using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.World;

/// <summary>
/// Compiles an authored <see cref="Study"/> into the simulation's <see cref="WorldModel"/>.
/// </summary>
/// <remarks>
/// One flat floor surface per level, carrying that level's walkable and obstacle outlines. When a polygon
/// library is supplied, walls are thickened into obstacles and connector openings, footprints and shafts are
/// added as obstacles too (D-055, D-056). Each connector piece (flight, landing, flat) becomes its own
/// <see cref="SurfaceKind.Connector"/> surface with u running up the piece (§7.1, D-004), and portals join pieces
/// to each other and to the floors they open onto (M2, <see cref="ConnectorPortals"/>). The net walkable union
/// itself is available from <see cref="WalkableArea"/>.
/// </remarks>
public static class WorldCompiler
{
    /// <summary>Compiles the study, or fails with the first validation error.</summary>
    /// <param name="study">Study to compile.</param>
    /// <param name="polygonOps">Optional polygon library; when given, walls are thickened into obstacles.</param>
    public static Result<WorldModel> Compile(Study study, IPolygonOps? polygonOps = null)
    {
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        ValidationMessage? firstError = findings.FirstOrDefault(m => m.Severity == ValidationSeverity.Error);
        if (firstError is not null)
        {
            return Result<WorldModel>.Fail(firstError.Message);
        }

        var surfaces = new List<Surface>();
        foreach (Level level in study.LevelsByElevation())
        {
            var walkable = new List<IReadOnlyList<Point2>>();
            var obstacles = new List<IReadOnlyList<Point2>>();
            walkable.AddRange(study.RegionsOn(level.Id).Where(r => r.Kind == RegionKind.Walkable).Select(r => (IReadOnlyList<Point2>)r.Outline.ToArray()));
            foreach (Region region in study.RegionsPresentOn(level.Id))
            {
                switch (region.Kind)
                {
                    case RegionKind.Obstacle:
                        obstacles.Add(region.Outline.ToArray());
                        break;
                    case RegionKind.Wall when polygonOps is not null:
                        obstacles.AddRange(polygonOps.ThickenPolyline(region.Outline, region.ThicknessM));
                        break;
                }
            }

            if (polygonOps is not null)
            {
                obstacles.AddRange(VerticalCuts.PolygonsForLevel(study, level.Id, polygonOps));
            }

            foreach (Device device in study.DevicesOn(level.Id))
            {
                obstacles.AddRange(DeviceGeometry.Blocking(device));
            }

            surfaces.Add(new Surface
            {
                Id = level.Id,
                Kind = SurfaceKind.Floor,
                LevelId = level.Id,
                SourceId = level.Id,
                Name = level.Name,
                BaseElevationM = level.ElevationM,
                Walkable = walkable,
                Obstacles = obstacles,
            });
        }

        var portals = new List<Portal>();
        AddConnectorSurfaces(study, surfaces, portals);
        return Result<WorldModel>.Ok(new WorldModel { Surfaces = surfaces, Portals = portals });
    }

    /// <summary>
    /// One sloped (or flat) surface per connector piece, in piece-local coordinates (u up the piece, v left), plus
    /// the portals joining pieces to each other and to the floors they open onto (served levels only, D-093).
    /// </summary>
    private static void AddConnectorSurfaces(Study study, List<Surface> surfaces, List<Portal> portals)
    {
        foreach (Connector connector in study.Connectors)
        {
            Level? bottom = study.FindLevel(connector.BottomLevelId);
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, study);
            if (bottom is null || !layout.IsSuccess || layout.Value is not { } l)
            {
                continue;
            }

            var pieceSurfaces = new Surface[l.Pieces.Count];
            for (int i = 0; i < l.Pieces.Count; i++)
            {
                ConnectorPiece piece = l.Pieces[i];
                double h = piece.WidthM * 0.5;
                pieceSurfaces[i] = new Surface
                {
                    Kind = SurfaceKind.Connector,
                    LevelId = null,
                    SourceId = connector.Id,
                    Name = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{connector.Name} · {piece.Kind} {i + 1}"),
                    Origin = piece.Start,
                    AxisU = piece.Axis,
                    BaseElevationM = bottom.ElevationM + piece.Z0,
                    SlopeAlongU = piece.LengthM > 1e-9 ? (piece.Z1 - piece.Z0) / piece.LengthM : 0,
                    Walkable = [new Point2[] { new(0, -h), new(piece.LengthM, -h), new(piece.LengthM, h), new(0, h) }],
                };
                surfaces.Add(pieceSurfaces[i]);
            }

            foreach (PieceJoint joint in ConnectorPortals.Joints(l, bottom.ElevationM))
            {
                ConnectorPiece a = l.Pieces[joint.EdgeA.Piece];
                ConnectorPiece b = l.Pieces[joint.EdgeB.Piece];
                portals.Add(new Portal
                {
                    Kind = PortalKind.Edge,
                    SurfaceA = pieceSurfaces[joint.EdgeA.Piece].Id,
                    SurfaceB = pieceSurfaces[joint.EdgeB.Piece].Id,
                    EdgeOnA = (ToLocal(a, joint.A), ToLocal(a, joint.B)),
                    EdgeOnB = (ToLocal(b, joint.A), ToLocal(b, joint.B)),
                    ConnectorId = connector.Id,
                });
            }

            foreach (FloorEnd end in ConnectorPortals.FloorEnds(study, connector, l))
            {
                if (!end.IsServed)
                {
                    continue;
                }

                ConnectorPiece piece = l.Pieces[end.Edge.Piece];
                portals.Add(new Portal
                {
                    Kind = PortalKind.Edge,
                    SurfaceA = end.LevelId,
                    SurfaceB = pieceSurfaces[end.Edge.Piece].Id,
                    EdgeOnA = (end.Edge.A, end.Edge.B),
                    EdgeOnB = (ToLocal(piece, end.Edge.A), ToLocal(piece, end.Edge.B)),
                    AllowsAToB = ConnectorPortals.AllowsEntry(connector, end),
                    AllowsBToA = ConnectorPortals.AllowsExit(connector, end),
                    ConnectorId = connector.Id,
                });
            }
        }
    }

    /// <summary>Plan point to a piece's local (u along, v left) coordinates.</summary>
    private static Point2 ToLocal(ConnectorPiece piece, Point2 plan)
    {
        Point2 d = plan - piece.Start;
        return new Point2(Point2.Dot(d, piece.Axis), Point2.Dot(d, piece.Across));
    }
}
