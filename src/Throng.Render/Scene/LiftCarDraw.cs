using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// One lift car to draw in 3D while a run is on (round 013b, D-240, D-245): a box over its footprint from the car's
/// floor up <see cref="HeightM"/>, with translucent walls so its riders show inside, and its door openings marked
/// (lit while open).
/// </summary>
/// <param name="Footprint">The car's plan outline in the shaft.</param>
/// <param name="FloorZ">Height of the car floor, metres (riders stand on it).</param>
/// <param name="HeightM">Inside height of the car, metres.</param>
/// <param name="Doors">Door openings on the car's sides (front and, for through cars, rear).</param>
/// <param name="DoorsOpen">True while the doors are opening or open.</param>
public sealed record LiftCarDraw(IReadOnlyList<Point2> Footprint, double FloorZ, double HeightM, IReadOnlyList<(Point2 A, Point2 B)> Doors, bool DoorsOpen);
