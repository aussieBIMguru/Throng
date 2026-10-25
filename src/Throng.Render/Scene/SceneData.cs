using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>Decoded underlay image pixels (32-bit BGRA, top row first), supplied by the host app.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Bgra">Pixel bytes, 4 per pixel.</param>
public sealed record UnderlayPixels(int Width, int Height, byte[] Bgra);

/// <summary>An underlay to draw in plan: its corners and image (D-085).</summary>
/// <param name="Id">Underlay id (the renderer caches its texture by id and pixel buffer).</param>
/// <param name="Corners">Plan corners bottom-left, bottom-right, top-right, top-left at the level's elevation.</param>
/// <param name="Opacity">0–1.</param>
/// <param name="Pixels">Decoded image, or null when missing (then only the outline is drawn).</param>
public sealed record UnderlayDraw(Guid Id, IReadOnlyList<Point3> Corners, float Opacity, UnderlayPixels? Pixels);

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

    /// <summary>
    /// Plan-only translucent fills: shaft openings (D-067). Not drawn in 3D, where the shaft box shows instead.
    /// </summary>
    public MeshData PlanOverlays { get; init; } = MeshData.Empty(MeshPrimitive.Triangles);

    /// <summary>
    /// Plan-only lines: connector symbols cut at mid-rise (D-062) and shaft hatches (D-067). Not drawn in 3D,
    /// where connectors are true sloped geometry.
    /// </summary>
    public MeshData PlanLines { get; init; } = MeshData.Empty(MeshPrimitive.Lines);
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

    /// <summary>3D connector pieces: sloped surfaces, stringers and undersides (opaque triangles, 3D only).</summary>
    public MeshData VerticalSolids { get; init; } = MeshData.Empty(MeshPrimitive.Triangles);

    /// <summary>3D connector edges, nosings and handrails, plus shaft box edges (lines, 3D only).</summary>
    public MeshData VerticalLines { get; init; } = MeshData.Empty(MeshPrimitive.Lines);

    /// <summary>Translucent shaft boxes spanning their levels (D-067, 3D only).</summary>
    public MeshData ShaftBoxes { get; init; } = MeshData.Empty(MeshPrimitive.Triangles);

    /// <summary>Underlays of the active level, drawn in plan only, just above the floor fill (D-085).</summary>
    public IReadOnlyList<UnderlayDraw> Underlays { get; init; } = [];

    /// <summary>Text labels for the active level in plan (connector UP / DN), drawn by the WPF overlay.</summary>
    public IReadOnlyList<PlanLabel> PlanLabels { get; init; } = [];

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
