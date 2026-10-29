using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Sim;

/// <summary>
/// A lift bank ready to run (round 013, D-236–D-244): its preset values, its stops (the shaft's served levels, lowest
/// first) with the landing portal per door side, the cars' plan layout, the lobby, and the dispatch with its
/// time-of-day switches.
/// </summary>
public sealed class SimLift
{
    /// <summary>Position in <see cref="SimModel.Lifts"/>.</summary>
    public required int Index { get; init; }

    /// <summary>The shaft.</summary>
    public required Guid ShaftId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Its connector in the navigation world.</summary>
    public required int Connector { get; init; }

    /// <summary>Index of its first car in the engine's car arrays.</summary>
    public required int FirstCar { get; init; }

    /// <summary>Cars in the bank.</summary>
    public int Cars => CarLayouts.Count;

    /// <summary>Plan layout of each car.</summary>
    public required IReadOnlyList<LiftCarLayout> CarLayouts { get; init; }

    /// <summary>Door sides (0 front, 1 rear; rear null when the shaft has none).</summary>
    public required LiftSide?[] Sides { get; init; }

    /// <summary>Places in a car (persons, D-243).</summary>
    public required double Capacity { get; init; }

    /// <summary>Rated speed, m/s.</summary>
    public required double SpeedMps { get; init; }

    /// <summary>Acceleration and braking, m/s².</summary>
    public required double AccelMps2 { get; init; }

    /// <summary>Door opening time, s.</summary>
    public required double DoorOpenS { get; init; }

    /// <summary>Door closing time, s.</summary>
    public required double DoorCloseS { get; init; }

    /// <summary>Least time the doors stay open at a stop, s.</summary>
    public required double DwellS { get; init; }

    /// <summary>People who need accessible devices board first (D-239).</summary>
    public required bool AccessiblePriority { get; init; }

    /// <summary>Stop heights, lowest first.</summary>
    public required double[] StopZ { get; init; }

    /// <summary>Stop level ids, lowest first.</summary>
    public required Guid[] StopLevels { get; init; }

    /// <summary>Floor surface of each stop (−1 when the level has no floor).</summary>
    public required int[] StopFloor { get; init; }

    /// <summary>Landing portal per stop and side: <c>Portals[stop * 2 + side]</c>, −1 for none.</summary>
    public required int[] Portals { get; init; }

    /// <summary>Stop the bank returns to in Up-peak (D-244).</summary>
    public required int LobbyStop { get; init; }

    /// <summary>Dispatch before the first schedule row (Shuttle ignores the schedule).</summary>
    public required LiftDispatch Dispatch { get; init; }

    /// <summary>Schedule rows sorted by time of day (D-238).</summary>
    public required (TimeSpan From, LiftDispatch Dispatch)[] Schedule { get; init; }

    /// <summary>Number of stops.</summary>
    public int Stops => StopZ.Length;

    /// <summary>Round trip over the whole rise with a stop at each end, s (for the wait estimate).</summary>
    public required double RoundTripS { get; init; }

    /// <summary>The landing portal at a stop on a side, or −1.</summary>
    public int PortalAt(int stop, int side) => stop >= 0 && stop < Stops && side is 0 or 1 ? Portals[(stop * 2) + side] : -1;

    /// <summary>True when the lift has a landing (any side) at the stop.</summary>
    public bool Serves(int stop) => PortalAt(stop, 0) >= 0 || PortalAt(stop, 1) >= 0;

    /// <summary>The dispatch in force at a time of day.</summary>
    public LiftDispatch DispatchAt(TimeSpan timeOfDay)
    {
        if (Dispatch == LiftDispatch.Shuttle)
        {
            return LiftDispatch.Shuttle;
        }

        LiftDispatch mode = Dispatch;
        foreach ((TimeSpan from, LiftDispatch dispatch) in Schedule)
        {
            if (from <= timeOfDay)
            {
                mode = dispatch;
            }
        }

        return mode == LiftDispatch.DestinationDispatch ? LiftDispatch.Collective : mode;
    }
}
