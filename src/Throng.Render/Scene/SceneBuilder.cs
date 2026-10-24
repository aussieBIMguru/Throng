using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Render.Scene;

/// <summary>
/// Converts a <see cref="Study"/> into renderable <see cref="SceneData"/>. Pure CPU work with no GL calls,
/// so it can run anywhere and is cheap enough to rebuild after every edit at M0 scale.
/// </summary>
public static class SceneBuilder
{
    // Small vertical offsets keep coplanar passes from z-fighting.
    private const double OverlayLift = 0.02;
    private const double LineLift = 0.04;
    private const double WalkableLift = 0.05;
    private const double GridLift = 0.005;
    private const double GridMargin = 5.0;

    /// <summary>Builds the scene for all visible levels.</summary>
    /// <param name="study">Study to draw.</param>
    /// <param name="activeLevelId">Level being edited (highlighted, gridded, shown in plan).</param>
    /// <param name="activeWalkable">
    /// Optional net walkable polygons of the active level (D-039), drawn as a green boundary over the sketch.
    /// </param>
    /// <param name="hiddenRegionIds">Regions temporarily hidden in the view (D-046); they're left out of the scene.</param>
    public static SceneData Build(
        Study study,
        Guid? activeLevelId,
        IReadOnlyList<IReadOnlyList<Point2>>? activeWalkable = null,
        IReadOnlySet<Guid>? hiddenRegionIds = null)
    {
        List<Level> visible = [.. study.Levels.Where(l => l.IsVisible).OrderBy(l => l.ElevationM)];
        var levels = new List<LevelScene>(visible.Count);
        Bounds2 bounds = Bounds2.Empty;
        Bounds2 activeBounds = Bounds2.Empty;

        foreach (Level level in visible)
        {
            bool isActive = level.Id == activeLevelId;
            levels.Add(BuildLevel(study, level, isActive, isActive ? activeWalkable : null, hiddenRegionIds));
            Bounds2 levelBounds = study.GetLevelBounds(level.Id);
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

        return new SceneData
        {
            Levels = levels,
            ActiveLevelId = activeLevelId,
            GhostLevelId = ghost,
            Grid = active is null ? MeshData.Empty(MeshPrimitive.Lines) : BuildGrid(activeBounds, active.ElevationM),
            Bounds = bounds,
            ActiveBounds = activeBounds,
            MinElevationM = visible.Count > 0 ? visible.Min(l => l.ElevationM) : 0,
            MaxElevationM = visible.Count > 0 ? visible.Max(l => l.ElevationM + l.HeightM) : 0,
        };
    }

    private static LevelScene BuildLevel(Study study, Level level, bool isActive, IReadOnlyList<IReadOnlyList<Point2>>? walkable, IReadOnlySet<Guid>? hidden)
    {
        var floor = new MeshBuilder(MeshPrimitive.Triangles);
        var solids = new MeshBuilder(MeshPrimitive.Triangles);
        var overlays = new MeshBuilder(MeshPrimitive.Triangles);
        var lines = new MeshBuilder(MeshPrimitive.Lines);

        double z = level.ElevationM;
        ColorF floorColor = isActive ? RenderPalette.FloorActive : RenderPalette.FloorInactive;
        ColorF floorOutline = isActive ? RenderPalette.FloorOutlineActive : RenderPalette.FloorOutlineInactive;

        foreach (Region region in study.RegionsOn(level.Id))
        {
            if (region.Outline.Count < 2 || (hidden is not null && hidden.Contains(region.Id)))
            {
                continue;
            }

            switch (region.Kind)
            {
                case RegionKind.Walkable when region.Outline.Count >= 3:
                    floor.PolygonFill(region.Outline, z, floorColor);
                    lines.PolygonOutline(region.Outline, z + LineLift, floorOutline);
                    break;

                case RegionKind.Obstacle when region.Outline.Count >= 3:
                    if (string.Equals(region.Tag, "Void", StringComparison.OrdinalIgnoreCase))
                    {
                        // Floor opening: dark hole plus a low balustrade (sides only).
                        overlays.PolygonFill(region.Outline, z + OverlayLift, RenderPalette.VoidFill);
                        solids.PrismSides(region.Outline, z, z + region.HeightM, RenderPalette.VoidEdge.WithAlpha(1f).Shade(0.55f));
                        lines.PolygonOutline(region.Outline, z + LineLift, RenderPalette.VoidEdge);
                        lines.PolygonOutline(region.Outline, z + region.HeightM, RenderPalette.VoidEdge);
                    }
                    else
                    {
                        solids.Prism(region.Outline, z, z + region.HeightM, RenderPalette.ObstacleSide, RenderPalette.ObstacleTop);
                        lines.PolygonOutline(region.Outline, z + region.HeightM + 0.01, RenderPalette.ObstacleOutline);
                    }

                    break;

                case RegionKind.Wall:
                    AddWall(solids, lines, region, z);
                    break;

                case RegionKind.Zone when region.Outline.Count >= 3:
                    ColorF zone = RenderPalette.Zone(region.ZoneKind);
                    overlays.PolygonFill(region.Outline, z + OverlayLift, zone.WithAlpha(RenderPalette.ZoneFillAlpha));
                    lines.PolygonOutline(region.Outline, z + LineLift, zone);
                    break;
            }
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
        };
    }

    /// <summary>Walls are drawn as one thin box per polyline segment.</summary>
    private static void AddWall(MeshBuilder solids, MeshBuilder lines, Region wall, double z)
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
            solids.Prism(box, z, z + wall.HeightM, RenderPalette.WallSide, RenderPalette.WallTop);
            lines.PolygonOutline(box, z + wall.HeightM + 0.01, RenderPalette.WallTop);
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
