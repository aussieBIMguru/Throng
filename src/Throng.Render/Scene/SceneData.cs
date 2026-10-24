using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// Geometry for one level, split into passes so the renderer can order them for plan and 3D views.
/// </summary>
public sealed class LevelScene
{
    /// <summary>Level id.</summary>
    public required Guid LevelId { get; init; }

    /// <summary>Level elevation in metres.</summary>
    public required double ElevationM { get; init; }

    /// <summary>True for the level currently being edited.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Walkable floor fills (triangles, opaque colour; drawn slightly translucent in 3D).</summary>
    public required MeshData Floor { get; init; }

    /// <summary>Obstacles, walls and void balustrades (opaque triangles).</summary>
    public required MeshData Solids { get; init; }

    /// <summary>Zone fills and void openings (translucent triangles).</summary>
    public required MeshData Overlays { get; init; }

    /// <summary>Outlines of everything on the level (lines).</summary>
    public required MeshData Lines { get; init; }
}

/// <summary>
/// Everything the renderer needs for one frame's static geometry. Built on the CPU by <see cref="SceneBuilder"/>,
/// uploaded to the GPU by the renderer on the GL thread.
/// </summary>
public sealed class SceneData
{
    /// <summary>Visible levels, ordered from lowest to highest elevation.</summary>
    public required IReadOnlyList<LevelScene> Levels { get; init; }

    /// <summary>Active level id, if any.</summary>
    public Guid? ActiveLevelId { get; init; }

    /// <summary>Level directly below the active one, ghosted in plan view.</summary>
    public Guid? GhostLevelId { get; init; }

    /// <summary>Plan grid for the active level (lines).</summary>
    public required MeshData Grid { get; init; }

    /// <summary>Plan bounds of all visible geometry.</summary>
    public Bounds2 Bounds { get; init; } = Bounds2.Empty;

    /// <summary>Plan bounds of the active level's geometry.</summary>
    public Bounds2 ActiveBounds { get; init; } = Bounds2.Empty;

    /// <summary>Lowest visible elevation.</summary>
    public double MinElevationM { get; init; }

    /// <summary>Highest visible elevation plus that level's height.</summary>
    public double MaxElevationM { get; init; }

    /// <summary>An empty scene.</summary>
    public static SceneData Empty { get; } = new()
    {
        Levels = [],
        Grid = MeshData.Empty(MeshPrimitive.Lines),
    };
}
