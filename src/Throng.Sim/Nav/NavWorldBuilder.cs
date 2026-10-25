using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.Sim.Nav;

/// <summary>
/// Builds the <see cref="NavWorld"/> from a study (round 006 design): one grid per level's net walkable floor,
/// one per connector piece, links across piece joints and floor ends, and the sink zones.
/// </summary>
/// <remarks>
/// Staff-only connectors (D-110) are left out entirely until staff routing arrives in M3; their footprints and
/// openings are still cut from the floors.
/// </remarks>
public static class NavWorldBuilder
{
    /// <summary>Default belt values when a connector's preset can't be found.</summary>
    private const double EscalatorSpeed = 0.5, EscalatorThroughput = 100, TravelatorSpeed = 0.65, TravelatorThroughput = 90;

    /// <summary>Default walking flow when a stair or ramp preset can't be found, people per metre per minute.</summary>
    private const double StairFlow = 50, RampFlow = 60;

    /// <summary>Largest grid accepted per floor, in cells (about 2 km² at 0.25 m).</summary>
    private const long MaxCellsPerFloor = 32_000_000;

    /// <summary>Builds the world. Problems that don't stop a run (an unconnected end, an empty sink) go to <paramref name="findings"/>.</summary>
    public static Result<NavWorld> Build(Study study, IPolygonOps ops, List<ValidationMessage> findings)
    {
        double cs = Math.Clamp(study.Settings.NavCellSizeM, 0.1, 1.0);
        var surfaces = new List<NavSurface>();
        var floorByLevel = new Dictionary<Guid, int>();

        foreach (Level level in study.LevelsAscending())
        {
            Result<IReadOnlyList<IReadOnlyList<Point2>>> net = WalkableArea.Compute(study, level.Id, ops);
            if (!net.IsSuccess || net.Value is null)
            {
                return Result<NavWorld>.Fail(net.Error ?? $"The walkable area of '{level.Name}' couldn't be computed.");
            }

            if (net.Value.Count == 0)
            {
                continue;
            }

            Result<NavSurface> floor = BuildFloor(study, level, net.Value, cs, surfaces.Count);
            if (!floor.IsSuccess || floor.Value is null)
            {
                return Result<NavWorld>.Fail(floor.Error ?? "A floor grid couldn't be built.");
            }

            floorByLevel[level.Id] = surfaces.Count;
            surfaces.Add(floor.Value);
        }

        var connectors = new List<NavConnector>();
        var portals = new List<NavPortal>();
        var links = new List<NavLink>();
        foreach (Connector connector in study.Connectors)
        {
            if (connector.StaffOnly)
            {
                continue;
            }

            Level? bottom = study.FindLevel(connector.BottomLevelId);
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, study);
            if (bottom is null || !layout.IsSuccess || layout.Value is not { } l || l.Pieces.Count == 0)
            {
                continue;
            }

            AddConnector(study, connector, l, bottom, cs, surfaces, floorByLevel, connectors, portals, links, findings);
        }

        // Global cell numbering.
        int[] cellBase = new int[surfaces.Count];
        int total = 0;
        Bounds2 bounds = Bounds2.Empty;
        for (int s = 0; s < surfaces.Count; s++)
        {
            cellBase[s] = total;
            total += surfaces[s].CellCount;
            NavSurface surface = surfaces[s];
            double w = surface.Nx * surface.CellU, h = surface.Ny * surface.CellV;
            bounds = bounds.Include(surface.ToPlan(0, 0)).Include(surface.ToPlan(w, 0)).Include(surface.ToPlan(w, h)).Include(surface.ToPlan(0, h));
        }

        // Keep the quickest of any duplicate links, then group them by end cell for the reverse searches.
        var best = new Dictionary<long, NavLink>();
        foreach (NavLink link in links)
        {
            long key = ((long)link.From << 32) | (uint)link.To;
            if (!best.TryGetValue(key, out NavLink existing) || link.Seconds < existing.Seconds)
            {
                best[key] = link;
            }
        }

        NavLink[] sorted = [.. best.Values.OrderBy(k => k.To).ThenBy(k => k.From)];
        int[] start = new int[total + 1];
        foreach (NavLink link in sorted)
        {
            start[link.To + 1]++;
        }

        for (int i = 0; i < total; i++)
        {
            start[i + 1] += start[i];
        }

        List<NavSink> sinks = BuildSinks(study, surfaces, floorByLevel, findings);

        return Result<NavWorld>.Ok(new NavWorld
        {
            Surfaces = surfaces,
            Connectors = connectors,
            Portals = portals,
            Sinks = sinks,
            CellBase = cellBase,
            TotalCells = total,
            LinksInto = sorted,
            LinksIntoStart = start,
            Bounds = bounds,
            FloorByLevel = floorByLevel,
        });
    }

    /// <summary>Raster cells (by centre) of polygons on a floor grid.</summary>
    public static List<int> CellsInside(NavSurface floor, IEnumerable<IReadOnlyList<Point2>> polygons, bool walkableOnly)
    {
        var cells = new List<int>();
        GridRaster.Fill(polygons, floor.Origin, floor.CellU, floor.Nx, floor.Ny, cell =>
        {
            if (!walkableOnly || floor.Walkable[cell] != 0)
            {
                cells.Add(cell);
            }
        });
        return cells;
    }

    private static Result<NavSurface> BuildFloor(Study study, Level level, IReadOnlyList<IReadOnlyList<Point2>> net, double cs, int index)
    {
        Bounds2 b = Bounds2.Empty;
        foreach (IReadOnlyList<Point2> polygon in net)
        {
            b = b.Union(Bounds2.FromPoints(polygon));
        }

        b = b.Inflate(cs);
        int nx = Math.Max(1, (int)Math.Ceiling(b.Width / cs));
        int ny = Math.Max(1, (int)Math.Ceiling(b.Height / cs));
        if ((long)nx * ny > MaxCellsPerFloor)
        {
            return Result<NavSurface>.Fail(string.Create(CultureInfo.InvariantCulture,
                $"'{level.Name}' is too large to simulate at {cs:0.00} m cells ({nx} × {ny})."));
        }

        int count = nx * ny;
        var walkable = new byte[count];
        var slowness = new float[count];
        Array.Fill(slowness, 1f);
        var origin = new Point2(b.MinX, b.MinY);
        GridRaster.Fill(net, origin, cs, nx, ny, cell => walkable[cell] = 1);

        foreach (Region zone in study.RegionsOn(level.Id).Where(r => r.Kind == RegionKind.Zone && r.ZoneKind == ZoneKind.Slow && r.Outline.Count >= 3))
        {
            float s = (float)(1.0 / Math.Clamp(zone.SpeedFactor, 0.1, 3.0));
            GridRaster.Fill([zone.Outline], origin, cs, nx, ny, cell => slowness[cell] = Math.Max(slowness[cell], s));
        }

        return Result<NavSurface>.Ok(new NavSurface
        {
            Index = index,
            Kind = NavSurfaceKind.Floor,
            LevelId = level.Id,
            Name = level.Name,
            Origin = origin,
            AxisU = new Point2(1, 0),
            AxisV = new Point2(0, 1),
            CellU = cs,
            CellV = cs,
            Nx = nx,
            Ny = ny,
            Z0 = level.ElevationM,
            Walkable = walkable,
            Slowness = slowness,
            WallDistance = WallDistances(walkable, nx, ny, cs),
        });
    }

    /// <summary>Two-pass chamfer distance (1, √2) from unwalkable cells and the grid edge, in metres, capped at 2 m.</summary>
    private static float[] WallDistances(byte[] walkable, int nx, int ny, double cs)
    {
        const float Diagonal = 1.41421356f;
        var d = new float[nx * ny];
        for (int c = 0; c < d.Length; c++)
        {
            d[c] = walkable[c] != 0 ? float.MaxValue : 0f;
        }

        float At(int i, int j) => i < 0 || j < 0 || i >= nx || j >= ny ? 0f : d[(j * nx) + i];

        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                int c = (j * nx) + i;
                if (d[c] == 0)
                {
                    continue;
                }

                float v = Math.Min(At(i - 1, j) + 1, At(i, j - 1) + 1);
                v = Math.Min(v, Math.Min(At(i - 1, j - 1), At(i + 1, j - 1)) + Diagonal);
                d[c] = Math.Min(d[c], v);
            }
        }

        for (int j = ny - 1; j >= 0; j--)
        {
            for (int i = nx - 1; i >= 0; i--)
            {
                int c = (j * nx) + i;
                if (d[c] == 0)
                {
                    continue;
                }

                float v = Math.Min(At(i + 1, j) + 1, At(i, j + 1) + 1);
                v = Math.Min(v, Math.Min(At(i + 1, j + 1), At(i - 1, j + 1)) + Diagonal);
                d[c] = Math.Min(d[c], v);
            }
        }

        // Centre-to-centre cells to metres from the centre to the wall's edge.
        for (int c = 0; c < d.Length; c++)
        {
            d[c] = d[c] == 0 ? 0 : (float)Math.Min(2.0, Math.Max(0, d[c] - 0.5) * cs);
        }

        return d;
    }

    private static void AddConnector(
        Study study, Connector connector, ConnectorLayout layout, Level bottom, double cs, List<NavSurface> surfaces,
        Dictionary<Guid, int> floorByLevel, List<NavConnector> connectors, List<NavPortal> portals, List<NavLink> links,
        List<ValidationMessage> findings)
    {
        ConnectorTemplate? template = StudyTemplates.FindConnectorTemplate(study, connector);
        bool belt = connector.Kind is ConnectorKind.Escalator or ConnectorKind.Travelator;
        double speed = 0, throughput = 0;
        if (belt)
        {
            bool escalator = connector.Kind == ConnectorKind.Escalator;
            speed = template is { BeltSpeedMps: > 0 } ? template.BeltSpeedMps : escalator ? EscalatorSpeed : TravelatorSpeed;
            throughput = template is { ThroughputPerMin: > 0 } ? template.ThroughputPerMin : escalator ? EscalatorThroughput : TravelatorThroughput;
        }

        double flowPerMetre = template is { FlowPerMetrePerMin: > 0 } ? template.FlowPerMetrePerMin
            : connector.Kind == ConnectorKind.Ramp ? RampFlow : StairFlow;
        double capacity = belt ? throughput / 60.0 : flowPerMetre * Math.Max(0.1, connector.WidthM) / 60.0;

        var nav = new NavConnector
        {
            Index = connectors.Count,
            ConnectorId = connector.Id,
            Name = connector.Name,
            Kind = connector.Kind,
            RiserM = layout.ActualRiserM > 0 ? layout.ActualRiserM : connector.RiserM,
            GoingM = connector.GoingM,
            BeltSpeedMps = speed,
            ThroughputPerMin = throughput,
            StepFreeOk = connector.Kind is ConnectorKind.Ramp or ConnectorKind.Travelator,
            CapacityPerSecond = Math.Max(0.05, capacity),
        };
        connectors.Add(nav);

        // Escalator belts run up or down the pieces' axes; travelators along theirs.
        double signedSpeed = connector.Kind == ConnectorKind.Escalator && !ConnectorPortals.BeltRunsUp(connector) ? -speed : speed;
        int[] pieceSurface = new int[layout.Pieces.Count];
        for (int p = 0; p < layout.Pieces.Count; p++)
        {
            ConnectorPiece piece = layout.Pieces[p];
            double length = Math.Max(piece.LengthM, 1e-3);
            double width = Math.Max(piece.WidthM, 0.1);
            int nu = Math.Max(1, (int)Math.Round(length / cs));
            int nv = Math.Max(1, (int)Math.Round(width / cs));
            int count = nu * nv;
            var walk = new byte[count];
            Array.Fill(walk, (byte)1);
            var slow = new float[count];
            Array.Fill(slow, 1f);
            var wall = new float[count];
            double cellV = width / nv;
            for (int j = 0; j < nv; j++)
            {
                double v = (j + 0.5) * cellV;
                float dist = (float)Math.Min(v, width - v);
                for (int i = 0; i < nu; i++)
                {
                    wall[(j * nu) + i] = dist;
                }
            }

            NavMotion motion = belt
                ? NavMotion.Belt
                : piece.Kind switch
                {
                    ConnectorPieceKind.Flight => NavMotion.Stair,
                    ConnectorPieceKind.RampRun => NavMotion.Ramp,
                    _ => NavMotion.Walk,
                };

            pieceSurface[p] = surfaces.Count;
            nav.Pieces.Add(surfaces.Count);
            surfaces.Add(new NavSurface
            {
                Index = surfaces.Count,
                Kind = NavSurfaceKind.Piece,
                ConnectorIndex = nav.Index,
                Name = string.Create(CultureInfo.InvariantCulture, $"{connector.Name} · {piece.Kind} {p + 1}"),
                Origin = piece.Start - (piece.Across * (width * 0.5)),
                AxisU = piece.Axis,
                AxisV = piece.Across,
                CellU = length / nu,
                CellV = cellV,
                Nx = nu,
                Ny = nv,
                Z0 = bottom.ElevationM + piece.Z0,
                SlopePerU = (piece.Z1 - piece.Z0) / length,
                HalfWidth = width * 0.5,
                Motion = motion,
                BeltSpeed = belt ? signedSpeed : 0,
                StepFreeOk = nav.StepFreeOk,
                Walkable = walk,
                Slowness = slow,
                WallDistance = wall,
            });
        }

        // Provisional global numbering: surfaces are only appended, so the running total of cells before a
        // surface is its final base.
        int BaseOf(int surface)
        {
            int total = 0;
            for (int s = 0; s < surface; s++)
            {
                total += surfaces[s].CellCount;
            }

            return total;
        }

        void Link(int fromSurface, int fromCell, int toSurface, int toCell, bool passOver)
        {
            NavSurface from = surfaces[fromSurface];
            NavSurface to = surfaces[toSurface];
            Point2 delta = to.CellCentre(toCell) - from.CellCentre(fromCell);
            float seconds = NavCosts.StepSeconds(from, delta, from.Slowness[fromCell]);
            if (float.IsFinite(seconds))
            {
                links.Add(new NavLink(BaseOf(fromSurface) + fromCell, BaseOf(toSurface) + toCell, seconds, nav.Index, passOver));
            }
        }

        // Piece ↔ piece joints.
        foreach (PieceJoint joint in ConnectorPortals.Joints(layout, bottom.ElevationM))
        {
            int sa = pieceSurface[joint.EdgeA.Piece];
            int sb = pieceSurface[joint.EdgeB.Piece];
            NavSurface a = surfaces[sa];
            NavSurface b = surfaces[sb];
            a.Segments.Add(new PortalSegment { A = joint.A, B = joint.B, Normal = joint.EdgeA.Outward, Other = sb, Allowed = true, Connector = nav.Index });
            b.Segments.Add(new PortalSegment { A = joint.A, B = joint.B, Normal = joint.EdgeB.Outward, Other = sa, Allowed = true, Connector = nav.Index });
            foreach (Point2 q in Samples(joint.A, joint.B, cs))
            {
                int ca = a.CellAt(q.X - (joint.EdgeA.Outward.X * a.CellU * 0.5), q.Y - (joint.EdgeA.Outward.Y * a.CellU * 0.5));
                int cb = b.CellAt(q.X + (joint.EdgeA.Outward.X * b.CellU * 0.5), q.Y + (joint.EdgeA.Outward.Y * b.CellU * 0.5));
                if (ca >= 0 && cb >= 0)
                {
                    Link(sa, ca, sb, cb, passOver: false);
                    Link(sb, cb, sa, ca, passOver: false);
                }
            }
        }

        // Floor ends.
        foreach (FloorEnd end in ConnectorPortals.FloorEnds(study, connector, layout))
        {
            if (!floorByLevel.TryGetValue(end.LevelId, out int sf))
            {
                continue;
            }

            int sp = pieceSurface[end.Edge.Piece];
            NavSurface floor = surfaces[sf];
            NavSurface piece = surfaces[sp];
            Point2 outward = end.Edge.Outward;
            var floorCells = new List<int>();
            var pieceCells = new List<int>();
            var pairs = new List<(int Floor, int Piece)>();
            foreach (Point2 q in Samples(end.Edge.A, end.Edge.B, cs))
            {
                int cp = piece.CellAt(q.X - (outward.X * piece.CellU * 0.5), q.Y - (outward.Y * piece.CellU * 0.5));
                int cf = floor.CellAt(q.X + (outward.X * cs * 0.9), q.Y + (outward.Y * cs * 0.9));
                if (cp < 0 || cf < 0 || floor.Walkable[cf] == 0)
                {
                    continue;
                }

                pairs.Add((cf, cp));
                if (!floorCells.Contains(cf))
                {
                    floorCells.Add(cf);
                }

                if (!pieceCells.Contains(cp))
                {
                    pieceCells.Add(cp);
                }
            }

            string levelName = study.FindLevel(end.LevelId)?.Name ?? "?";
            if (pairs.Count == 0)
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning,
                    $"Connector '{connector.Name}': its end on '{levelName}' doesn't meet walkable floor, so people can't use it there.", connector.Id));
                continue;
            }

            if (!end.IsServed)
            {
                // Pass-over (D-093): only for people already on the connector, both ways.
                if (!nav.PassOverFloors.Contains(sf))
                {
                    nav.PassOverFloors.Add(sf);
                }

                floor.Segments.Add(new PortalSegment { A = end.Edge.A, B = end.Edge.B, Normal = outward * -1, Other = sp, Allowed = true, Connector = nav.Index, IsPassOver = true });
                piece.Segments.Add(new PortalSegment { A = end.Edge.A, B = end.Edge.B, Normal = outward, Other = sf, Allowed = true, Connector = nav.Index, IsPassOver = true });
                foreach ((int cf, int cp) in pairs)
                {
                    Link(sf, cf, sp, cp, passOver: true);
                    Link(sp, cp, sf, cf, passOver: true);
                }

                continue;
            }

            bool entry = ConnectorPortals.AllowsEntry(connector, end);
            bool exit = ConnectorPortals.AllowsExit(connector, end);
            int index = portals.Count;
            portals.Add(new NavPortal
            {
                Index = index,
                Connector = nav.Index,
                Floor = sf,
                Piece = sp,
                LevelId = end.LevelId,
                ElevationM = end.Edge.ElevationM,
                AllowsEntry = entry,
                AllowsExit = exit,
                A = end.Edge.A,
                B = end.Edge.B,
                Outward = outward,
                FloorCells = [.. floorCells],
                PieceCells = [.. pieceCells],
            });
            nav.Portals.Add(index);

            floor.Segments.Add(new PortalSegment
            {
                A = end.Edge.A, B = end.Edge.B, Normal = outward * -1, Other = sp, Allowed = entry, Connector = nav.Index, Portal = index,
                IsCappedEntry = entry && throughput > 0,
            });
            piece.Segments.Add(new PortalSegment { A = end.Edge.A, B = end.Edge.B, Normal = outward, Other = sf, Allowed = exit, Connector = nav.Index, Portal = index });
            foreach ((int cf, int cp) in pairs)
            {
                if (entry)
                {
                    Link(sf, cf, sp, cp, passOver: false);
                }

                if (exit)
                {
                    Link(sp, cp, sf, cf, passOver: false);
                }
            }
        }
    }

    /// <summary>Sample points every half cell along a segment (at least one, at the middle).</summary>
    private static IEnumerable<Point2> Samples(Point2 a, Point2 b, double cs)
    {
        double length = Point2.Distance(a, b);
        int count = Math.Max(1, (int)Math.Floor(length / (cs * 0.5)));
        for (int k = 0; k < count; k++)
        {
            double t = (k + 0.5) / count;
            yield return a + ((b - a) * t);
        }
    }

    private static List<NavSink> BuildSinks(Study study, List<NavSurface> surfaces, Dictionary<Guid, int> floorByLevel, List<ValidationMessage> findings)
    {
        var sinks = new List<NavSink>();
        foreach (Region region in study.Regions)
        {
            if (!region.IsSink || region.Outline.Count < 3 || !floorByLevel.TryGetValue(region.LevelId, out int sf))
            {
                continue;
            }

            List<int> cells = CellsInside(surfaces[sf], [region.Outline], walkableOnly: true);
            if (cells.Count == 0)
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning, $"Sink '{region.Name}' has no walkable floor inside it, so no one can leave there.", region.Id));
                continue;
            }

            sinks.Add(new NavSink { Index = sinks.Count, RegionId = region.Id, Name = region.Name, Floor = sf, Cells = [.. cells] });
        }

        return sinks;
    }
}
