using Throng.Core.Geometry;

namespace Throng.Sim.Engine;

/// <summary>
/// One surface's part of the live density map (D-202, D-203): a grid of about 0.5 m cells laid out like the
/// surface's own grid (same origin and axes), so a floor maps in plan and a flight maps along its slope.
/// </summary>
public sealed class DensityLayer
{
    /// <summary>Nav surface the layer covers.</summary>
    public required int Surface { get; init; }

    /// <summary>Level it's shown with: the floor's level, or the level a connector piece rises from.</summary>
    public required Guid LevelId { get; init; }

    /// <summary>True for a level's floor; false for a connector piece.</summary>
    public required bool IsFloor { get; init; }

    /// <summary>Connector name for pieces; empty for floors.</summary>
    public required string Label { get; init; }

    /// <summary>Plan position of cell (0, 0)'s outer corner.</summary>
    public required Point2 Origin { get; init; }

    /// <summary>Unit plan direction of increasing i.</summary>
    public required Point2 AxisU { get; init; }

    /// <summary>Unit plan direction of increasing j.</summary>
    public required Point2 AxisV { get; init; }

    /// <summary>Cell size along u, metres.</summary>
    public required double CellU { get; init; }

    /// <summary>Cell size along v, metres.</summary>
    public required double CellV { get; init; }

    /// <summary>Cells along u.</summary>
    public required int Nx { get; init; }

    /// <summary>Cells along v.</summary>
    public required int Ny { get; init; }

    /// <summary>Height at u = 0.</summary>
    public required double Z0 { get; init; }

    /// <summary>Rise per metre of u.</summary>
    public required double SlopePerU { get; init; }

    /// <summary>First index of this layer's cells in the map's arrays.</summary>
    public required int Base { get; init; }

    /// <summary>Cells in the layer.</summary>
    public int CellCount => Nx * Ny;

    /// <summary>Plan point of local (u, v).</summary>
    public Point2 ToPlan(double u, double v) => Origin + (AxisU * u) + (AxisV * v);

    /// <summary>Plan centre of a cell (local index).</summary>
    public Point2 CellCentre(int cell) => ToPlan(((cell % Nx) + 0.5) * CellU, ((cell / Nx) + 0.5) * CellV);

    /// <summary>The layer's four corners in 3D (u0v0, u1v0, u1v1, u0v1), for draping a texture over it.</summary>
    public Point3[] Corners()
    {
        double lu = Nx * CellU, lv = Ny * CellV;
        double z1 = Z0 + (SlopePerU * lu);
        Point2 a = ToPlan(0, 0), b = ToPlan(lu, 0), c = ToPlan(lu, lv), d = ToPlan(0, lv);
        return [new Point3(a.X, a.Y, Z0), new Point3(b.X, b.Y, z1), new Point3(c.X, c.Y, z1), new Point3(d.X, d.Y, Z0)];
    }
}
