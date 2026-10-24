namespace Throng.Core.Geometry;

/// <summary>
/// Polygon boolean and offset operations. Implemented with Clipper2 from M1 (D-007) and kept behind this
/// interface so the library can be swapped without touching callers.
/// </summary>
/// <remarks>
/// Results are lists of closed paths. Outer boundaries wind counter-clockwise and holes wind clockwise.
/// </remarks>
public interface IPolygonOps
{
    /// <summary>Union of all input polygons.</summary>
    IReadOnlyList<IReadOnlyList<Point2>> Union(IEnumerable<IReadOnlyList<Point2>> polygons);

    /// <summary>Subjects minus the union of clips.</summary>
    IReadOnlyList<IReadOnlyList<Point2>> Difference(IEnumerable<IReadOnlyList<Point2>> subjects, IEnumerable<IReadOnlyList<Point2>> clips);

    /// <summary>Intersection of subjects with the union of clips.</summary>
    IReadOnlyList<IReadOnlyList<Point2>> Intersection(IEnumerable<IReadOnlyList<Point2>> subjects, IEnumerable<IReadOnlyList<Point2>> clips);

    /// <summary>Grows (positive) or shrinks (negative) polygons by <paramref name="delta"/> metres.</summary>
    IReadOnlyList<IReadOnlyList<Point2>> Offset(IEnumerable<IReadOnlyList<Point2>> polygons, double delta);

    /// <summary>Turns an open polyline into a closed outline of the given total thickness (walls).</summary>
    IReadOnlyList<IReadOnlyList<Point2>> ThickenPolyline(IReadOnlyList<Point2> polyline, double thickness);
}
