using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// A centre-opening sliding door to draw in 3D (round 014, D-258): a car door or a landing door in the shaft wall.
/// Two leaves meet in the middle when shut and slide apart along the opening as it opens.
/// </summary>
/// <param name="A">One end of the opening in plan.</param>
/// <param name="B">The other end.</param>
/// <param name="Offset">Plan offset of the leaves from the opening line (inside the car, or out on the landing side), metres.</param>
/// <param name="FloorZ">Height of the sill, metres.</param>
/// <param name="HeightM">Leaf height, metres.</param>
/// <param name="OpenShare">How far open, 0 (shut) to 1 (open).</param>
/// <param name="IsLanding">True for a landing door (shaft wall), false for a car door.</param>
public sealed record LiftDoorDraw(Point2 A, Point2 B, Point2 Offset, double FloorZ, double HeightM, double OpenShare, bool IsLanding);
