using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using Throng.Core.Common;
using Throng.Render.Scene;

namespace Throng.Render.Gl;

/// <summary>How the scene is presented.</summary>
public enum ViewMode
{
    /// <summary>Orthographic top-down view of the active level, with the level below ghosted.</summary>
    Plan,

    /// <summary>Perspective orbit view of all visible levels stacked at true elevation.</summary>
    Orbit,
}

/// <summary>
/// Draws <see cref="SceneData"/> with OpenGL 3.3. All methods must be called on the thread that owns the GL
/// context (the WPF UI thread when hosted in GLWpfControl). Scenes handed over with <see cref="SetScene"/> are
/// uploaded lazily on the next <see cref="Render"/>.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private const float GhostAlpha = 0.35f;
    private const float FloorAlpha3D = 0.92f;

    /// <summary>Scene line weight in DIPs (D-049).</summary>
    private const float LineWidthDip = 2f;

    /// <summary>Grid line weight in DIPs; the grid stays fine so it doesn't compete with geometry.</summary>
    private const float GridWidthDip = 1f;

    private readonly ShaderProgram _flat = new(Shaders.FlatVertex, null, Shaders.FlatFragment);
    private readonly ShaderProgram _lines = new(Shaders.FlatVertex, Shaders.LineGeometry, Shaders.LineFragment);
    private float _pixelsPerDip = 1f;
    private readonly List<GpuLevel> _levels = [];
    private GpuMesh? _grid;
    private SceneData? _pendingScene;
    private SceneData _scene = SceneData.Empty;

    /// <summary>True after a successful <see cref="Initialise"/>.</summary>
    public bool IsInitialised { get; private set; }

    /// <summary>Compiles shaders. Call once with the GL context current.</summary>
    public Result Initialise()
    {
        if (IsInitialised)
        {
            return Result.Ok();
        }

        Result flat = _flat.Initialise();
        if (!flat.IsSuccess)
        {
            return flat;
        }

        Result lines = _lines.Initialise();
        if (!lines.IsSuccess)
        {
            _flat.Dispose();
            return lines;
        }

        IsInitialised = true;
        return Result.Ok();
    }

    /// <summary>Queues a new scene; it replaces the current GPU data at the next render.</summary>
    public void SetScene(SceneData scene) => _pendingScene = scene;

    /// <summary>Draws one frame into the currently bound framebuffer.</summary>
    /// <param name="pixelWidth">Framebuffer width in pixels.</param>
    /// <param name="pixelHeight">Framebuffer height in pixels.</param>
    /// <param name="viewProjection">Camera matrix.</param>
    /// <param name="mode">Plan or orbit presentation.</param>
    /// <param name="pixelsPerDip">Display scale, so line widths are the same physical size at any DPI.</param>
    public void Render(int pixelWidth, int pixelHeight, Matrix4x4 viewProjection, ViewMode mode, float pixelsPerDip = 1f)
    {
        if (!IsInitialised)
        {
            return;
        }

        if (_pendingScene is not null)
        {
            Upload(_pendingScene);
            _pendingScene = null;
        }

        ColorF bg = RenderPalette.Background;
        GL.Viewport(0, 0, Math.Max(1, pixelWidth), Math.Max(1, pixelHeight));
        GL.ClearColor(bg.R, bg.G, bg.B, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Multisample);

        _pixelsPerDip = pixelsPerDip > 0 ? pixelsPerDip : 1f;
        _lines.Use();
        _lines.SetViewProjection(viewProjection);
        _lines.SetViewport(pixelWidth, pixelHeight);
        _flat.Use();
        _flat.SetViewProjection(viewProjection);

        if (mode == ViewMode.Plan)
        {
            RenderPlan();
        }
        else
        {
            RenderOrbit();
        }

        GL.BindVertexArray(0);
        GL.DepthMask(true);
    }

    /// <summary>
    /// Plan: painter's order on the active level only, no depth test. The level below is ghosted over the
    /// active floor so its outlines read through.
    /// </summary>
    private void RenderPlan()
    {
        GL.Disable(EnableCap.DepthTest);

        GpuLevel? active = _levels.FirstOrDefault(l => l.LevelId == _scene.ActiveLevelId);
        GpuLevel? ghost = _levels.FirstOrDefault(l => l.LevelId == _scene.GhostLevelId);
        if (active is null)
        {
            return;
        }

        DrawTriangles(active.Floor, 1f);
        DrawLines(_grid, 1f, GridWidthDip);

        if (ghost is not null)
        {
            DrawLines(ghost.Lines, GhostAlpha, LineWidthDip);
        }

        DrawTriangles(active.Overlays, 1f);
        DrawTriangles(active.Solids, 1f);
        DrawLines(active.Lines, 1f, LineWidthDip);
    }

    /// <summary>
    /// Orbit: solids opaque with depth writes, then floors and overlays translucent bottom-up without depth
    /// writes (so stacked levels read through slightly), then outlines.
    /// </summary>
    private void RenderOrbit()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);

        GL.DepthMask(true);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Solids, 1f);
        }

        GL.DepthMask(false);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Floor, FloorAlpha3D);
        }

        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Overlays, 1f);
        }

        foreach (GpuLevel level in _levels)
        {
            DrawLines(level.Lines, 1f, LineWidthDip);
        }

        GL.DepthMask(true);
    }

    /// <summary>Draws a triangle mesh with the flat program.</summary>
    private void DrawTriangles(GpuMesh? mesh, float alpha)
    {
        if (mesh is null)
        {
            return;
        }

        _flat.Use();
        _flat.SetAlpha(alpha);
        mesh.Draw();
    }

    /// <summary>Draws a line mesh as screen-space quads of the given width (D-049).</summary>
    private void DrawLines(GpuMesh? mesh, float alpha, float widthDip)
    {
        if (mesh is null)
        {
            return;
        }

        _lines.Use();
        _lines.SetAlpha(alpha);
        _lines.SetLineWidth(widthDip * _pixelsPerDip);
        mesh.Draw();
    }

    private void Upload(SceneData scene)
    {
        ReleaseScene();
        foreach (LevelScene level in scene.Levels)
        {
            _levels.Add(new GpuLevel(
                level.LevelId,
                GpuMesh.Create(level.Floor),
                GpuMesh.Create(level.Solids),
                GpuMesh.Create(level.Overlays),
                GpuMesh.Create(level.Lines)));
        }

        _grid = GpuMesh.Create(scene.Grid);
        _scene = scene;
    }

    private void ReleaseScene()
    {
        foreach (GpuLevel level in _levels)
        {
            level.Dispose();
        }

        _levels.Clear();
        _grid?.Dispose();
        _grid = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsInitialised)
        {
            ReleaseScene();
            _flat.Dispose();
            _lines.Dispose();
            IsInitialised = false;
        }
    }

    /// <summary>GPU meshes for one level.</summary>
    private sealed record GpuLevel(Guid LevelId, GpuMesh? Floor, GpuMesh? Solids, GpuMesh? Overlays, GpuMesh? Lines) : IDisposable
    {
        public void Dispose()
        {
            Floor?.Dispose();
            Solids?.Dispose();
            Overlays?.Dispose();
            Lines?.Dispose();
        }
    }
}
