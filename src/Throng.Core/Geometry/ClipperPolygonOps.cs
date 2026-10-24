using Clipper2Lib;

namespace Throng.Core.Geometry;

/// <summary>
/// <see cref="IPolygonOps"/> backed by Clipper2 (Boost Software License 1.0, D-007). Closed inputs are normalised
/// to counter-clockwise winding first, so hand-sketched shapes of either winding union correctly under the
/// non-zero fill rule. Outputs follow the interface contract: outers counter-clockwise, holes clockwise.
/// </summary>
public sealed class ClipperPolygonOps : IPolygonOps
{
    /// <summary>Decimal places kept by Clipper's integer conversion (4 = 0.1 mm).</summary>
    private const int Precision = 4;

    /// <summary>Mitre limit for offsets and wall corners, as a multiple of the offset distance.</summary>
    private const double MiterLimit = 2.0;

    /// <summary>A shared instance; the class is stateless.</summary>
    public static ClipperPolygonOps Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Point2>> Union(IEnumerable<IReadOnlyList<Point2>> polygons)
    {
        PathsD subject = ToClosedPaths(polygons);
        return subject.Count == 0 ? [] : FromPaths(UnionD(subject));
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Point2>> Difference(IEnumerable<IReadOnlyList<Point2>> subjects, IEnumerable<IReadOnlyList<Point2>> clips)
    {
        PathsD subject = ToClosedPaths(subjects);
        if (subject.Count == 0)
        {
            return [];
        }

        PathsD clip = ToClosedPaths(clips);
        PathsD result = clip.Count == 0
            ? UnionD(subject)
            : Clipper.Difference(subject, clip, FillRule.NonZero, Precision);
        return FromPaths(result);
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Point2>> Intersection(IEnumerable<IReadOnlyList<Point2>> subjects, IEnumerable<IReadOnlyList<Point2>> clips)
    {
        PathsD subject = ToClosedPaths(subjects);
        PathsD clip = ToClosedPaths(clips);
        return subject.Count == 0 || clip.Count == 0
            ? []
            : FromPaths(Clipper.Intersect(subject, clip, FillRule.NonZero, Precision));
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Point2>> Offset(IEnumerable<IReadOnlyList<Point2>> polygons, double delta)
    {
        PathsD subject = ToClosedPaths(polygons);
        if (subject.Count == 0)
        {
            return [];
        }

        // Union first so overlapping inputs offset as one shape.
        PathsD merged = UnionD(subject);
        return FromPaths(Clipper.InflatePaths(merged, delta, JoinType.Miter, EndType.Polygon, MiterLimit, Precision));
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Point2>> ThickenPolyline(IReadOnlyList<Point2> polyline, double thickness)
    {
        if (polyline.Count < 2 || !(thickness > 0))
        {
            return [];
        }

        PathsD open = [ToPath(polyline)];
        PathsD thick = Clipper.InflatePaths(open, thickness * 0.5, JoinType.Miter, EndType.Butt, MiterLimit, Precision);

        // Union so a wall that crosses itself becomes one clean outline.
        return FromPaths(UnionD(thick));
    }

    /// <summary>
    /// Non-zero union of one set of paths at <see cref="Precision"/>. Clipper2's single-set PathsD overload has no
    /// precision parameter, so this uses the subject + clip overload (the same shape as Difference/Intersect)
    /// with an empty clip set.
    /// </summary>
    private static PathsD UnionD(PathsD subject) => Clipper.Union(subject, new PathsD(), FillRule.NonZero, Precision);

    /// <summary>Converts closed polygons to Clipper paths, dropping degenerate ones and forcing CCW winding.</summary>
    private static PathsD ToClosedPaths(IEnumerable<IReadOnlyList<Point2>> polygons)
    {
        var paths = new PathsD();
        foreach (IReadOnlyList<Point2> polygon in polygons)
        {
            if (polygon.Count < 3)
            {
                continue;
            }

            PathD path = ToPath(polygon);
            if (PolygonMath.SignedArea(polygon) < 0)
            {
                path.Reverse();
            }

            paths.Add(path);
        }

        return paths;
    }

    private static PathD ToPath(IReadOnlyList<Point2> points)
    {
        var path = new PathD(points.Count);
        foreach (Point2 p in points)
        {
            path.Add(new PointD(p.X, p.Y));
        }

        return path;
    }

    private static IReadOnlyList<IReadOnlyList<Point2>> FromPaths(PathsD paths)
    {
        var result = new List<IReadOnlyList<Point2>>(paths.Count);
        foreach (PathD path in paths)
        {
            if (path.Count < 3)
            {
                continue;
            }

            var points = new Point2[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                points[i] = new Point2(path[i].x, path[i].y);
            }

            result.Add(points);
        }

        return result;
    }
}
