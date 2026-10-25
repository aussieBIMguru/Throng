using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Geometry;

/// <summary>What a <see cref="ConnectorPiece"/> is.</summary>
public enum ConnectorPieceKind
{
    /// <summary>A stair flight (risers and goings).</summary>
    Flight,

    /// <summary>A sloped ramp run.</summary>
    RampRun,

    /// <summary>An escalator's inclined section.</summary>
    Incline,

    /// <summary>An escalator's flat run-off at the top or bottom.</summary>
    Flat,

    /// <summary>An intermediate landing between flights or ramp runs.</summary>
    Landing,

    /// <summary>The half landing where a switchback turns.</summary>
    HalfLanding,

    /// <summary>A travelator's flat moving belt (D-090).</summary>
    Belt,
}

/// <summary>
/// One straight piece of a connector: an oriented rectangle in plan whose height rises linearly along its axis
/// (flat pieces have <see cref="Z0"/> = <see cref="Z1"/>). Heights are relative to the bottom level.
/// </summary>
/// <param name="Kind">What the piece is.</param>
/// <param name="Start">Plan centre of the piece's start edge.</param>
/// <param name="Axis">Unit plan direction of travel up the piece.</param>
/// <param name="LengthM">Plan length along <paramref name="Axis"/>.</param>
/// <param name="WidthM">Plan width across the axis.</param>
/// <param name="Z0">Height above the bottom level at the start edge.</param>
/// <param name="Z1">Height above the bottom level at the end edge.</param>
/// <param name="Risers">Riser count for stair flights; 0 otherwise.</param>
public sealed record ConnectorPiece(
    ConnectorPieceKind Kind, Point2 Start, Point2 Axis, double LengthM, double WidthM, double Z0, double Z1, int Risers)
{
    /// <summary>Unit vector 90° counter-clockwise from <see cref="Axis"/> (the piece's left).</summary>
    public Point2 Across => Axis.Perpendicular();

    /// <summary>Plan centre of the end edge.</summary>
    public Point2 End => Start + (Axis * LengthM);

    /// <summary>True when the piece rises along its length.</summary>
    public bool IsSloped => Z1 - Z0 > 1e-9;

    /// <summary>Plan point at station <paramref name="s"/> along the axis and offset <paramref name="t"/> across it.</summary>
    public Point2 At(double s, double t) => Start + (Axis * s) + (Across * t);

    /// <summary>Height at station <paramref name="s"/> (clamped to the piece).</summary>
    public double ZAt(double s) => LengthM <= 1e-12 ? Z1 : Z0 + ((Z1 - Z0) * Math.Clamp(s / LengthM, 0, 1));

    /// <summary>Station where the piece reaches height <paramref name="z"/> (clamped; flat pieces return 0 or the length).</summary>
    public double StationAt(double z)
    {
        if (!IsSloped)
        {
            return z <= Z0 ? 0 : LengthM;
        }

        return Math.Clamp((z - Z0) / (Z1 - Z0), 0, 1) * LengthM;
    }

    /// <summary>Counter-clockwise outline of the part of the piece between two stations.</summary>
    public Point2[] Outline(double s0, double s1)
    {
        double h = WidthM * 0.5;
        return [At(s0, -h), At(s1, -h), At(s1, h), At(s0, h)];
    }

    /// <summary>Counter-clockwise outline of the whole piece.</summary>
    public Point2[] Outline() => Outline(0, LengthM);
}

/// <summary>A point on a connector's walking centreline with its height above the bottom level.</summary>
/// <param name="Point">Plan position.</param>
/// <param name="Z">Height above the bottom level.</param>
public readonly record struct CentrelinePoint(Point2 Point, double Z);

/// <summary>
/// One storey's worth of a connector. A connector has one circuit, except a per-level switchback (D-091), which has
/// one per storey, all starting at the same bottom landing.
/// </summary>
/// <param name="Index">Circuit number from the bottom, 0-based.</param>
/// <param name="BottomLevelId">Level the circuit starts on, when known.</param>
/// <param name="TopLevelId">Level the circuit arrives at, when known.</param>
/// <param name="BaseZ">Height of the circuit's start above the connector's bottom level.</param>
/// <param name="RiseM">The circuit's own rise.</param>
/// <param name="Pieces">The circuit's pieces (heights relative to the connector's bottom level).</param>
/// <param name="Centreline">The circuit's walking centreline.</param>
/// <param name="Footprint">The circuit's plan footprint.</param>
/// <param name="TopLanding">Centre of the circuit's exit edge.</param>
/// <param name="TopAxis">Direction of travel off the circuit.</param>
public sealed record ConnectorCircuit(
    int Index,
    Guid? BottomLevelId,
    Guid? TopLevelId,
    double BaseZ,
    double RiseM,
    IReadOnlyList<ConnectorPiece> Pieces,
    IReadOnlyList<CentrelinePoint> Centreline,
    IReadOnlyList<Point2> Footprint,
    Point2 TopLanding,
    Point2 TopAxis);

/// <summary>
/// Derived geometry of a <see cref="Connector"/>: flights, landings and flats as <see cref="ConnectorPiece"/>s, the
/// plan footprint, the walking centreline and the top exit. Computed on demand from the connector and the real
/// level-to-level rise (D-054), never stored.
/// </summary>
/// <remarks>
/// Stairs (D-057, D-069): risers = round(rise ÷ riser), so the actual riser always fits the rise; a flight of n
/// risers has n − 1 goings in plan; more than the maximum risers per flight inserts landings (length = width,
/// at least <see cref="Connector.LandingM"/>). Ramps (D-072): run = rise ÷ gradient, split into runs of at most
/// <see cref="Connector.MaxRampRunM"/> with landings between. Escalators (D-071): flat run-off, incline, flat
/// run-off. Switchbacks (D-058): the first run, a half landing across both runs, and the return run alongside.
/// Per-level switchbacks (D-091, D-092): one switchback circuit per storey, each fitted to its own storey's rise,
/// stacked at the same bottom landing. Travelators (D-090): one flat belt of <see cref="Connector.RunLengthM"/>.
/// </remarks>
public sealed class ConnectorLayout
{
    private const double MinRiseM = 0.01;

    private ConnectorLayout()
    {
    }

    /// <summary>Pieces in travel order, bottom to top.</summary>
    public IReadOnlyList<ConnectorPiece> Pieces { get; private init; } = [];

    /// <summary>Total rise in metres.</summary>
    public double RiseM { get; private init; }

    /// <summary>Stair riser count (0 for escalators and ramps).</summary>
    public int RiserCount { get; private init; }

    /// <summary>Actual riser height in metres (stairs), rise ÷ riser count; the tallest circuit's for per-level stairs.</summary>
    public double ActualRiserM { get; private init; }

    /// <summary>Smallest actual riser among the circuits (equals <see cref="ActualRiserM"/> with one circuit).</summary>
    public double MinRiserM { get; private set; }

    /// <summary>Storey-by-storey circuits (one unless the connector uses per-level circuits, D-091).</summary>
    public IReadOnlyList<ConnectorCircuit> Circuits { get; private set; } = [];

    /// <summary>Number of sloped pieces (flights, ramp runs or the escalator incline).</summary>
    public int RunCount { get; private init; }

    /// <summary>Pitch of the steepest sloped piece in degrees.</summary>
    public double PitchDeg { get; private init; }

    /// <summary>Rise ÷ run of the steepest sloped piece.</summary>
    public double SteepestGradient { get; private init; }

    /// <summary>Overall plan length of the footprint along the run direction.</summary>
    public double LengthM { get; private init; }

    /// <summary>Overall plan width of the footprint.</summary>
    public double FootprintWidthM { get; private init; }

    /// <summary>Plan centre of the bottom entry edge.</summary>
    public Point2 BottomLanding { get; private init; }

    /// <summary>Unit direction of travel onto the connector at the bottom.</summary>
    public Point2 BottomAxis { get; private init; }

    /// <summary>Plan centre of the top exit edge.</summary>
    public Point2 TopLanding { get; private init; }

    /// <summary>Unit direction of travel off the connector at the top.</summary>
    public Point2 TopAxis { get; private init; }

    /// <summary>Centre of the footprint's far end along the run (where the rotate grip sits, D-063).</summary>
    public Point2 FarEnd { get; private init; }

    /// <summary>Counter-clockwise plan footprint (an oriented rectangle).</summary>
    public IReadOnlyList<Point2> Footprint { get; private init; } = [];

    /// <summary>Walking centreline from bottom to top, with heights.</summary>
    public IReadOnlyList<CentrelinePoint> Centreline { get; private init; } = [];

    /// <summary>Computes the layout using the study's level elevations.</summary>
    public static Result<ConnectorLayout> Compute(Connector connector, Study study)
    {
        Level? bottom = study.FindLevel(connector.BottomLevelId);
        Level? top = study.FindLevel(connector.TopLevelId);
        if (bottom is null || top is null)
        {
            return Result<ConnectorLayout>.Fail($"'{connector.Name}' links a level that doesn't exist.");
        }

        if (connector.IsTravelator && bottom.Id != top.Id)
        {
            return Result<ConnectorLayout>.Fail($"Travelator '{connector.Name}' must start and end on the same level.");
        }

        if (connector.UsesCircuits)
        {
            List<Level> storeys = [.. study.LevelsBetween(bottom, top)];
            if (storeys.Count >= 3)
            {
                return BuildCircuits(connector, storeys);
            }
        }

        Result<ConnectorLayout> single = Compute(connector, top.ElevationM - bottom.ElevationM);
        if (single.IsSuccess && single.Value is { } layout)
        {
            layout.Circuits = [layout.Circuits[0] with { BottomLevelId = bottom.Id, TopLevelId = top.Id }];
        }

        return single;
    }

    /// <summary>Computes a single-circuit layout for a given rise in metres (travelators ignore the rise).</summary>
    public static Result<ConnectorLayout> Compute(Connector connector, double riseM)
    {
        string? problem = CheckInputs(connector, riseM);
        if (problem is not null)
        {
            return Result<ConnectorLayout>.Fail(problem);
        }

        if (connector.IsTravelator)
        {
            return BuildTravelator(connector);
        }

        return connector.IsSwitchback ? BuildSwitchback(connector, riseM) : BuildStraight(connector, riseM);
    }

    /// <summary>
    /// One switchback circuit per storey (D-091, D-092), each fitted to its storey's rise and lifted to its
    /// storey's height, all starting at the bottom landing.
    /// </summary>
    private static Result<ConnectorLayout> BuildCircuits(Connector c, List<Level> storeys)
    {
        var circuits = new List<ConnectorCircuit>();
        var pieces = new List<ConnectorPiece>();
        var centreline = new List<CentrelinePoint>();
        ConnectorLayout? longest = null;
        int risers = 0, runs = 0;
        double maxRiser = 0, minRiser = double.MaxValue, pitch = 0, steepest = 0;
        double baseElevation = storeys[0].ElevationM;

        for (int i = 0; i + 1 < storeys.Count; i++)
        {
            double rise = storeys[i + 1].ElevationM - storeys[i].ElevationM;
            Result<ConnectorLayout> one = Compute(c, rise);
            if (!one.IsSuccess || one.Value is not { } l)
            {
                return Result<ConnectorLayout>.Fail($"'{c.Name}', {storeys[i].Name} → {storeys[i + 1].Name}: {one.Error}");
            }

            double baseZ = storeys[i].ElevationM - baseElevation;
            List<ConnectorPiece> lifted = [.. l.Pieces.Select(p => p with { Z0 = p.Z0 + baseZ, Z1 = p.Z1 + baseZ })];
            List<CentrelinePoint> path = [.. l.Centreline.Select(p => p with { Z = p.Z + baseZ })];
            circuits.Add(new ConnectorCircuit(i, storeys[i].Id, storeys[i + 1].Id, baseZ, rise, lifted, path, l.Footprint, l.TopLanding, l.TopAxis));
            pieces.AddRange(lifted);
            centreline.AddRange(path);

            risers += l.RiserCount;
            runs += l.RunCount;
            maxRiser = Math.Max(maxRiser, l.ActualRiserM);
            minRiser = Math.Min(minRiser, l.MinRiserM);
            pitch = Math.Max(pitch, l.PitchDeg);
            steepest = Math.Max(steepest, l.SteepestGradient);
            if (longest is null || l.LengthM > longest.LengthM)
            {
                longest = l;
            }
        }

        // Every circuit starts at the same landing with the same width, so the longest one covers them all.
        return Result<ConnectorLayout>.Ok(new ConnectorLayout
        {
            Pieces = pieces,
            RiseM = storeys[^1].ElevationM - baseElevation,
            RiserCount = risers,
            ActualRiserM = maxRiser,
            MinRiserM = minRiser == double.MaxValue ? maxRiser : minRiser,
            RunCount = runs,
            PitchDeg = pitch,
            SteepestGradient = steepest,
            LengthM = longest!.LengthM,
            FootprintWidthM = longest.FootprintWidthM,
            BottomLanding = longest.BottomLanding,
            BottomAxis = longest.BottomAxis,
            TopLanding = circuits[^1].TopLanding,
            TopAxis = circuits[^1].TopAxis,
            FarEnd = longest.FarEnd,
            Footprint = longest.Footprint,
            Centreline = centreline,
            Circuits = circuits,
        });
    }

    /// <summary>A travelator: one flat belt from the start edge, <see cref="Connector.RunLengthM"/> long (D-090).</summary>
    private static Result<ConnectorLayout> BuildTravelator(Connector c)
    {
        var frame = new Frame(c.BottomLanding, c.Axis());
        double length = c.RunLengthM;
        var pieces = new List<ConnectorPiece> { new(ConnectorPieceKind.Belt, c.BottomLanding, frame.U, length, c.WidthM, 0, 0, 0) };
        var centreline = new List<CentrelinePoint> { new(c.BottomLanding, 0), new(frame.ToPlan(length, 0), 0) };
        double h = c.WidthM * 0.5;
        return Result<ConnectorLayout>.Ok(Finish(c, 0, 0, 0, pieces, centreline, frame,
            uMin: 0, uMax: length, vMin: -h, vMax: h, top: frame.ToPlan(length, 0), topAxis: frame.U));
    }

    /// <summary>One-line description of the derived geometry, e.g. "29 risers × 172 mm · 2 flights · 11.16 m long".</summary>
    public string Describe(ConnectorKind kind)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string length = LengthM.ToString("0.00", c) + " m long";
        if (Circuits.Count > 1)
        {
            string detail = kind == ConnectorKind.Stair
                ? string.Create(c, $"{RiserCount} risers, {MinRiserM * 1000:0}–{ActualRiserM * 1000:0} mm")
                : string.Create(c, $"1:{(SteepestGradient > 0 ? 1 / SteepestGradient : 0):0.#}");
            return string.Create(c, $"{Circuits.Count} circuits (one per level) · {detail} · {length}");
        }

        return kind switch
        {
            ConnectorKind.Travelator => string.Create(c, $"{LengthM:0.0} m belt"),
            ConnectorKind.Stair => string.Create(c, $"{RiserCount} risers × {ActualRiserM * 1000:0} mm · {RunCount} flight{(RunCount == 1 ? string.Empty : "s")} · {length}"),
            ConnectorKind.Ramp => string.Create(c, $"1:{(SteepestGradient > 0 ? 1 / SteepestGradient : 0):0.#} · {RunCount} run{(RunCount == 1 ? string.Empty : "s")} · {length}"),
            _ => string.Create(c, $"{PitchDeg:0.#}° · {length}"),
        };
    }

    private static string? CheckInputs(Connector c, double riseM)
    {
        if (c.IsTravelator)
        {
            return c.RunLengthM >= 1 && c.WidthM > 0.1 ? null : "A travelator needs a belt at least 1 m long and wider than 0.1 m.";
        }

        if (!(riseM > MinRiseM))
        {
            return "The top level must be above the bottom level.";
        }

        if (!(c.WidthM > 0.1))
        {
            return "Width must be more than 0.1 m.";
        }

        switch (c.Kind)
        {
            case ConnectorKind.Stair when !(c.RiserM > 0.05) || !(c.GoingM > 0.05):
                return "Riser and going must both be more than 50 mm.";
            case ConnectorKind.Ramp when !(c.Gradient > 0.005) || c.Gradient > 1 || !(c.MaxRampRunM > 0.5):
                return "Ramp gradient must be between 1:200 and 1:1, with runs longer than 0.5 m.";
            case ConnectorKind.Escalator when !(c.InclineDeg >= 5) || c.InclineDeg > 60:
                return "Escalator incline must be between 5° and 60°.";
        }

        if (c.IsSwitchback && c.Kind == ConnectorKind.Stair && RiserCountFor(c, riseM) < 2)
        {
            return "Too low for a switchback: it needs at least two risers.";
        }

        return null;
    }

    private static int RiserCountFor(Connector c, double riseM) =>
        Math.Max(1, (int)Math.Round(riseM / c.RiserM, MidpointRounding.AwayFromZero));

    // ------------------------------------------------------------------ 1D run segments

    /// <summary>A piece before it is placed in plan: kind, length along the run, rise, riser count.</summary>
    private readonly record struct Segment(ConnectorPieceKind Kind, double Length, double Rise, int Risers);

    /// <summary>Stair flights for <paramref name="risers"/> risers, split at the max per flight with landings between.</summary>
    private static List<Segment> StairSegments(Connector c, int risers, double riser)
    {
        var segments = new List<Segment>();
        if (risers <= 0)
        {
            return segments;
        }

        int max = Math.Max(1, c.MaxRisersPerFlight);
        int flights = (risers + max - 1) / max;
        int perFlight = risers / flights;
        int extra = risers % flights;
        double landing = Math.Max(c.WidthM, c.LandingM);

        for (int f = 0; f < flights; f++)
        {
            int r = perFlight + (f < extra ? 1 : 0);

            // n risers have n − 1 goings between the bottom floor and the top landing; a single step still
            // gets one going so the piece never collapses to zero length.
            double length = Math.Max(r - 1, 1) * c.GoingM;
            segments.Add(new Segment(ConnectorPieceKind.Flight, length, r * riser, r));
            if (f < flights - 1)
            {
                segments.Add(new Segment(ConnectorPieceKind.Landing, landing, 0, 0));
            }
        }

        return segments;
    }

    /// <summary>Ramp runs for <paramref name="rise"/>, each at most the max run, with landings between (D-072).</summary>
    private static List<Segment> RampSegments(Connector c, double rise)
    {
        var segments = new List<Segment>();
        double run = rise / c.Gradient;
        int runs = Math.Max(1, (int)Math.Ceiling((run / c.MaxRampRunM) - 1e-9));
        for (int i = 0; i < runs; i++)
        {
            segments.Add(new Segment(ConnectorPieceKind.RampRun, run / runs, rise / runs, 0));
            if (i < runs - 1)
            {
                segments.Add(new Segment(ConnectorPieceKind.Landing, c.LandingM, 0, 0));
            }
        }

        return segments;
    }

    /// <summary>Escalator: flat run-off, incline, flat run-off (D-071).</summary>
    private static List<Segment> EscalatorSegments(Connector c, double rise)
    {
        var segments = new List<Segment>();
        double incline = rise / Math.Tan(c.InclineDeg * Math.PI / 180.0);
        if (c.FlatLengthM > 1e-6)
        {
            segments.Add(new Segment(ConnectorPieceKind.Flat, c.FlatLengthM, 0, 0));
        }

        segments.Add(new Segment(ConnectorPieceKind.Incline, incline, rise, 0));
        if (c.FlatLengthM > 1e-6)
        {
            segments.Add(new Segment(ConnectorPieceKind.Flat, c.FlatLengthM, 0, 0));
        }

        return segments;
    }

    // ------------------------------------------------------------------ plan placement

    /// <summary>Maps connector-local (u along the run, v to the left) to plan.</summary>
    private readonly record struct Frame(Point2 Origin, Point2 U)
    {
        public Point2 V => U.Perpendicular();

        public Point2 ToPlan(double u, double v) => Origin + (U * u) + (V * v);
    }

    /// <summary>Lays segments along u (direction <paramref name="sign"/>) from (u0, v), appending pieces and centreline points.</summary>
    private static double PlaceLane(
        Frame frame, IReadOnlyList<Segment> segments, double u0, double v, int sign, double z0, double width,
        List<ConnectorPiece> pieces, List<CentrelinePoint> centreline, out double zEnd)
    {
        double u = u0;
        double z = z0;
        Point2 axis = frame.U * sign;
        foreach (Segment segment in segments)
        {
            pieces.Add(new ConnectorPiece(segment.Kind, frame.ToPlan(u, v), axis, segment.Length, width, z, z + segment.Rise, segment.Risers));
            u += sign * segment.Length;
            z += segment.Rise;
            centreline.Add(new CentrelinePoint(frame.ToPlan(u, v), z));
        }

        zEnd = z;
        return u;
    }

    private static List<Segment> LaneSegments(Connector c, double rise, int risers, double riser) => c.Kind switch
    {
        ConnectorKind.Stair => StairSegments(c, risers, riser),
        ConnectorKind.Ramp => RampSegments(c, rise),
        _ => EscalatorSegments(c, rise),
    };

    private static Result<ConnectorLayout> BuildStraight(Connector c, double rise)
    {
        int risers = c.Kind == ConnectorKind.Stair ? RiserCountFor(c, rise) : 0;
        double riser = risers > 0 ? rise / risers : 0;
        var frame = new Frame(c.BottomLanding, c.Axis());
        var pieces = new List<ConnectorPiece>();
        var centreline = new List<CentrelinePoint> { new(c.BottomLanding, 0) };

        double length = PlaceLane(frame, LaneSegments(c, rise, risers, riser), 0, 0, +1, 0, c.WidthM, pieces, centreline, out _);
        double h = c.WidthM * 0.5;
        return Result<ConnectorLayout>.Ok(Finish(c, rise, risers, riser, pieces, centreline, frame,
            uMin: 0, uMax: length, vMin: -h, vMax: h, top: frame.ToPlan(length, 0), topAxis: frame.U));
    }

    private static Result<ConnectorLayout> BuildSwitchback(Connector c, double rise)
    {
        int risers = c.Kind == ConnectorKind.Stair ? RiserCountFor(c, rise) : 0;
        double riser = risers > 0 ? rise / risers : 0;
        int risers1 = (risers + 1) / 2;
        int risers2 = risers - risers1;
        double rise1 = c.Kind == ConnectorKind.Stair ? risers1 * riser : rise * 0.5;
        double rise2 = rise - rise1;

        var frame = new Frame(c.BottomLanding, c.Axis());
        var pieces = new List<ConnectorPiece>();
        var centreline = new List<CentrelinePoint> { new(c.BottomLanding, 0) };
        double w = c.WidthM;
        double side = c.ReturnSide == TurnSide.Left ? 1 : -1;
        double offset = side * (w + Math.Max(0, c.SwitchbackGapM));

        // First run up from the bottom landing.
        double len1 = PlaceLane(frame, LaneSegments(c, rise1, risers1, riser), 0, 0, +1, 0, w, pieces, centreline, out double zTurn);

        // Half landing across both runs; the centreline turns through its middle.
        double turn = Math.Max(w, c.LandingM);
        pieces.Add(new ConnectorPiece(ConnectorPieceKind.HalfLanding, frame.ToPlan(len1, offset * 0.5), frame.U, turn, w + Math.Abs(offset), zTurn, zTurn, 0));
        centreline.Add(new CentrelinePoint(frame.ToPlan(len1 + (turn * 0.5), 0), zTurn));
        centreline.Add(new CentrelinePoint(frame.ToPlan(len1 + (turn * 0.5), offset), zTurn));
        centreline.Add(new CentrelinePoint(frame.ToPlan(len1, offset), zTurn));

        // Return run back alongside the first.
        double uTop = PlaceLane(frame, LaneSegments(c, rise2, risers2, riser), len1, offset, -1, zTurn, w, pieces, centreline, out _);

        double h = w * 0.5;
        return Result<ConnectorLayout>.Ok(Finish(c, rise, risers, riser, pieces, centreline, frame,
            uMin: Math.Min(0, uTop), uMax: len1 + turn, vMin: Math.Min(-h, offset - h), vMax: Math.Max(h, offset + h),
            top: frame.ToPlan(uTop, offset), topAxis: frame.U * -1));
    }

    private static ConnectorLayout Finish(
        Connector c, double rise, int risers, double riser, List<ConnectorPiece> pieces, List<CentrelinePoint> centreline, Frame frame,
        double uMin, double uMax, double vMin, double vMax, Point2 top, Point2 topAxis)
    {
        double steepest = 0;
        int runs = 0;
        foreach (ConnectorPiece piece in pieces)
        {
            if (piece.IsSloped && piece.LengthM > 1e-9)
            {
                runs++;
                steepest = Math.Max(steepest, (piece.Z1 - piece.Z0) / piece.LengthM);
            }
        }

        var layout = new ConnectorLayout
        {
            Pieces = pieces,
            RiseM = rise,
            RiserCount = risers,
            ActualRiserM = riser,
            RunCount = runs,
            PitchDeg = Math.Atan(steepest) * 180.0 / Math.PI,
            SteepestGradient = steepest,
            LengthM = uMax - uMin,
            FootprintWidthM = vMax - vMin,
            BottomLanding = c.BottomLanding,
            BottomAxis = frame.U,
            TopLanding = top,
            TopAxis = topAxis,
            FarEnd = frame.ToPlan(uMax, (vMin + vMax) * 0.5),
            Footprint = [frame.ToPlan(uMin, vMin), frame.ToPlan(uMax, vMin), frame.ToPlan(uMax, vMax), frame.ToPlan(uMin, vMax)],
            Centreline = centreline,
        };
        layout.MinRiserM = riser;
        layout.Circuits = [new ConnectorCircuit(0, null, null, 0, rise, pieces, centreline, layout.Footprint, top, topAxis)];
        return layout;
    }
}
