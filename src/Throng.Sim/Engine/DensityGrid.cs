using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// The live density map (M5, round 011a): people per square metre on every floor, landing and stair or ramp flight,
/// on about 0.5 m cells (D-203). Each person is spread over a 1 m radius on the walkable cells around them and the
/// result is smoothed over about 5 s, sampled every 0.5 s of simulated time. Belts aren't mapped (D-205).
/// Round 011b adds the cumulative maps (D-216): max, mean, time at LoS E or worse and a footfall trail, gathered at
/// every sample once the warm-up has passed (D-218) and kept until the run is reset.
/// </summary>
/// <remarks>
/// Owned by the engine and written only on the simulation thread. <see cref="Layers"/> and <see cref="Kinds"/> are
/// fixed once built, so the UI may read them; the smoothed values reach the UI through
/// <see cref="SimRunner.CopyDensity"/>.
/// </remarks>
public sealed class DensityGrid
{
    /// <summary>Target cell size, metres (D-203).</summary>
    public const double CellM = 0.5;

    /// <summary>Radius each person is spread over, metres (D-203).</summary>
    public const double KernelRadiusM = 1.0;

    /// <summary>Smoothing time constant, seconds (D-203).</summary>
    public const double SmoothingS = 5.0;

    /// <summary>Simulated seconds between samples.</summary>
    public const double SampleIntervalS = 0.5;

    // The kernel is flat to 0.75 m and fades to nothing at 1.25 m, so its footprint is close to a 1 m disc without
    // the stepping a hard edge gives on 0.5 m cells.
    private const double KernelSoftM = 0.25;

    // Queue cells: within this distance of a queue slot.
    private const double QueueReachM = 0.45;

    // A walk between two samples longer than this many cells is a jump (lift, teleport): only the end cell counts.
    private const int MaxTrailStepCells = 16;

    // Fruin lower bounds of grade D, m² per person (FruinLevelOfService in Throng.Analysis, which the sim can't
    // reference): at or below them a cell is at E or worse. Round011bTests checks they agree.
    private const double WalkwayDBoundM2 = 0.93, QueueDBoundM2 = 0.28, StairDBoundM2 = 0.65;

    private readonly NavWorld _world;
    private readonly DensityLayer[] _layers;
    private readonly int[] _layerOfSurface;
    private readonly DensityCellKind[] _kinds;
    private readonly float[] _smoothed;
    private readonly float[] _instant;
    private readonly float[] _max;
    private readonly float[] _mean;
    private readonly float[] _ePlusS;
    private readonly float[] _footfall;
    private readonly float[] _ePlusDensity;
    private readonly double _warmUpS;
    private int _accumulatedSamples;
    private double _lastSampleS = double.NegativeInfinity;

    /// <summary>Builds the layers and cell kinds for a compiled model.</summary>
    public DensityGrid(SimModel model)
    {
        _world = model.World;
        var layers = new List<DensityLayer>();
        _layerOfSurface = new int[_world.Surfaces.Count];
        int total = 0;
        foreach (NavSurface s in _world.Surfaces)
        {
            _layerOfSurface[s.Index] = -1;
            if (s.Kind == NavSurfaceKind.Piece && s.Motion == NavMotion.Belt)
            {
                continue;
            }

            double lu = s.Nx * s.CellU, lv = s.Ny * s.CellV;
            int nx = Math.Max(1, (int)Math.Round(lu / CellM));
            int ny = Math.Max(1, (int)Math.Round(lv / CellM));
            bool floor = s.Kind == NavSurfaceKind.Floor;
            _layerOfSurface[s.Index] = layers.Count;
            layers.Add(new DensityLayer
            {
                Surface = s.Index,
                LevelId = floor ? s.LevelId : LevelOfPiece(s),
                IsFloor = floor,
                Label = !floor && s.ConnectorIndex >= 0 ? _world.Connectors[s.ConnectorIndex].Name : string.Empty,
                Origin = s.Origin,
                AxisU = s.AxisU,
                AxisV = s.AxisV,
                CellU = lu / nx,
                CellV = lv / ny,
                Nx = nx,
                Ny = ny,
                Z0 = s.Z0,
                SlopePerU = s.SlopePerU,
                Base = total,
            });
            total += nx * ny;
        }

        _layers = [.. layers];
        _kinds = new DensityCellKind[total];
        _smoothed = new float[total];
        _instant = new float[total];
        _max = new float[total];
        _mean = new float[total];
        _ePlusS = new float[total];
        _footfall = new float[total];
        _ePlusDensity = new float[total];
        _warmUpS = Math.Max(0, model.WarmUpS);
        MarkWalkable();
        MarkQueues(model);
        for (int c = 0; c < total; c++)
        {
            _ePlusDensity[c] = (float)LosEOrWorseDensity(_kinds[c]);
        }
    }

    /// <summary>The mapped surfaces.</summary>
    public IReadOnlyList<DensityLayer> Layers => _layers;

    /// <summary>What each cell is (which Fruin table applies); <see cref="DensityCellKind.None"/> isn't drawn.</summary>
    public ReadOnlySpan<DensityCellKind> Kinds => _kinds;

    /// <summary>Total cells over all layers.</summary>
    public int CellCount => _kinds.Length;

    /// <summary>Smoothed people per square metre per cell.</summary>
    public ReadOnlySpan<float> Smoothed => _smoothed;

    /// <summary>Bumped at every sample, so readers can skip unchanged maps.</summary>
    public long Version { get; private set; }

    /// <summary>Seconds left out of the cumulative maps at the start of the run (D-218).</summary>
    public double WarmUpS => _warmUpS;

    /// <summary>Simulated seconds gathered into the cumulative maps so far (0 during the warm-up).</summary>
    public double AccumulatedS { get; private set; }

    /// <summary>One value per cell for a measure (<see cref="Smoothed"/> for <see cref="DensityMeasure.Live"/>).</summary>
    public ReadOnlySpan<float> Values(DensityMeasure measure) => measure switch
    {
        DensityMeasure.Max => _max,
        DensityMeasure.Mean => _mean,
        DensityMeasure.LosEOrWorseS => _ePlusS,
        DensityMeasure.Footfall => _footfall,
        _ => _smoothed,
    };

    /// <summary>
    /// Density at and above which a cell of this kind is at Fruin LoS E or worse, people per square metre (D-216).
    /// </summary>
    public static double LosEOrWorseDensity(DensityCellKind kind) => kind switch
    {
        DensityCellKind.Queue => 1.0 / QueueDBoundM2,
        DensityCellKind.Stair => 1.0 / StairDBoundM2,
        DensityCellKind.Walkway => 1.0 / WalkwayDBoundM2,
        _ => double.PositiveInfinity,
    };

    /// <summary>The layer showing a nav surface, or −1 (belts).</summary>
    public int LayerOfSurface(int surface) => surface >= 0 && surface < _layerOfSurface.Length ? _layerOfSurface[surface] : -1;

    /// <summary>Back to an empty map (run reset).</summary>
    public void Clear()
    {
        Array.Clear(_smoothed);
        Array.Clear(_instant);
        Array.Clear(_max);
        Array.Clear(_mean);
        Array.Clear(_ePlusS);
        Array.Clear(_footfall);
        _accumulatedSamples = 0;
        AccumulatedS = 0;
        _lastSampleS = double.NegativeInfinity;
        Version++;
    }

    /// <summary>
    /// Samples the agents when <see cref="SampleIntervalS"/> has passed since the last sample and blends the result
    /// into the smoothed map.
    /// </summary>
    public void Sample(AgentStore agents, double timeS)
    {
        double since = timeS - _lastSampleS;
        if (since < SampleIntervalS - 1e-9)
        {
            return;
        }

        bool counting = timeS >= _warmUpS - 1e-9;
        Array.Clear(_instant);
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents.Phase[i] == AgentPhase.LiftRide)
            {
                // In a lift car (round 013): off the floor, so neither density nor footfall.
                agents.TrailCell[i] = -1;
                continue;
            }

            int layer = LayerOfSurface(agents.Surface[i]);
            if (layer >= 0)
            {
                Splat(_layers[layer], agents.X[i], agents.Y[i]);
            }

            Trail(agents, i, layer, counting);
        }

        double dt = double.IsInfinity(since) ? SampleIntervalS : Math.Min(since, 10 * SmoothingS);
        float blend = (float)(1 - Math.Exp(-dt / SmoothingS));
        for (int c = 0; c < _smoothed.Length; c++)
        {
            _smoothed[c] += (_instant[c] - _smoothed[c]) * blend;
        }

        if (counting)
        {
            Accumulate(Math.Min(dt, 2 * SampleIntervalS));
        }

        _lastSampleS = timeS;
        Version++;
    }

    /// <summary>Folds the smoothed map into the max, mean and E-or-worse maps (D-216).</summary>
    private void Accumulate(double dt)
    {
        _accumulatedSamples++;
        AccumulatedS += dt;
        float inverseCount = 1f / _accumulatedSamples;
        float seconds = (float)dt;
        for (int c = 0; c < _smoothed.Length; c++)
        {
            float d = _smoothed[c];
            if (d > _max[c])
            {
                _max[c] = d;
            }

            _mean[c] += (d - _mean[c]) * inverseCount;
            if (d >= _ePlusDensity[c])
            {
                _ePlusS[c] += seconds;
            }
        }
    }

    /// <summary>
    /// Footfall (D-216): counts every mapped cell a person stepped onto since the last sample, along the straight
    /// line between the two cells on the same layer (people walk about 0.7 m per sample, more than a cell).
    /// </summary>
    private void Trail(AgentStore agents, int i, int layer, bool counting)
    {
        int previous = agents.TrailCell[i];
        if (layer < 0)
        {
            agents.TrailCell[i] = -1;
            return;
        }

        DensityLayer l = _layers[layer];
        if (!CellOf(l, agents.X[i], agents.Y[i], out int ci, out int cj))
        {
            agents.TrailCell[i] = -1;
            return;
        }

        int cell = l.Base + (cj * l.Nx) + ci;
        if (cell == previous)
        {
            return;
        }

        agents.TrailCell[i] = cell;
        if (!counting)
        {
            return;
        }

        bool sameLayer = previous >= l.Base && previous < l.Base + l.CellCount;
        int pi = sameLayer ? (previous - l.Base) % l.Nx : ci;
        int pj = sameLayer ? (previous - l.Base) / l.Nx : cj;
        int steps = Math.Max(Math.Abs(ci - pi), Math.Abs(cj - pj));
        if (!sameLayer || steps > MaxTrailStepCells)
        {
            CountFootfall(cell);
            return;
        }

        // Walk the cells between, skipping the one they were already counted on.
        for (int k = 1; k <= steps; k++)
        {
            int ii = pi + (int)Math.Round((ci - pi) * (double)k / steps);
            int jj = pj + (int)Math.Round((cj - pj) * (double)k / steps);
            CountFootfall(l.Base + (jj * l.Nx) + ii);
        }
    }

    private void CountFootfall(int cell)
    {
        if (_kinds[cell] != DensityCellKind.None)
        {
            _footfall[cell] += 1f;
        }
    }

    /// <summary>The cell holding a point, false when it's off the layer.</summary>
    private static bool CellOf(DensityLayer layer, double x, double y, out int ci, out int cj)
    {
        double dx = x - layer.Origin.X, dy = y - layer.Origin.Y;
        double u = (dx * layer.AxisU.X) + (dy * layer.AxisU.Y);
        double v = (dx * layer.AxisV.X) + (dy * layer.AxisV.Y);
        ci = (int)Math.Floor(u / layer.CellU);
        cj = (int)Math.Floor(v / layer.CellV);
        return ci >= 0 && cj >= 0 && ci < layer.Nx && cj < layer.Ny;
    }

    /// <summary>
    /// Spreads one person over the mapped cells within the kernel, weights normalised so the person adds exactly
    /// one to the layer (near a wall the share that would land in the wall goes to the floor beside it).
    /// </summary>
    private void Splat(DensityLayer layer, double x, double y)
    {
        double dx = x - layer.Origin.X, dy = y - layer.Origin.Y;
        double u = (dx * layer.AxisU.X) + (dy * layer.AxisU.Y);
        double v = (dx * layer.AxisV.X) + (dy * layer.AxisV.Y);
        int ci = (int)Math.Floor(u / layer.CellU);
        int cj = (int)Math.Floor(v / layer.CellV);
        double outer = KernelRadiusM + KernelSoftM;
        int ri = (int)Math.Ceiling(outer / layer.CellU);
        int rj = (int)Math.Ceiling(outer / layer.CellV);
        double area = layer.CellU * layer.CellV;

        double sum = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = Math.Max(0, cj - rj); j <= Math.Min(layer.Ny - 1, cj + rj); j++)
            {
                double ev = ((j + 0.5) * layer.CellV) - v;
                for (int i = Math.Max(0, ci - ri); i <= Math.Min(layer.Nx - 1, ci + ri); i++)
                {
                    int cell = layer.Base + (j * layer.Nx) + i;
                    if (_kinds[cell] == DensityCellKind.None)
                    {
                        continue;
                    }

                    double eu = ((i + 0.5) * layer.CellU) - u;
                    double r = Math.Sqrt((eu * eu) + (ev * ev));
                    double w = Math.Clamp((KernelRadiusM + KernelSoftM - r) / (2 * KernelSoftM), 0, 1);
                    if (pass == 0)
                    {
                        sum += w;
                    }
                    else if (w > 0)
                    {
                        _instant[cell] += (float)(w / (sum * area));
                    }
                }
            }

            if (sum <= 0)
            {
                // Nothing mapped within reach (standing on a cell edge of an odd shape): own cell if it's mapped.
                if (ci >= 0 && cj >= 0 && ci < layer.Nx && cj < layer.Ny)
                {
                    int own = layer.Base + (cj * layer.Nx) + ci;
                    if (_kinds[own] != DensityCellKind.None)
                    {
                        _instant[own] += (float)(1 / area);
                    }
                }

                return;
            }
        }
    }

    /// <summary>A cell is mapped when any walkable nav cell's centre falls in it.</summary>
    private void MarkWalkable()
    {
        foreach (DensityLayer layer in _layers)
        {
            NavSurface s = _world.Surfaces[layer.Surface];
            DensityCellKind kind = layer.IsFloor || s.Motion == NavMotion.Walk ? DensityCellKind.Walkway : DensityCellKind.Stair;
            for (int n = 0; n < s.CellCount; n++)
            {
                if (s.Walkable[n] == 0)
                {
                    continue;
                }

                int i = Math.Min(layer.Nx - 1, (int)((((n % s.Nx) + 0.5) * s.CellU) / layer.CellU));
                int j = Math.Min(layer.Ny - 1, (int)((((n / s.Nx) + 0.5) * s.CellV) / layer.CellV));
                _kinds[layer.Base + (j * layer.Nx) + i] = kind;
            }
        }
    }

    /// <summary>Queue lines, snakes and dwell zones use the queuing table (D-205).</summary>
    private void MarkQueues(SimModel model)
    {
        foreach (SimDevice device in model.Devices)
        {
            int l = LayerOfSurface(device.Floor);
            if (l < 0)
            {
                continue;
            }

            DensityLayer layer = _layers[l];
            foreach (Point2[] line in device.Slots)
            {
                foreach (Point2 slot in line)
                {
                    MarkAround(layer, slot, QueueReachM);
                }
            }
        }

        foreach (SimJourney journey in model.Journeys)
        {
            foreach (SimStep step in journey.Steps)
            {
                if (step.Kind != JourneyStepKind.Dwell)
                {
                    continue;
                }

                foreach (int target in step.Targets)
                {
                    if (target < 0 || target >= _world.Sinks.Count)
                    {
                        continue;
                    }

                    NavSink zone = _world.Sinks[target];
                    int l = LayerOfSurface(zone.Floor);
                    if (l < 0)
                    {
                        continue;
                    }

                    NavSurface s = _world.Surfaces[zone.Floor];
                    foreach (int n in zone.Cells)
                    {
                        Point2 p = s.CellCentre(n);
                        MarkAround(_layers[l], p, 0.01);
                    }
                }
            }
        }
    }

    /// <summary>Turns mapped cells whose centre is within <paramref name="reach"/> of a point (or holding it) into queue cells.</summary>
    private void MarkAround(DensityLayer layer, Point2 p, double reach)
    {
        double dx = p.X - layer.Origin.X, dy = p.Y - layer.Origin.Y;
        double u = (dx * layer.AxisU.X) + (dy * layer.AxisU.Y);
        double v = (dx * layer.AxisV.X) + (dy * layer.AxisV.Y);
        int ci = (int)Math.Floor(u / layer.CellU);
        int cj = (int)Math.Floor(v / layer.CellV);
        int ri = (int)Math.Ceiling(reach / layer.CellU), rj = (int)Math.Ceiling(reach / layer.CellV);
        for (int j = Math.Max(0, cj - rj); j <= Math.Min(layer.Ny - 1, cj + rj); j++)
        {
            for (int i = Math.Max(0, ci - ri); i <= Math.Min(layer.Nx - 1, ci + ri); i++)
            {
                int cell = layer.Base + (j * layer.Nx) + i;
                if (_kinds[cell] == DensityCellKind.None)
                {
                    continue;
                }

                double eu = ((i + 0.5) * layer.CellU) - u, ev = ((j + 0.5) * layer.CellV) - v;
                bool holds = i == ci && j == cj;
                if (holds || (eu * eu) + (ev * ev) <= reach * reach)
                {
                    _kinds[cell] = DensityCellKind.Queue;
                }
            }
        }
    }

    /// <summary>
    /// The level a connector piece is shown with: the highest served floor end at or below the piece's low end, or
    /// the lowest one when none is.
    /// </summary>
    private Guid LevelOfPiece(NavSurface piece)
    {
        double low = Math.Min(piece.Z0, piece.Z0 + (piece.SlopePerU * piece.Nx * piece.CellU)) + 0.05;
        Guid best = Guid.Empty, lowest = Guid.Empty;
        double bestZ = double.NegativeInfinity, lowestZ = double.PositiveInfinity;
        foreach (NavPortal portal in _world.Portals)
        {
            if (portal.Connector != piece.ConnectorIndex)
            {
                continue;
            }

            if (portal.ElevationM <= low && portal.ElevationM > bestZ)
            {
                bestZ = portal.ElevationM;
                best = portal.LevelId;
            }

            if (portal.ElevationM < lowestZ)
            {
                lowestZ = portal.ElevationM;
                lowest = portal.LevelId;
            }
        }

        return best != Guid.Empty ? best : lowest;
    }
}
