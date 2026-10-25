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
    private readonly ShaderProgram _textured = new(Shaders.TexturedVertex, null, Shaders.TexturedFragment);
    private readonly ShaderProgram _agentProgram = new(Shaders.AgentVertex, null, Shaders.AgentFragment);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private GpuAgents? _agents;
    private float[]? _pendingAgents;
    private int _pendingAgentCount;
    private bool _agentsDirty;
    private readonly Dictionary<Guid, GpuUnderlay> _underlayCache = new();
    private readonly List<GpuUnderlay> _underlays = [];
    private float _pixelsPerDip = 1f;
    private readonly List<GpuLevel> _levels = [];
    private GpuMesh? _grid;
    private GpuMesh? _verticalSolids;
    private GpuMesh? _verticalLines;
    private GpuMesh? _shaftBoxes;
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

        Result textured = _textured.Initialise();
        if (!textured.IsSuccess)
        {
            _flat.Dispose();
            _lines.Dispose();
            return textured;
        }

        Result agents = _agentProgram.Initialise();
        if (!agents.IsSuccess)
        {
            _flat.Dispose();
            _lines.Dispose();
            _textured.Dispose();
            return agents;
        }

        _agents = GpuAgents.Create();
        IsInitialised = true;
        return Result.Ok();
    }

    /// <summary>Queues a new scene; it replaces the current GPU data at the next render.</summary>
    public void SetScene(SceneData scene) => _pendingScene = scene;

    /// <summary>
    /// Agents to draw from the next render (D-102): <paramref name="count"/> instances of
    /// <see cref="GpuAgents.FloatsPerInstance"/> floats (the simulation snapshot layout). The caller keeps
    /// <paramref name="data"/> unchanged until the next render; pass a count of 0 to clear.
    /// </summary>
    public void SetAgents(float[] data, int count)
    {
        _pendingAgents = data;
        _pendingAgentCount = count;
        _agentsDirty = true;
    }

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
        _textured.Use();
        _textured.SetViewProjection(viewProjection);
        _flat.Use();
        _flat.SetViewProjection(viewProjection);
        _agentProgram.Use();
        _agentProgram.SetViewProjection(viewProjection);
        _agentProgram.SetTime((float)_clock.Elapsed.TotalSeconds);
        if (_agentsDirty && _agents is not null)
        {
            _agents.Update(_pendingAgents ?? [], _pendingAgents is null ? 0 : _pendingAgentCount);
            _agentsDirty = false;
        }

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

        // Underlays sit just above the floor fill and under everything else (D-085).
        if (_underlays.Count > 0)
        {
            _textured.Use();
            foreach (GpuUnderlay underlay in _underlays)
            {
                _textured.SetAlpha(underlay.Opacity);
                underlay.Draw();
            }
        }

        DrawLines(_grid, 1f, GridWidthDip);

        if (ghost is not null)
        {
            DrawLines(ghost.Lines, GhostAlpha, LineWidthDip);
            DrawLines(ghost.PlanLines, GhostAlpha, LineWidthDip);
        }

        DrawTriangles(active.Overlays, 1f);
        DrawTriangles(active.PlanOverlays, 1f);
        DrawTriangles(active.Solids, 1f);
        DrawLines(active.Lines, 1f, LineWidthDip);

        // Connector symbols and shaft hatches read over everything else on the level (D-062, D-067).
        DrawLines(active.PlanLines, 1f, LineWidthDip);

        // Agents last, over everything (the app passes only the active level's band, D-102).
        DrawAgents();
    }

    /// <summary>
    /// Orbit: solids opaque with depth writes, then floors and overlays translucent bottom-up without depth
    /// writes (so stacked levels read through slightly), then outlines, then agents behind a depth-only floor pass.
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

        // Sloped connector pieces are solid geometry (D-054).
        DrawTriangles(_verticalSolids, 1f);

        GL.DepthMask(false);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Floor, FloorAlpha3D);
        }

        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Overlays, 1f);
        }

        // Faint shaft boxes after the floors, still without depth writes, so everything reads through them (D-067).
        DrawTriangles(_shaftBoxes, 1f);

        foreach (GpuLevel level in _levels)
        {
            DrawLines(level.Lines, 1f, LineWidthDip);
        }

        DrawLines(_verticalLines, 1f, LineWidthDip);

        // Agents last (D-102): floors first write depth only (no colour), so people on other levels don't show
        // through a floor (D-117), while outlines and solids below still read through as before.
        GL.DepthMask(true);
        GL.ColorMask(false, false, false, false);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Floor, 1f);
        }

        GL.ColorMask(true, true, true, true);
        DrawAgents();
    }

    /// <summary>Draws the agent instances with the agent program.</summary>
    private void DrawAgents()
    {
        if (_agents is null)
        {
            return;
        }

        _agentProgram.Use();
        _agents.Draw();
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
                GpuMesh.Create(level.Lines),
                GpuMesh.Create(level.PlanOverlays),
                GpuMesh.Create(level.PlanLines)));
        }

        _grid = GpuMesh.Create(scene.Grid);
        UploadUnderlays(scene.Underlays);
        _verticalSolids = GpuMesh.Create(scene.VerticalSolids);
        _verticalLines = GpuMesh.Create(scene.VerticalLines);
        _shaftBoxes = GpuMesh.Create(scene.ShaftBoxes);
        _scene = scene;
    }

    /// <summary>Reuses textures whose pixel buffer is unchanged; creates new ones and frees the rest.</summary>
    private void UploadUnderlays(IReadOnlyList<UnderlayDraw> draws)
    {
        _underlays.Clear();
        var keep = new HashSet<Guid>();
        foreach (UnderlayDraw draw in draws)
        {
            if (draw.Pixels is not { } pixels || pixels.Width <= 0 || pixels.Height <= 0 || pixels.Bgra.Length < pixels.Width * pixels.Height * 4)
            {
                continue;
            }

            if (!_underlayCache.TryGetValue(draw.Id, out GpuUnderlay? gpu) || !ReferenceEquals(gpu.Pixels, pixels))
            {
                gpu?.Dispose();
                gpu = GpuUnderlay.Create(pixels);
                _underlayCache[draw.Id] = gpu;
            }

            gpu.Update(draw);
            _underlays.Add(gpu);
            keep.Add(draw.Id);
        }

        foreach (Guid id in _underlayCache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _underlayCache[id].Dispose();
            _underlayCache.Remove(id);
        }
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
        _verticalSolids?.Dispose();
        _verticalSolids = null;
        _verticalLines?.Dispose();
        _verticalLines = null;
        _shaftBoxes?.Dispose();
        _shaftBoxes = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsInitialised)
        {
            ReleaseScene();
            foreach (GpuUnderlay underlay in _underlayCache.Values)
            {
                underlay.Dispose();
            }

            _underlayCache.Clear();
            _underlays.Clear();
            _flat.Dispose();
            _lines.Dispose();
            _textured.Dispose();
            _agentProgram.Dispose();
            _agents?.Dispose();
            _agents = null;
            IsInitialised = false;
        }
    }

    /// <summary>GPU meshes for one level.</summary>
    private sealed record GpuLevel(
        Guid LevelId, GpuMesh? Floor, GpuMesh? Solids, GpuMesh? Overlays, GpuMesh? Lines, GpuMesh? PlanOverlays, GpuMesh? PlanLines) : IDisposable
    {
        public void Dispose()
        {
            Floor?.Dispose();
            Solids?.Dispose();
            Overlays?.Dispose();
            Lines?.Dispose();
            PlanOverlays?.Dispose();
            PlanLines?.Dispose();
        }
    }
}
