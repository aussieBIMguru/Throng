using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.Render.Scene;

/// <summary>
/// Converts a <see cref="Study"/> into renderable <see cref="SceneData"/>. Pure CPU work with no GL calls,
/// so it can run anywhere and is cheap enough to rebuild after every edit at M1 scale.
/// </summary>
public static class SceneBuilder
{
    // Small vertical offsets keep coplanar passes from z-fighting.
    private const double OverlayLift = 0.02;
    private const double LineLift = 0.04;
    private const double WalkableLift = 0.05;
    private const double SymbolLift = 0.06;
    private const double GridLift = 0.005;
    private const double GridMargin = 5.0;

    /// <summary>Builds the scene for all visible levels.</summary>
    /// <param name="study">Study to draw.</param>
    /// <param name="activeLevelId">Level being edited (highlighted, gridded, shown in plan).</param>
    /// <param name="activeWalkable">
    /// Optional net walkable polygons of the active level (D-039), drawn as a green boundary over the sketch.
    /// </param>
    /// <param name="hiddenIds">Regions, connectors and shafts temporarily hidden in the view (D-046); left out of the scene.</param>
    /// <param name="ops">
    /// Polygon library. When given, floors with openings or shafts are filled with the holes cut out
    /// (D-055, D-056); without it floors are filled from the raw sketch.
    /// </param>
    /// <param name="underlayPixels">Decodes an underlay's image (the host app owns image decoding); null skips images.</param>
    public static SceneData Build(
        Study study,
        Guid? activeLevelId,
        IReadOnlyList<IReadOnlyList<Point2>>? activeWalkable = null,
        IReadOnlySet<Guid>? hiddenIds = null,
        IPolygonOps? ops = null,
        Func<Underlay, UnderlayPixels?>? underlayPixels = null)
    {
        List<Level> visible = [.. study.Levels.Where(l => l.IsVisible).OrderBy(l => l.ElevationM)];
        var visibleIds = visible.Select(l => l.Id).ToHashSet();
        var levels = new List<LevelScene>(visible.Count);
        var labels = new List<PlanLabel>();
        Bounds2 bounds = Bounds2.Empty;
        Bounds2 activeBounds = Bounds2.Empty;

        // Layouts once per connector; they feed plan symbols on both levels and the 3D pieces.
        var layouts = new List<(Connector Connector, ConnectorLayout Layout, double BaseZ)>();
        foreach (Connector connector in study.Connectors)
        {
            if (hiddenIds is not null && hiddenIds.Contains(connector.Id))
            {
                continue;
            }

            Level? bottom = study.FindLevel(connector.BottomLevelId);
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, study);
            if (bottom is not null && layout.IsSuccess && layout.Value is { } l)
            {
                layouts.Add((connector, l, bottom.ElevationM));
            }
        }

        foreach (Level level in visible)
        {
            bool isActive = level.Id == activeLevelId;
            levels.Add(BuildLevel(study, level, isActive, isActive ? activeWalkable : null, hiddenIds, ops, layouts, isActive ? labels : null));
            Bounds2 levelBounds = LevelBounds(study, level, layouts);
            bounds = bounds.Union(levelBounds);
            if (isActive)
            {
                activeBounds = levelBounds;
            }
        }

        // Ghost = the nearest visible level below the active one.
        Guid? ghost = null;
        Level? active = activeLevelId is { } id ? study.FindLevel(id) : null;
        if (active is not null)
        {
            ghost = visible.Where(l => l.ElevationM < active.ElevationM).OrderByDescending(l => l.ElevationM).Select(l => (Guid?)l.Id).FirstOrDefault();
        }

        (MeshData verticalSolids, MeshData verticalLines, MeshData shaftBoxes) = BuildVertical(study, visibleIds, layouts, hiddenIds);

        // Underlays: active level only, plan only (D-085).
        var underlays = new List<UnderlayDraw>();
        if (active is not null && active.IsVisible)
        {
            foreach (Underlay underlay in study.UnderlaysOn(active.Id))
            {
                if (underlay.IsVisible && underlay.PixelWidth > 0 && underlay.PixelHeight > 0 && (hiddenIds is null || !hiddenIds.Contains(underlay.Id)))
                {
                    Point3[] corners = [.. UnderlayGeometry.Corners(underlay).Select(p => Point3.FromPlan(p, active.ElevationM + GridLift))];
                    underlays.Add(new UnderlayDraw(underlay.Id, corners, (float)Math.Clamp(underlay.Opacity, 0, 1), underlayPixels?.Invoke(underlay)));
                }
            }
        }

        return new SceneData
        {
            Levels = levels,
            ActiveLevelId = activeLevelId,
            GhostLevelId = ghost,
            Grid = active is null ? MeshData.Empty(MeshPrimitive.Lines) : BuildGrid(activeBounds, active.ElevationM),
            VerticalSolids = verticalSolids,
            VerticalLines = verticalLines,
            ShaftBoxes = shaftBoxes,
            Underlays = underlays,
            PlanLabels = labels,
            Bounds = bounds,
            ActiveBounds = activeBounds,
            MinElevationM = visible.Count > 0 ? visible.Min(l => l.ElevationM) : 0,
            MaxElevationM = visible.Count > 0 ? visible.Max(l => l.ElevationM + l.HeightM) : 0,
        };
    }

    private static LevelScene BuildLevel(
        Study study,
        Level level,
        bool isActive,
        IReadOnlyList<IReadOnlyList<Point2>>? walkable,
        IReadOnlySet<Guid>? hidden,
        IPolygonOps? ops,
        List<(Connector Connector, ConnectorLayout Layout, double BaseZ)> layouts,
        List<PlanLabel>? labels)
    {
        var floor = new MeshBuilder(MeshPrimitive.Triangles);
        var solids = new MeshBuilder(MeshPrimitive.Triangles);
        var overlays = new MeshBuilder(MeshPrimitive.Triangles);
        var lines = new MeshBuilder(MeshPrimitive.Lines);
        var planOverlays = new MeshBuilder(MeshPrimitive.Triangles);
        var planLines = new MeshBuilder(MeshPrimitive.Lines);

        double z = level.ElevationM;
        ColorF floorColor = isActive ? RenderPalette.FloorActive : RenderPalette.FloorInactive;
        ColorF floorOutline = isActive ? RenderPalette.FloorOutlineActive : RenderPalette.FloorOutlineInactive;

        IReadOnlyList<VerticalCut> cuts = ops is null ? [] : VerticalCuts.ForLevel(study, level.Id, ops);
        var floorPolygons = new List<IReadOnlyList<Point2>>();

        // Regions drawn here, plus obstacles and walls spanning onto this level (D-076, D-079, D-087).
        foreach (Region region in study.RegionsPresentOn(level.Id))
        {
            if (region.Outline.Count < 2 || (hidden is not null && hidden.Contains(region.Id)))
            {
                continue;
            }

            double height = study.RegionExtentOn(region, level).Top - z;
            switch (region.Kind)
            {
                case RegionKind.Walkable when region.Outline.Count >= 3:
                    floorPolygons.Add(region.Outline);
                    lines.PolygonOutline(region.Outline, z + LineLift, floorOutline);
                    break;

                case RegionKind.Obstacle when region.Outline.Count >= 3:
                    if (string.Equals(region.Tag, "Void", StringComparison.OrdinalIgnoreCase))
                    {
                        // Hand-drawn floor opening: dark hole plus a low balustrade (sides only).
                        overlays.PolygonFill(region.Outline, z + OverlayLift, RenderPalette.VoidFill);
                        solids.PrismSides(region.Outline, z, z + height, RenderPalette.VoidEdge.WithAlpha(1f).Shade(0.55f));
                        lines.PolygonOutline(region.Outline, z + LineLift, RenderPalette.VoidEdge);
                        lines.PolygonOutline(region.Outline, z + height, RenderPalette.VoidEdge);
                    }
                    else
                    {
                        solids.Prism(region.Outline, z, z + height, RenderPalette.ObstacleSide, RenderPalette.ObstacleTop);
                        lines.PolygonOutline(region.Outline, z + height + 0.01, RenderPalette.ObstacleOutline);
                    }

                    break;

                case RegionKind.Wall:
                    AddWall(solids, lines, region, z, height);
                    break;

                case RegionKind.Zone when region.Outline.Count >= 3:
                    ColorF zone = RenderPalette.Zone(region.ZoneKind);
                    overlays.PolygonFill(region.Outline, z + OverlayLift, zone.WithAlpha(RenderPalette.ZoneFillAlpha));
                    lines.PolygonOutline(region.Outline, z + LineLift, zone);
                    break;
            }
        }

        FillFloor(floor, floorPolygons, cuts, ops, z, floorColor);

        // Linked openings: a balustrade-coloured outline (plan and 3D).
        foreach (VerticalCut cut in cuts)
        {
            if (cut.Kind == VerticalCutKind.Opening && (hidden is null || !hidden.Contains(cut.SourceId)))
            {
                foreach (IReadOnlyList<Point2> ring in cut.Polygons)
                {
                    lines.PolygonOutline(ring, z + LineLift, RenderPalette.OpeningEdge);
                }
            }
        }

        // Connector plan symbols, cut at mid-rise (D-062); per-level stairs draw each circuit on its levels (D-091).
        foreach ((Connector connector, ConnectorLayout layout, double baseZ) in layouts)
        {
            if (layout.Circuits.Count > 1)
            {
                foreach (ConnectorCircuit circuit in layout.Circuits)
                {
                    if (circuit.BottomLevelId == level.Id)
                    {
                        ConnectorSymbols.AddPlan(planLines, labels, connector, circuit, ConnectorPlanPart.Lower, z + SymbolLift);
                    }

                    if (circuit.TopLevelId == level.Id)
                    {
                        ConnectorSymbols.AddPlan(planLines, labels, connector, circuit, ConnectorPlanPart.Upper, z + SymbolLift);
                        if (labels is not null && connector.ExcludedLevelIds.Contains(level.Id) && circuit.Index < layout.Circuits.Count - 1)
                        {
                            labels.Add(new PlanLabel(circuit.TopLanding + (circuit.TopAxis * 0.6), "NO EXIT", RenderPalette.Connector));
                        }
                    }
                }

                continue;
            }

            ConnectorPlanPart? part = PartOn(study, connector, level, baseZ, layout);
            if (part is { } p)
            {
                ConnectorSymbols.AddPlan(planLines, labels, connector, layout, p, z + SymbolLift);
            }
        }

        // Underlay extents (plan only), so a missing or faint image still shows where it sits.
        if (isActive)
        {
            foreach (Underlay underlay in study.UnderlaysOn(level.Id))
            {
                if (underlay.IsVisible && underlay.PixelWidth > 0 && (hidden is null || !hidden.Contains(underlay.Id)))
                {
                    planLines.PolygonOutline(UnderlayGeometry.Corners(underlay), z + GridLift, RenderPalette.UnderlayEdge);
                }
            }
        }

        // Devices (D-089): footprint and lanes in plan and 3D, the service-side arrow in plan.
        foreach (Device device in study.DevicesOn(level.Id))
        {
            if (hidden is null || !hidden.Contains(device.Id))
            {
                AddDevice(solids, lines, planLines, device, z);
            }
        }

        // Shafts: dark opening with a diagonal cross on every spanned level (D-067).
        foreach (Shaft shaft in study.ShaftsSpanning(level.Id))
        {
            if (shaft.Outline.Count < 3 || (hidden is not null && hidden.Contains(shaft.Id)))
            {
                continue;
            }

            planOverlays.PolygonFill(shaft.Outline, z + OverlayLift, RenderPalette.ShaftPlanFill);
            planLines.PolygonOutline(shaft.Outline, z + SymbolLift, RenderPalette.Shaft);
            if (labels is not null && shaft.NoStopLevelIds.Contains(level.Id))
            {
                labels.Add(new PlanLabel(PolygonMath.Centroid(shaft.Outline), "NO STOP", RenderPalette.Shaft));
            }
            Bounds2 b = Bounds2.FromPoints(shaft.Outline);
            AddClipped(planLines, shaft.Outline, new Point2(b.MinX, b.MinY), new Point2(b.MaxX, b.MaxY), z + SymbolLift, RenderPalette.Shaft);
            AddClipped(planLines, shaft.Outline, new Point2(b.MinX, b.MaxY), new Point2(b.MaxX, b.MinY), z + SymbolLift, RenderPalette.Shaft);
        }

        // Net walkable boundary last, so it reads over the individual sketch outlines.
        if (walkable is not null)
        {
            foreach (IReadOnlyList<Point2> ring in walkable)
            {
                lines.PolygonOutline(ring, z + WalkableLift, RenderPalette.WalkableNet);
            }
        }

        return new LevelScene
        {
            LevelId = level.Id,
            ElevationM = level.ElevationM,
            IsActive = isActive,
            Floor = floor.Build(),
            Solids = solids.Build(),
            Overlays = overlays.Build(),
            Lines = lines.Build(),
            PlanOverlays = planOverlays.Build(),
            PlanLines = planLines.Build(),
        };
    }

    /// <summary>Which part of a connector a level shows, or null when the connector doesn't reach it.</summary>
    private static ConnectorPlanPart? PartOn(Study study, Connector connector, Level level, double baseZ, ConnectorLayout layout)
    {
        if (connector.BottomLevelId == level.Id)
        {
            return ConnectorPlanPart.Lower;
        }

        if (connector.TopLevelId == level.Id)
        {
            return ConnectorPlanPart.Upper;
        }

        return level.ElevationM > baseZ && level.ElevationM < baseZ + layout.RiseM && study.FindLevel(connector.TopLevelId) is not null
            ? ConnectorPlanPart.Through
            : null;
    }

    /// <summary>
    /// Fills a level's floor. With openings or shafts, the walkable union minus them is filled as hole-free horizontal strips
    /// (strip edges at every vertex Y, so no strip can contain a whole hole); otherwise each region is filled as drawn.
    /// </summary>
    private static void FillFloor(MeshBuilder floor, List<IReadOnlyList<Point2>> polygons, IReadOnlyList<VerticalCut> cuts, IPolygonOps? ops, double z, ColorF color)
    {
        // Footprints stand on the floor, so only openings and shafts are holes in it.
        var holes = new List<IReadOnlyList<Point2>>();
        foreach (VerticalCut cut in cuts)
        {
            if (cut.Kind != VerticalCutKind.Footprint)
            {
                holes.AddRange(cut.Polygons);
            }
        }

        if (ops is null || holes.Count == 0 || polygons.Count == 0)
        {
            foreach (IReadOnlyList<Point2> polygon in polygons)
            {
                floor.PolygonFill(polygon, z, color);
            }

            return;
        }

        Bounds2 bounds = Bounds2.Empty;
        var ys = new SortedSet<double>();
        foreach (IReadOnlyList<Point2> ring in polygons.Concat(holes))
        {
            foreach (Point2 p in ring)
            {
                bounds = bounds.Include(p);
                ys.Add(Math.Round(p.Y, 5));
            }
        }

        double? previous = null;
        foreach (double y in ys)
        {
            if (previous is double y0 && y - y0 > 1e-5)
            {
                Point2[] strip = [new(bounds.MinX - 1, y0), new(bounds.MaxX + 1, y0), new(bounds.MaxX + 1, y), new(bounds.MinX - 1, y)];
                IReadOnlyList<IReadOnlyList<Point2>> band = ops.Intersection(polygons, [strip]);
                foreach (IReadOnlyList<Point2> piece in ops.Difference(band, holes))
                {
                    floor.PolygonFill(piece, z, color);
                }
            }

            previous = y;
        }
    }

    /// <summary>Adds the parts of segment a–b that lie inside a polygon (for shaft hatches).</summary>
    private static void AddClipped(MeshBuilder lines, IReadOnlyList<Point2> polygon, Point2 a, Point2 b, double z, ColorF color)
    {
        var ts = new List<double> { 0, 1 };
        Point2 d = b - a;
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            Point2 p = polygon[i];
            Point2 e = polygon[(i + 1) % n] - p;
            double denominator = Point2.Cross(d, e);
            if (Math.Abs(denominator) < 1e-12)
            {
                continue;
            }

            double t = Point2.Cross(p - a, e) / denominator;
            double u = Point2.Cross(p - a, d) / denominator;
            if (t > 0 && t < 1 && u >= 0 && u <= 1)
            {
                ts.Add(t);
            }
        }

        ts.Sort();
        for (int i = 0; i + 1 < ts.Count; i++)
        {
            Point2 s = a + (d * ts[i]);
            Point2 f = a + (d * ts[i + 1]);
            if (Point2.Distance(s, f) > 1e-6 && PolygonMath.Contains(polygon, (s + f) * 0.5))
            {
                lines.Line(s, f, z, color);
            }
        }
    }

    /// <summary>3D connectors (when either end level is visible) and translucent shaft boxes (D-067).</summary>
    private static (MeshData Solids, MeshData Lines, MeshData Boxes) BuildVertical(
        Study study, HashSet<Guid> visibleIds, List<(Connector Connector, ConnectorLayout Layout, double BaseZ)> layouts, IReadOnlySet<Guid>? hidden)
    {
        var solids = new MeshBuilder(MeshPrimitive.Triangles);
        var lines = new MeshBuilder(MeshPrimitive.Lines);
        var boxes = new MeshBuilder(MeshPrimitive.Triangles);

        foreach ((Connector connector, ConnectorLayout layout, double baseZ) in layouts)
        {
            if (visibleIds.Contains(connector.BottomLevelId) || visibleIds.Contains(connector.TopLevelId))
            {
                ConnectorSymbols.Add3D(solids, lines, connector, layout, baseZ);
            }
        }

        foreach (Shaft shaft in study.Shafts)
        {
            Level? bottom = study.FindLevel(shaft.BaseLevelId);
            Level? top = study.FindLevel(shaft.TopLevelId);
            if (bottom is null || top is null || shaft.Outline.Count < 3 || (hidden is not null && hidden.Contains(shaft.Id))
                || (!visibleIds.Contains(bottom.Id) && !visibleIds.Contains(top.Id)))
            {
                continue;
            }

            double z0 = bottom.ElevationM;
            double z1 = Math.Max(z0 + 0.1, top.ElevationM + top.HeightM);
            ColorF face = RenderPalette.Shaft.WithAlpha(RenderPalette.ShaftBoxAlpha);
            boxes.PrismSides(shaft.Outline, z0, z1, face);
            boxes.PolygonFill(shaft.Outline, z1, face);

            ColorF edge = RenderPalette.Shaft.WithAlpha(0.6f);
            lines.PolygonOutline(shaft.Outline, z0 + LineLift, edge);
            lines.PolygonOutline(shaft.Outline, z1, edge);
            foreach (Point2 p in shaft.Outline)
            {
                lines.Line(Point3.FromPlan(p, z0), Point3.FromPlan(p, z1), edge.WithAlpha(0.35f));
            }
        }

        return (solids.Build(), lines.Build(), boxes.Build());
    }

    /// <summary>Plan bounds of everything drawn on a level: regions, connector footprints and shafts.</summary>
    private static Bounds2 LevelBounds(Study study, Level level, List<(Connector Connector, ConnectorLayout Layout, double BaseZ)> layouts)
    {
        Bounds2 bounds = study.GetLevelBounds(level.Id);
        foreach ((Connector connector, ConnectorLayout layout, _) in layouts)
        {
            if (connector.BottomLevelId == level.Id || connector.TopLevelId == level.Id)
            {
                bounds = bounds.Union(Bounds2.FromPoints(layout.Footprint));
            }
        }

        foreach (Shaft shaft in study.ShaftsSpanning(level.Id))
        {
            bounds = bounds.Union(Bounds2.FromPoints(shaft.Outline));
        }

        foreach (Region region in study.RegionsPresentOn(level.Id).Where(r => r.LevelId != level.Id))
        {
            bounds = bounds.Union(Bounds2.FromPoints(region.Outline));
        }

        foreach (Device device in study.DevicesOn(level.Id))
        {
            bounds = bounds.Union(Bounds2.FromPoints(DeviceGeometry.Footprint(device)));
        }

        foreach (Underlay underlay in study.UnderlaysOn(level.Id))
        {
            if (underlay.IsVisible && underlay.PixelWidth > 0)
            {
                bounds = bounds.Union(Bounds2.FromPoints(UnderlayGeometry.Corners(underlay)));
            }
        }

        return bounds;
    }

    /// <summary>Device footprint (or pass-through lane dividers) as solids, its outline and lanes, and a service arrow.</summary>
    private static void AddDevice(MeshBuilder solids, MeshBuilder lines, MeshBuilder planLines, Device device, double z)
    {
        ColorF color = ColorF.FromHex(device.ColorHex);
        Point2[] footprint = DeviceGeometry.Footprint(device);
        double top = z + Math.Max(0.1, device.HeightM);
        if (device.PassThrough)
        {
            foreach (IReadOnlyList<Point2> divider in DeviceGeometry.Blocking(device))
            {
                solids.Prism(divider, z, top, color.Shade(0.5f), color.Shade(0.8f));
            }
        }
        else
        {
            solids.Prism(footprint, z, top, color.Shade(0.45f), color.Shade(0.75f));
        }

        lines.PolygonOutline(footprint, top + 0.01, color);
        foreach ((Point2 a, Point2 b) in DeviceGeometry.LaneLines(device))
        {
            lines.Line(a, b, top + 0.01, color.WithAlpha(0.7f));
        }

        // Service side: an arrow out of the front edge (through the device for pass-through ones).
        Point2 facing = device.Facing();
        Point2 front = DeviceGeometry.ServicePoint(device);
        double size = Math.Min(0.5, Math.Max(0.2, device.WidthM * 0.25));
        Point2 tip = front + (facing * size);
        Point2 side = facing.Perpendicular() * (size * 0.5);
        double pz = z + SymbolLift + 0.02;
        planLines.Line(front - side, tip, pz, color);
        planLines.Line(front + side, tip, pz, color);
        if (device.PassThrough)
        {
            Point2 back = device.Position - (facing * (device.DepthM * 0.5));
            planLines.Line(back - (facing * size), back, pz, color.WithAlpha(0.6f));
        }
    }

    /// <summary>Walls are drawn as one thin box per polyline segment, <paramref name="height"/> tall.</summary>
    private static void AddWall(MeshBuilder solids, MeshBuilder lines, Region wall, double z, double height)
    {
        double half = Math.Max(0.02, wall.ThicknessM * 0.5);
        for (int i = 0; i + 1 < wall.Outline.Count; i++)
        {
            Point2 a = wall.Outline[i];
            Point2 b = wall.Outline[i + 1];
            Point2 n = (b - a).Perpendicular().Normalised() * half;
            if (n == Point2.Zero)
            {
                continue;
            }

            Point2[] box = [a + n, b + n, b - n, a - n];
            solids.Prism(box, z, z + height, RenderPalette.WallSide, RenderPalette.WallTop);
            lines.PolygonOutline(box, z + height + 0.01, RenderPalette.WallTop);
        }
    }

    /// <summary>1 m minor / 5 m major grid covering the active level plus a margin.</summary>
    private static MeshData BuildGrid(Bounds2 levelBounds, double z)
    {
        var grid = new MeshBuilder(MeshPrimitive.Lines);
        Bounds2 b = levelBounds.IsEmpty ? new Bounds2(-20, -20, 20, 20) : levelBounds.Inflate(GridMargin);
        int x0 = (int)Math.Floor(b.MinX), x1 = (int)Math.Ceiling(b.MaxX);
        int y0 = (int)Math.Floor(b.MinY), y1 = (int)Math.Ceiling(b.MaxY);
        double gz = z + GridLift;

        for (int x = x0; x <= x1; x++)
        {
            ColorF c = x % 5 == 0 ? RenderPalette.GridMajor : RenderPalette.GridMinor;
            grid.Line(new Point2(x, y0), new Point2(x, y1), gz, c);
        }

        for (int y = y0; y <= y1; y++)
        {
            ColorF c = y % 5 == 0 ? RenderPalette.GridMajor : RenderPalette.GridMinor;
            grid.Line(new Point2(x0, y), new Point2(x1, y), gz, c);
        }

        return grid.Build();
    }
}
