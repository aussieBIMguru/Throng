using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Render.Scene;

namespace Throng.Render.Gl;

/// <summary>How the scene is presented.</summary>
public enum ViewMode
{
    /// <summary>Orthographic top-down view of the active level, with the level below ghosted.</summary>
    Plan,

    /// <summary>Perspective orbit view of all visible levels stacked at true elevation.</summary>
    Orbit,

    /// <summary>Orthographic 2:1 game isometric of all visible levels, in one of four quarter-turn presets (D-263).</summary>
    Iso,
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

    /// <summary>People are drawn as flat discs below this many framebuffer pixels per metre (agent LOD, D-267).</summary>
    public const float DiscBelowPixelsPerMetre = 10f;

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
    private readonly Dictionary<int, GpuHeatLayer> _heatCache = new();
    private readonly List<GpuHeatLayer> _heat = [];
    private IReadOnlyList<HeatLayerDraw>? _pendingHeat;
    private long _heatVersion = long.MinValue;
    private bool _heatDirty;
    private LiftSceneDraw? _pendingCars;
    private bool _carsDirty;
    private GpuMesh? _carSolid, _carGlass, _carLines;
    private SceneData? _pendingScene;
    private SceneData _scene = SceneData.Empty;
    private readonly float[] _levelZ = new float[Shaders.MaxLevels];
    private double[] _elevations = [];
    private float _gap;
    private (int Mode, float Shift) _spanWarp;
    private bool _clips;
    private Vector3 _clipMin, _clipMax;
    private bool _boxOn;
    private Point3 _boxMin, _boxMax;
    private GpuMesh? _boxLines;
    private (Point3, Point3, int, bool) _boxSignature;

    /// <summary>Section box handle faces (D-277): west, east, south, north, bottom, top.</summary>
    public const int BoxFaces = 6;

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

    /// <summary>What agent bodies are coloured by (D-146); read at every render.</summary>
    public AgentColoring AgentColoring { get; set; }

    /// <summary>Cutaway and exploded levels for the 3D and ISO views (D-264–D-266); plan ignores it. Read at every render.</summary>
    public ViewSettings View { get; set; } = ViewSettings.None;

    /// <summary>
    /// Framebuffer pixels per metre near the middle of the view, set by the host each frame; below
    /// <see cref="DiscBelowPixelsPerMetre"/> people are drawn as discs (D-267).
    /// </summary>
    public float AgentPixelsPerMetre { get; set; } = 100f;

    /// <summary>Section box face whose handle is under the pointer (0–5, see <see cref="BoxFaces"/>), or −1 (D-277).</summary>
    public int HoveredBoxFace { get; set; } = -1;

    /// <summary>
    /// Where the six section box handles are drawn (face centres, exploded heights) after the last 3D/ISO frame, and the
    /// box's true extents; false while the box is off or in plan (D-277).
    /// </summary>
    public bool TryGetBoxHandles(out Vector3[] centres, out Point3 boxMin, out Point3 boxMax)
    {
        boxMin = _boxMin;
        boxMax = _boxMax;
        if (!_boxOn)
        {
            centres = [];
            return false;
        }

        Point3 c = new((_boxMin.X + _boxMax.X) * 0.5, (_boxMin.Y + _boxMax.Y) * 0.5, (_boxMin.Z + _boxMax.Z) * 0.5);
        Point3[] faces =
        [
            c with { X = _boxMin.X }, c with { X = _boxMax.X },
            c with { Y = _boxMin.Y }, c with { Y = _boxMax.Y },
            c with { Z = _boxMin.Z }, c with { Z = _boxMax.Z },
        ];
        centres = [.. faces.Select(p => new Vector3((float)p.X, (float)p.Y, (float)WarpZ(p.Z)))];
        return true;
    }

    /// <summary>The exploded height of a true height with the last frame's explode (D-264).</summary>
    public double WarpZ(double z) => ExplodeMap.Warp(z, _elevations, _gap);

    /// <summary>The true height of an exploded height (inverse of <see cref="WarpZ"/>).</summary>
    public double UnwarpZ(double zw) => ExplodeMap.Unwarp(zw, _elevations, _gap);

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

    /// <summary>
    /// Lift cars and their doors to draw in 3D from the next render (round 013b, doors round 014); null for none. The
    /// meshes are rebuilt only when a different instance is passed, so the caller passes the same one while nothing moved.
    /// </summary>
    public void SetLiftCars(LiftSceneDraw? cars)
    {
        if (ReferenceEquals(cars, _pendingCars))
        {
            return;
        }

        _pendingCars = cars;
        _carsDirty = true;
    }

    /// <summary>
    /// Live heatmap layers to drape over floors and flights from the next render (D-202); null or empty for none.
    /// Textures are re-uploaded only when <paramref name="version"/> changes. While any layer shows, agents are drawn
    /// smaller and dimmed (D-207).
    /// </summary>
    public void SetHeatmap(IReadOnlyList<HeatLayerDraw>? layers, long version)
    {
        if (version == _heatVersion && ReferenceEquals(layers, _pendingHeat))
        {
            return;
        }

        _pendingHeat = layers;
        _heatVersion = version;
        _heatDirty = true;
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
        _agentProgram.SetColorMode(AgentColoring == AgentColoring.Stress ? 1 : 0);
        if (_heatDirty)
        {
            UploadHeat(_pendingHeat);
            _heatDirty = false;
        }

        _agentProgram.SetDim(_heat.Count > 0 ? 1f : 0f);
        if (_agentsDirty && _agents is not null)
        {
            _agents.Update(_pendingAgents ?? [], _pendingAgents is null ? 0 : _pendingAgentCount);
            _agentsDirty = false;
        }

        if (_carsDirty)
        {
            UploadCars(_pendingCars);
            _carsDirty = false;
        }

        PrepareView(mode == ViewMode.Plan ? ViewSettings.None : View);
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
    /// Sets the explode and clip uniforms on every program for this frame (D-264, D-266): the distinct elevations of the
    /// scene's levels and the gap, and the clip box over the scene's extents. With no gap nothing is warped.
    /// </summary>
    private void PrepareView(ViewSettings view)
    {
        _gap = view.ExplodeGapM > 0 && _elevations.Length >= 2 ? (float)view.ExplodeGapM : 0f;
        _spanWarp = _gap > 0 ? (1, 0f) : (0, 0f);
        (Point3 min, Point3 max) = view.ClipBox(_scene.Bounds, _scene.MinElevationM, _scene.MaxElevationM);
        var boxMin = new Vector3((float)min.X, (float)min.Y, (float)min.Z);
        var boxMax = new Vector3((float)max.X, (float)max.Y, (float)max.Z);
        _clips = view.Clips;
        _clipMin = boxMin;
        _clipMax = boxMax;
        ReadOnlySpan<ShaderProgram> programs = [_flat, _lines, _textured, _agentProgram];
        foreach (ShaderProgram program in programs)
        {
            program.Use();
            program.SetExplode(_levelZ, _elevations.Length, _gap);
            program.SetWarp(0);
            program.SetClip(view.Clips, boxMin, boxMax);
        }

        // The section box itself, without the clip-above cap, for its outline and handles (D-277).
        _boxOn = view.SectionBox && !_scene.Bounds.IsEmpty;
        if (_boxOn)
        {
            (_boxMin, _boxMax) = (view with { ClipAboveZ = null }).ClipBox(_scene.Bounds, _scene.MinElevationM, _scene.MaxElevationM);
        }
    }

    /// <summary>
    /// Section box outline and the six face handles (D-277), drawn over everything without the clip (it would cut the
    /// box's own edges). Rebuilt only when the box, the hovered handle or the explode changes.
    /// </summary>
    private void DrawSectionBox()
    {
        if (!_boxOn)
        {
            return;
        }

        (Point3, Point3, int, bool) signature = (_boxMin, _boxMax, HoveredBoxFace, _gap > 0);
        if (_boxLines is null || signature != _boxSignature)
        {
            _boxLines?.Dispose();
            _boxLines = GpuMesh.Create(SectionBoxMesh.Build(_boxMin, _boxMax, HoveredBoxFace));
            _boxSignature = signature;
        }

        GL.Disable(EnableCap.DepthTest);
        _lines.Use();
        _lines.SetClip(false, _clipMin, _clipMax);
        DrawLines(_boxLines, 1f, LineWidthDip);
        _lines.Use();
        _lines.SetClip(_clips, _clipMin, _clipMax);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>A level's own meshes move up rigidly when exploded (D-264).</summary>
    private (int Mode, float Shift) LevelWarp(GpuLevel level) =>
        _gap > 0 ? (2, (float)ExplodeMap.LevelShift(level.ElevationM, _elevations, _gap)) : (0, 0f);

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

        DrawHeat();
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
            DrawTriangles(level.Solids, 1f, LevelWarp(level));
        }

        // Sloped connector pieces are solid geometry (D-054).
        DrawTriangles(_verticalSolids, 1f);

        GL.DepthMask(false);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Floor, FloorAlpha3D, LevelWarp(level));
        }

        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Overlays, 1f, LevelWarp(level));
        }

        DrawHeat();

        // Faint shaft boxes after the floors, still without depth writes, so everything reads through them (D-067).
        DrawTriangles(_shaftBoxes, 1f);

        foreach (GpuLevel level in _levels)
        {
            DrawLines(level.Lines, 1f, LineWidthDip, LevelWarp(level));
        }

        DrawLines(_verticalLines, 1f, LineWidthDip);

        // Agents last (D-102): floors first write depth only (no colour), so people on other levels don't show
        // through a floor (D-117), while outlines and solids below still read through as before.
        GL.DepthMask(true);
        GL.ColorMask(false, false, false, false);
        foreach (GpuLevel level in _levels)
        {
            DrawTriangles(level.Floor, 1f, LevelWarp(level));
        }

        GL.ColorMask(true, true, true, true);

        // Lift car floors write depth with the agents, then the translucent walls go over the riders without depth
        // writes so the people inside stay visible, then the car outlines (round 013b).
        DrawTriangles(_carSolid, 1f);
        DrawAgents();
        GL.DepthMask(false);
        DrawTriangles(_carGlass, 1f);
        DrawLines(_carLines, 1f, LineWidthDip);
        GL.DepthMask(true);
        DrawSectionBox();
    }

    /// <summary>Rebuilds the lift car meshes (a few boxes, so cheap enough on every car move).</summary>
    private void UploadCars(LiftSceneDraw? cars)
    {
        ReleaseCars();
        if (cars is null || (cars.Cars.Count == 0 && cars.Doors.Count == 0))
        {
            return;
        }

        (MeshData solid, MeshData glass, MeshData lines) = LiftCarMeshes.Build(cars);
        _carSolid = GpuMesh.Create(solid);
        _carGlass = GpuMesh.Create(glass);
        _carLines = GpuMesh.Create(lines);
    }

    private void ReleaseCars()
    {
        _carSolid?.Dispose();
        _carSolid = null;
        _carGlass?.Dispose();
        _carGlass = null;
        _carLines?.Dispose();
        _carLines = null;
    }

    /// <summary>Draws the heatmap layers with the textured program at full opacity (the texels carry their alpha).</summary>
    private void DrawHeat()
    {
        if (_heat.Count == 0)
        {
            return;
        }

        _textured.Use();
        _textured.SetAlpha(1f);
        _textured.SetWarp(_spanWarp.Mode, _spanWarp.Shift);
        foreach (GpuHeatLayer layer in _heat)
        {
            layer.Draw();
        }
    }

    /// <summary>Uploads heat layers, reusing textures by key while the size is unchanged; frees the rest.</summary>
    private void UploadHeat(IReadOnlyList<HeatLayerDraw>? layers)
    {
        _heat.Clear();
        var keep = new HashSet<int>();
        foreach (HeatLayerDraw draw in layers ?? [])
        {
            if (draw.Width <= 0 || draw.Height <= 0 || draw.Bgra.Length < draw.Width * draw.Height * 4 || draw.Corners.Count < 4)
            {
                continue;
            }

            if (!_heatCache.TryGetValue(draw.Key, out GpuHeatLayer? gpu) || gpu.Width != draw.Width || gpu.Height != draw.Height)
            {
                gpu?.Dispose();
                gpu = GpuHeatLayer.Create(draw.Width, draw.Height);
                _heatCache[draw.Key] = gpu;
            }

            gpu.Update(draw);
            _heat.Add(gpu);
            keep.Add(draw.Key);
        }

        foreach (int key in _heatCache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _heatCache[key].Dispose();
            _heatCache.Remove(key);
        }
    }

    /// <summary>Draws the agent instances with the agent program: capsules, or discs when small on screen (D-267).</summary>
    private void DrawAgents()
    {
        if (_agents is null)
        {
            return;
        }

        _agentProgram.Use();
        _agentProgram.SetWarp(_spanWarp.Mode, _spanWarp.Shift);
        _agents.Draw(discs: AgentPixelsPerMetre < DiscBelowPixelsPerMetre);
    }

    /// <summary>
    /// Draws a triangle mesh with the flat program; <paramref name="warp"/> is a level's rigid shift, or null for
    /// spanning geometry (stretched when exploded, D-264).
    /// </summary>
    private void DrawTriangles(GpuMesh? mesh, float alpha, (int Mode, float Shift)? warp = null)
    {
        if (mesh is null)
        {
            return;
        }

        (int mode, float shift) = warp ?? _spanWarp;
        _flat.Use();
        _flat.SetAlpha(alpha);
        _flat.SetWarp(mode, shift);
        mesh.Draw();
    }

    /// <summary>Draws a line mesh as screen-space quads of the given width (D-049); <paramref name="warp"/> as for triangles.</summary>
    private void DrawLines(GpuMesh? mesh, float alpha, float widthDip, (int Mode, float Shift)? warp = null)
    {
        if (mesh is null)
        {
            return;
        }

        (int mode, float shift) = warp ?? _spanWarp;
        _lines.Use();
        _lines.SetAlpha(alpha);
        _lines.SetLineWidth(widthDip * _pixelsPerDip);
        _lines.SetWarp(mode, shift);
        mesh.Draw();
    }

    private void Upload(SceneData scene)
    {
        ReleaseScene();
        foreach (LevelScene level in scene.Levels)
        {
            _levels.Add(new GpuLevel(
                level.LevelId,
                level.ElevationM,
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

        // Level elevations for exploding (D-264), once per scene.
        _elevations = ExplodeMap.Elevations(scene.Levels.Select(l => l.ElevationM));
        for (int k = 0; k < _elevations.Length; k++)
        {
            _levelZ[k] = (float)_elevations[k];
        }
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
            ReleaseCars();
            _boxLines?.Dispose();
            _boxLines = null;
            foreach (GpuUnderlay underlay in _underlayCache.Values)
            {
                underlay.Dispose();
            }

            _underlayCache.Clear();
            _underlays.Clear();
            foreach (GpuHeatLayer layer in _heatCache.Values)
            {
                layer.Dispose();
            }

            _heatCache.Clear();
            _heat.Clear();
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
        Guid LevelId, double ElevationM, GpuMesh? Floor, GpuMesh? Solids, GpuMesh? Overlays, GpuMesh? Lines, GpuMesh? PlanOverlays, GpuMesh? PlanLines) : IDisposable
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
