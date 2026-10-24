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
/// library is supplied, walls are thickened into obstacles (M1). The net walkable union itself is available
/// from <see cref="WalkableArea"/>; the engine consumes it from M2. Connectors become sloped surfaces and
/// portals in round 003 / M2.
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
            foreach (Region region in study.RegionsOn(level.Id))
            {
                switch (region.Kind)
                {
                    case RegionKind.Walkable:
                        walkable.Add(region.Outline.ToArray());
                        break;
                    case RegionKind.Obstacle:
                        obstacles.Add(region.Outline.ToArray());
                        break;
                    case RegionKind.Wall when polygonOps is not null:
                        obstacles.AddRange(polygonOps.ThickenPolyline(region.Outline, region.ThicknessM));
                        break;
                }
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

        return Result<WorldModel>.Ok(new WorldModel { Surfaces = surfaces, Portals = [] });
    }
}
