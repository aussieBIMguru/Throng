using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.World;

/// <summary>
/// Where a shaft's walls are open for its lift's landing doors (round 015, D-261): one opening per car door, at every
/// stop and side with a landing (not Closed, and not a Shuttle's middle stops, as in the navigation world), from the
/// stop's floor up to the door height. Derived on demand, never stored.
/// </summary>
public static class LiftOpenings
{
    /// <summary>The landing door openings in <paramref name="shaft"/>'s walls; empty without a lift or a valid layout.</summary>
    public static List<WallOpening> For(Study study, Shaft shaft)
    {
        var openings = new List<WallOpening>();
        if (shaft.Lift is not { } lift)
        {
            return openings;
        }

        Result<LiftLayout> computed = LiftLayout.Compute(shaft.Outline, lift.FrontEdge, lift.Cars, lift.LandingDepthM);
        if (!computed.IsSuccess || computed.Value is not { } layout)
        {
            return openings;
        }

        IReadOnlyList<Level> stops = study.LiftStops(shaft);
        if (stops.Count < 2)
        {
            return openings;
        }

        double doorHeight = LiftLayout.CarHeightFor([.. stops.Select(s => s.ElevationM)]) * LiftLayout.DoorHeightShare;
        bool shuttle = lift.Dispatch == LiftDispatch.Shuttle;
        for (int stop = 0; stop < stops.Count; stop++)
        {
            if (shuttle && stop != 0 && stop != stops.Count - 1)
            {
                continue;
            }

            Level level = stops[stop];
            LiftLevelDoors doors = lift.DoorsAt(level.Id);
            for (int side = 0; side < 2; side++)
            {
                LiftDoorUse use = side == 0 ? doors.Front : doors.Rear;
                if (use == LiftDoorUse.Closed || layout.Side(side) is null)
                {
                    continue;
                }

                foreach (LiftCarLayout car in layout.Cars)
                {
                    if (car.Door(side) is { } door)
                    {
                        openings.Add(new WallOpening(door.A, door.B, level.ElevationM, level.ElevationM + doorHeight));
                    }
                }
            }
        }

        return openings;
    }
}
