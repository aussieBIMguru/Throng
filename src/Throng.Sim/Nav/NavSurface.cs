using Throng.Core.Geometry;

namespace Throng.Sim.Nav;

/// <summary>What a navigation surface is.</summary>
public enum NavSurfaceKind
{
    /// <summary>A level's floor (one grid per level).</summary>
    Floor,

    /// <summary>One connector piece: a flight, landing, ramp run, escalator flat or incline, or a belt.</summary>
    Piece,
}

/// <summary>How people move on a surface; sets the step costs and speed factors.</summary>
public enum NavMotion
{
    /// <summary>Level walking (floors and landings).</summary>
    Walk,

    /// <summary>A stair flight: slower up and down, slower still sideways.</summary>
    Stair,

    /// <summary>A ramp run: a little slower uphill.</summary>
    Ramp,

    /// <summary>A moving belt (escalator flats and incline, travelators): carried along, never against it.</summary>
    Belt,
}

/// <summary>
/// One grid of the navigation world (M2, BRIEF §17): a level's floor, or one connector piece. Agents live in plan
/// (x, y) plus a surface index; the surface gives their height.
/// </summary>
/// <remarks>
/// Cells are laid out from <see cref="Origin"/> along <see cref="AxisU"/> (index i, <see cref="Nx"/> cells of
/// <see cref="CellU"/>) and <see cref="AxisV"/> (index j, <see cref="Ny"/> cells of <see cref="CellV"/>); cell index =
/// j × Nx + i. Floors use world X and Y. Pieces run u along the piece axis and v across it from its right edge, so
/// the centreline is at v = <see cref="HalfWidth"/>.
/// </remarks>
public sealed class NavSurface
{
    /// <summary>Position in <see cref="NavWorld.Surfaces"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Floor or piece.</summary>
    public required NavSurfaceKind Kind { get; init; }

    /// <summary>Level of a floor (Guid.Empty for pieces).</summary>
    public Guid LevelId { get; init; }

    /// <summary>Index into <see cref="NavWorld.Connectors"/> for pieces; −1 for floors.</summary>
    public int ConnectorIndex { get; init; } = -1;

    /// <summary>Diagnostic name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Plan position of the grid corner (cell (0, 0)'s outer corner).</summary>
    public required Point2 Origin { get; init; }

    /// <summary>Unit plan direction of increasing i.</summary>
    public required Point2 AxisU { get; init; }

    /// <summary>Unit plan direction of increasing j (90° counter-clockwise from <see cref="AxisU"/>).</summary>
    public required Point2 AxisV { get; init; }

    /// <summary>Cell size along u, metres.</summary>
    public required double CellU { get; init; }

    /// <summary>Cell size along v, metres.</summary>
    public required double CellV { get; init; }

    /// <summary>Cells along u.</summary>
    public required int Nx { get; init; }

    /// <summary>Cells along v.</summary>
    public required int Ny { get; init; }

    /// <summary>Height at u = 0, metres.</summary>
    public double Z0 { get; init; }

    /// <summary>Rise per metre of u (pieces always rise along their own axis).</summary>
    public double SlopePerU { get; init; }

    /// <summary>Half the clear width (pieces); 0 for floors.</summary>
    public double HalfWidth { get; init; }

    /// <summary>How people move here.</summary>
    public NavMotion Motion { get; init; } = NavMotion.Walk;

    /// <summary>Belt speed along <see cref="AxisU"/>, m/s (negative runs against the axis); 0 when not a belt.</summary>
    public double BeltSpeed { get; init; }

    /// <summary>True when step-free (lift-only) profiles may use the surface: floors, ramps and travelators.</summary>
    public bool StepFreeOk { get; init; } = true;

    /// <summary>1 where agents can stand.</summary>
    public required byte[] Walkable { get; init; }

    /// <summary>1 ÷ speed factor per cell (Slow zones, &gt; 1 is slower).</summary>
    public required float[] Slowness { get; init; }

    /// <summary>Distance to the nearest unwalkable cell or grid edge, metres (capped at 2 m; floors only).</summary>
    public required float[] WallDistance { get; init; }

    /// <summary>Crossings to other surfaces (movement transfer).</summary>
    public List<PortalSegment> Segments { get; } = [];

    /// <summary>Total cells.</summary>
    public int CellCount => Nx * Ny;

    /// <summary>Plan velocity of the belt (zero when not a belt).</summary>
    public Point2 BeltVelocity => AxisU * BeltSpeed;

    /// <summary>Local (u, v) of a plan point.</summary>
    public void ToLocal(double x, double y, out double u, out double v)
    {
        double dx = x - Origin.X;
        double dy = y - Origin.Y;
        u = (dx * AxisU.X) + (dy * AxisU.Y);
        v = (dx * AxisV.X) + (dy * AxisV.Y);
    }

    /// <summary>Plan point of local (u, v).</summary>
    public Point2 ToPlan(double u, double v) => Origin + (AxisU * u) + (AxisV * v);

    /// <summary>Cell containing a plan point, or −1 when outside the grid.</summary>
    public int CellAt(double x, double y)
    {
        ToLocal(x, y, out double u, out double v);
        return CellAtLocal(u, v);
    }

    /// <summary>Cell containing a local point, or −1 when outside the grid.</summary>
    public int CellAtLocal(double u, double v)
    {
        if (u < 0 || v < 0)
        {
            return -1;
        }

        int i = (int)(u / CellU);
        int j = (int)(v / CellV);
        return i < Nx && j < Ny ? (j * Nx) + i : -1;
    }

    /// <summary>True when the plan point is on a walkable cell of this surface.</summary>
    public bool IsWalkable(double x, double y)
    {
        int cell = CellAt(x, y);
        return cell >= 0 && Walkable[cell] != 0;
    }

    /// <summary>Plan centre of a cell.</summary>
    public Point2 CellCentre(int cell)
    {
        int i = cell % Nx;
        int j = cell / Nx;
        return ToPlan((i + 0.5) * CellU, (j + 0.5) * CellV);
    }

    /// <summary>Height at a plan point (clamped to the piece's length).</summary>
    public double ZAt(double x, double y)
    {
        if (SlopePerU == 0)
        {
            return Z0;
        }

        ToLocal(x, y, out double u, out _);
        return Z0 + (SlopePerU * Math.Clamp(u, 0, Nx * CellU));
    }
}

/// <summary>
/// A line agents can cross from one surface to another while moving: a piece joint or a floor end, seen from one
/// side. <see cref="Normal"/> points out of the owning surface into <see cref="Other"/>.
/// </summary>
public sealed class PortalSegment
{
    /// <summary>First end, plan.</summary>
    public required Point2 A { get; init; }

    /// <summary>Second end, plan.</summary>
    public required Point2 B { get; init; }

    /// <summary>Unit outward normal (into the other surface).</summary>
    public required Point2 Normal { get; init; }

    /// <summary>Surface on the far side.</summary>
    public required int Other { get; init; }

    /// <summary>True when crossing this way is allowed (one-way belts, D-107).</summary>
    public required bool Allowed { get; init; }

    /// <summary>Connector the segment belongs to.</summary>
    public required int Connector { get; init; }

    /// <summary>Index of the route-graph portal (served floor ends); −1 for joints and pass-over ends.</summary>
    public int Portal { get; init; } = -1;

    /// <summary>
    /// True for a floor end on an excluded intermediate level (D-093): only agents already travelling on the
    /// connector may cross it.
    /// </summary>
    public bool IsPassOver { get; init; }

    /// <summary>True when entering the connector here is metered by its step-loading cap (escalator entries).</summary>
    public bool IsCappedEntry { get; init; }

    /// <summary>Unit direction from <see cref="A"/> to <see cref="B"/>.</summary>
    public Point2 Direction => (B - A).Normalised();

    /// <summary>Segment length.</summary>
    public double Length => (B - A).Length;
}
