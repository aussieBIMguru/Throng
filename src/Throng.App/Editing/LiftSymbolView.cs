using Throng.Core.Geometry;

namespace Throng.App.Editing;

/// <summary>
/// A lift bank drawn in the plan on one level (round 013, D-240): car outlines in the shaft, the doors used at this
/// level, and their landings (D-241).
/// </summary>
/// <param name="Cars">Car footprints.</param>
/// <param name="Doors">Door openings in use at this level.</param>
/// <param name="Landings">Landing rectangles in front of the sides with doors at this level.</param>
/// <param name="Name">Lift name, shown by the cars.</param>
/// <param name="LabelAt">Where the name goes (the shaft's middle).</param>
public sealed record LiftSymbolView(
    IReadOnlyList<IReadOnlyList<Point2>> Cars, IReadOnlyList<(Point2 A, Point2 B)> Doors, IReadOnlyList<IReadOnlyList<Point2>> Landings, string Name, Point2 LabelAt);
