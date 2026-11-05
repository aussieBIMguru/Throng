using Throng.Core.Geometry;

namespace Throng.Sim.Nav;

/// <summary>
/// Reverse Dijkstra over a set of surfaces (8-neighbour, no corner cutting), following cross-surface links whose
/// start surface is in the set. Step costs are the time to move from the neighbour into the current cell (the
/// direction people actually walk), so belts and one-way ends come out right.
/// </summary>
/// <remarks>Not thread-safe: one builder per thread. Buffers are reused between searches.</remarks>
public sealed class FieldBuilder
{
    private static readonly (int Di, int Dj)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    private readonly NavWorld _world;
    private readonly float[] _dist;
    private readonly int[] _surfaceOf;
    private readonly bool[] _scope;
    private readonly bool[] _avoid;
    private readonly PriorityQueue<int, float> _queue = new();

    /// <summary>Creates a builder for a world.</summary>
    public FieldBuilder(NavWorld world)
    {
        _world = world;
        _dist = new float[world.TotalCells];
        _surfaceOf = new int[world.TotalCells];
        for (int s = 0; s < world.Surfaces.Count; s++)
        {
            int b = world.CellBase[s];
            Array.Fill(_surfaceOf, s, b, world.Surfaces[s].CellCount);
        }

        _scope = new bool[world.Surfaces.Count];
        _avoid = new bool[world.TotalCells];
    }

    /// <summary>
    /// Builds a field over <paramref name="scope"/> towards <paramref name="targetCells"/> on
    /// <paramref name="targetSurface"/>. Cells in <paramref name="avoid"/> (on the target surface) are treated as
    /// unwalkable for this field only.
    /// </summary>
    public FlowField Build(
        IReadOnlyCollection<int> scope, int targetSurface, IEnumerable<int> targetCells, Point2 targetDirection, (Point2 A, Point2 B)? edge = null,
        IReadOnlyList<int>? avoid = null)
    {
        int avoidBase = _world.CellBase[targetSurface];
        if (avoid is not null)
        {
            foreach (int cell in avoid)
            {
                _avoid[avoidBase + cell] = true;
            }
        }

        try
        {
            return Search(scope, targetSurface, targetCells, targetDirection, edge);
        }
        finally
        {
            if (avoid is not null)
            {
                foreach (int cell in avoid)
                {
                    _avoid[avoidBase + cell] = false;
                }
            }
        }
    }

    private FlowField Search(
        IReadOnlyCollection<int> scope, int targetSurface, IEnumerable<int> targetCells, Point2 targetDirection, (Point2 A, Point2 B)? edge)
    {
        Array.Clear(_scope);
        foreach (int s in scope)
        {
            _scope[s] = true;
            Array.Fill(_dist, float.PositiveInfinity, _world.CellBase[s], _world.Surfaces[s].CellCount);
        }

        _queue.Clear();
        int targetBase = _world.CellBase[targetSurface];
        foreach (int cell in targetCells)
        {
            _dist[targetBase + cell] = 0;
            _queue.Enqueue(targetBase + cell, 0);
        }

        while (_queue.TryDequeue(out int n, out float d))
        {
            if (d > _dist[n])
            {
                continue;
            }

            int s = _surfaceOf[n];
            NavSurface surface = _world.Surfaces[s];
            int local = n - _world.CellBase[s];
            int i = local % surface.Nx;
            int j = local / surface.Nx;

            foreach ((int di, int dj) in Neighbours)
            {
                int mi = i + di, mj = j + dj;
                if (mi < 0 || mj < 0 || mi >= surface.Nx || mj >= surface.Ny)
                {
                    continue;
                }

                int m = (mj * surface.Nx) + mi;
                if (surface.Walkable[m] == 0 || _avoid[_world.CellBase[s] + m])
                {
                    continue;
                }

                // No corner cutting: a diagonal needs both orthogonal cells walkable.
                if (di != 0 && dj != 0
                    && (surface.Walkable[(j * surface.Nx) + mi] == 0 || surface.Walkable[(mj * surface.Nx) + i] == 0))
                {
                    continue;
                }

                // People walk from m into the current cell.
                Point2 delta = (surface.AxisU * (-di * surface.CellU)) + (surface.AxisV * (-dj * surface.CellV));
                float step = NavCosts.StepSeconds(surface, delta, (surface.Slowness[m] + surface.Slowness[local]) * 0.5f);
                Relax(_world.CellBase[s] + m, d + step);
            }

            for (int k = _world.LinksIntoStart[n]; k < _world.LinksIntoStart[n + 1]; k++)
            {
                NavLink link = _world.LinksInto[k];
                if (_scope[_surfaceOf[link.From]])
                {
                    Relax(link.From, d + link.Seconds);
                }
            }
        }

        var cost = new float[]?[_world.Surfaces.Count];
        foreach (int s in scope)
        {
            var slice = new float[_world.Surfaces[s].CellCount];
            Array.Copy(_dist, _world.CellBase[s], slice, 0, slice.Length);
            cost[s] = slice;
        }

        return new FlowField(cost, targetSurface, targetDirection, edge);
    }

    private void Relax(int cell, float distance)
    {
        if (float.IsFinite(distance) && distance < _dist[cell])
        {
            _dist[cell] = distance;
            _queue.Enqueue(cell, distance);
        }
    }
}
