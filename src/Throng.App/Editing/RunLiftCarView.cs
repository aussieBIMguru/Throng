using Throng.Core.Geometry;

namespace Throng.App.Editing;

/// <summary>A lift car while running (round 013, D-240): lit when it's at this level, with its direction and riders.</summary>
/// <param name="Footprint">The car's outline in the shaft.</param>
/// <param name="Here">True when the car is at this level.</param>
/// <param name="DoorsOpen">True while its doors are opening or open here.</param>
/// <param name="Label">Direction and riders, e.g. "▲ 5".</param>
public sealed record RunLiftCarView(IReadOnlyList<Point2> Footprint, bool Here, bool DoorsOpen, string Label);
