using Throng.Core.Geometry;

namespace Throng.Sim.Engine;

/// <summary>
/// Uniform plan grid of agent indices, rebuilt every step by counting sort (no allocations after warm-up).
/// Cells are as wide as the interaction range, so a query looks at the 3 × 3 block around a point.
/// </summary>
public sealed class SpatialHash
{
    private readonly double _minX, _minY, _cell;
    private readonly int _nx, _ny;
    private readonly int[] _start;
    private int[] _items = new int[256];
    private int[] _cellOf = new int[256];

    /// <summary>Creates a hash covering <paramref name="bounds"/> (points outside are clamped to the edge cells).</summary>
    public SpatialHash(Bounds2 bounds, double cell)
    {
        Bounds2 b = bounds.IsEmpty ? new Bounds2(0, 0, 1, 1) : bounds.Inflate(cell);
        _cell = cell;
        _minX = b.MinX;
        _minY = b.MinY;
        _nx = Math.Clamp((int)Math.Ceiling(b.Width / cell), 1, 4096);
        _ny = Math.Clamp((int)Math.Ceiling(b.Height / cell), 1, 4096);
        _start = new int[(_nx * _ny) + 1];
    }

    /// <summary>Indexes the first <paramref name="count"/> positions.</summary>
    public void Build(double[] x, double[] y, int count)
    {
        if (_items.Length < count)
        {
            _items = new int[count * 2];
            _cellOf = new int[count * 2];
        }

        Array.Clear(_start);
        for (int i = 0; i < count; i++)
        {
            int c = CellIndex(x[i], y[i]);
            _cellOf[i] = c;
            _start[c + 1]++;
        }

        for (int c = 0; c < _nx * _ny; c++)
        {
            _start[c + 1] += _start[c];
        }

        // Fill using a running cursor per cell (borrowing _start shifted by one, then restoring it).
        for (int i = 0; i < count; i++)
        {
            int c = _cellOf[i];
            _items[_start[c]++] = i;
        }

        for (int c = _nx * _ny; c > 0; c--)
        {
            _start[c] = _start[c - 1];
        }

        _start[0] = 0;
    }

    /// <summary>Agent indices sorted by cell; read a block of cells with <see cref="RowRange"/>.</summary>
    public ReadOnlySpan<int> Items => _items;

    /// <summary>Cell column and row of a point.</summary>
    public void CellOf(double x, double y, out int cx, out int cy)
    {
        cx = Math.Clamp((int)((x - _minX) / _cell), 0, _nx - 1);
        cy = Math.Clamp((int)((y - _minY) / _cell), 0, _ny - 1);
    }

    /// <summary>
    /// The contiguous run of <see cref="Items"/> holding cells <paramref name="cx0"/>..<paramref name="cx1"/> of row
    /// <paramref name="cy"/> (cells in a row are adjacent in the sorted order). Columns are clamped to the grid.
    /// </summary>
    public (int Start, int End) RowRange(int cy, int cx0, int cx1)
    {
        if (cy < 0 || cy >= _ny)
        {
            return (0, 0);
        }

        cx0 = Math.Max(0, cx0);
        cx1 = Math.Min(_nx - 1, cx1);
        if (cx1 < cx0)
        {
            return (0, 0);
        }

        int row = cy * _nx;
        return (_start[row + cx0], _start[row + cx1 + 1]);
    }

    private int CellIndex(double x, double y)
    {
        CellOf(x, y, out int cx, out int cy);
        return (cy * _nx) + cx;
    }
}
