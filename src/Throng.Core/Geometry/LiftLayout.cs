using Throng.Core.Common;

namespace Throng.Core.Geometry;

/// <summary>
/// Plan layout of a lift bank in its shaft (round 013, D-237, D-241, D-242): the front edge (picked per lift) and
/// the rear edge most nearly opposite it, the cars side by side along the front, a door per car on each side, and the
/// landing rectangle in front of each side. Shared by the simulation and the plan symbols.
/// </summary>
public sealed class LiftLayout
{
    /// <summary>Widest door per car, metres.</summary>
    public const double MaxDoorWidthM = 1.1;

    /// <summary>Share of a car's width its door takes when the car is narrower than that allows.</summary>
    public const double DoorShare = 0.8;

    /// <summary>A rear edge must face within this of straight back (cosine of the angle between the edges' directions).</summary>
    public const double RearFacingCos = -0.5;

    /// <summary>Car inside height, metres, where the storeys allow it (round 013b).</summary>
    public const double CarHeightM = 2.4;

    /// <summary>Door opening height as a share of the car height (round 014, D-258; shaft walls, D-261).</summary>
    public const double DoorHeightShare = 0.88;

    /// <summary>
    /// Car inside height for a bank with stops at <paramref name="stopZ"/> (lowest first): <see cref="CarHeightM"/>,
    /// or 0.4 m under the closest pair of stops, but never below 1.8 m.
    /// </summary>
    public static double CarHeightFor(IReadOnlyList<double> stopZ)
    {
        double gap = double.PositiveInfinity;
        for (int s = 1; s < stopZ.Count; s++)
        {
            gap = Math.Min(gap, stopZ[s] - stopZ[s - 1]);
        }

        return double.IsFinite(gap) ? Math.Clamp(gap - 0.4, 1.8, CarHeightM) : CarHeightM;
    }

    /// <summary>Front edge, as drawn.</summary>
    public required LiftSide Front { get; init; }

    /// <summary>Rear edge, or null when no edge faces the front.</summary>
    public LiftSide? Rear { get; init; }

    /// <summary>Cars, left to right along the front edge (A → B).</summary>
    public required IReadOnlyList<LiftCarLayout> Cars { get; init; }

    /// <summary>The side for a door index (0 front, 1 rear), or null.</summary>
    public LiftSide? Side(int side) => side == 0 ? Front : Rear;

    /// <summary>Lays out <paramref name="cars"/> cars on edge <paramref name="frontEdge"/> of a shaft outline.</summary>
    public static Result<LiftLayout> Compute(IReadOnlyList<Point2> outline, int frontEdge, int cars, double landingDepthM)
    {
        int n = outline.Count;
        if (n < 3)
        {
            return Result<LiftLayout>.Fail("The shaft outline needs at least three corners.");
        }

        cars = Math.Clamp(cars, 1, 4);
        frontEdge = ((frontEdge % n) + n) % n;
        double depth = Math.Clamp(double.IsFinite(landingDepthM) ? landingDepthM : 2.5, 0.5, 20);
        Point2 centroid = PolygonMath.Centroid(outline);
        Point2 fa = outline[frontEdge], fb = outline[(frontEdge + 1) % n];
        double frontLength = (fb - fa).Length;
        if (frontLength < 0.3)
        {
            return Result<LiftLayout>.Fail("The lift's front edge is too short for a door.");
        }

        Point2 along = (fb - fa) * (1 / frontLength);
        Point2 outward = OutwardNormal(fa, fb, centroid);
        Point2 inward = outward * -1;

        // Rear: the other edge whose direction is most nearly opposite the front's.
        int rearEdge = -1;
        double bestCos = RearFacingCos;
        for (int e = 0; e < n; e++)
        {
            Point2 a = outline[e], b = outline[(e + 1) % n];
            double length = (b - a).Length;
            if (e == frontEdge || length < 0.3)
            {
                continue;
            }

            double cos = Point2.Dot((b - a) * (1 / length), along);
            if (cos < bestCos)
            {
                bestCos = cos;
                rearEdge = e;
            }
        }

        // Car depth: from the front edge to the farthest corner inwards.
        double carDepth = 0;
        foreach (Point2 p in outline)
        {
            carDepth = Math.Max(carDepth, Point2.Dot(p - fa, inward));
        }

        carDepth = Math.Max(0.5, carDepth);
        double carWidth = frontLength / cars;
        double doorWidth = Math.Min(MaxDoorWidthM, carWidth * DoorShare);
        LiftSide front = MakeSide(fa, fb, outward, depth);
        LiftSide? rear = null;
        if (rearEdge >= 0)
        {
            Point2 ra = outline[rearEdge], rb = outline[(rearEdge + 1) % n];
            rear = MakeSide(ra, rb, OutwardNormal(ra, rb, centroid), depth);
        }

        var layouts = new List<LiftCarLayout>(cars);
        for (int c = 0; c < cars; c++)
        {
            Point2 mid = fa + (along * ((c + 0.5) * carWidth));
            Point2 centre = mid + (inward * (carDepth / 2));
            (Point2 A, Point2 B) frontDoor = (mid - (along * (doorWidth / 2)), mid + (along * (doorWidth / 2)));
            (Point2 A, Point2 B)? rearDoor = null;
            if (rear is not null)
            {
                // The rear door faces the car's front door, clamped onto the rear edge.
                Point2 rAlong = (rear.B - rear.A) * (1 / rear.Length);
                double t = Math.Clamp(Point2.Dot(centre - rear.A, rAlong), doorWidth / 2, Math.Max(doorWidth / 2, rear.Length - (doorWidth / 2)));
                Point2 rm = rear.A + (rAlong * t);
                rearDoor = (rm - (rAlong * (doorWidth / 2)), rm + (rAlong * (doorWidth / 2)));
            }

            Point2 hw = along * (carWidth / 2), hd = inward * (carDepth / 2);
            layouts.Add(new LiftCarLayout
            {
                Centre = centre,
                Along = along,
                Inward = inward,
                HalfWidth = carWidth / 2,
                HalfDepth = carDepth / 2,
                FrontDoor = frontDoor,
                RearDoor = rearDoor,
                Footprint = [centre - hw - hd, centre + hw - hd, centre + hw + hd, centre - hw + hd],
            });
        }

        return Result<LiftLayout>.Ok(new LiftLayout { Front = front, Rear = rear, Cars = layouts });
    }

    private static LiftSide MakeSide(Point2 a, Point2 b, Point2 outward, double depth) => new()
    {
        A = a,
        B = b,
        Outward = outward,
        Landing = [a, b, b + (outward * depth), a + (outward * depth)],
    };

    /// <summary>The unit normal of edge a→b that points away from <paramref name="centroid"/>.</summary>
    private static Point2 OutwardNormal(Point2 a, Point2 b, Point2 centroid)
    {
        Point2 d = b - a;
        double length = Math.Max(1e-9, d.Length);
        var normal = new Point2(d.Y / length, -d.X / length);
        Point2 mid = (a + b) * 0.5;
        return Point2.Dot(mid - centroid, normal) >= 0 ? normal : normal * -1;
    }
}
