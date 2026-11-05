using Throng.Core.Geometry;

namespace Throng.Sim;

/// <summary>
/// A train service ready to run (D-184–D-187): its doors as route targets, the door zones' emitters that people get
/// off from, the timetable, and the plain box drawn along the platform edge while a train is in (D-190).
/// </summary>
public sealed class SimTrain
{
    /// <summary>Position in <see cref="SimModel.Trains"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Source service id.</summary>
    public required Guid ServiceId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Level of the doors.</summary>
    public required Guid LevelId { get; init; }

    /// <summary>Floor surface of the doors.</summary>
    public required int Floor { get; init; }

    /// <summary>Door zone ids, in the service's order.</summary>
    public required Guid[] DoorRegionIds { get; init; }

    /// <summary>Each door's exit sink (nav sink index), parallel to <see cref="DoorRegionIds"/>.</summary>
    public required int[] DoorSinks { get; init; }

    /// <summary>Each door's centre, parallel to <see cref="DoorRegionIds"/>.</summary>
    public required Point2[] DoorCentres { get; init; }

    /// <summary>Half the width of each door along the platform edge, metres.</summary>
    public required double[] DoorHalfWidths { get; init; }

    /// <summary>Per door: the emitters (index into <see cref="SimModel.Emitters"/>) people get off from, with weights.</summary>
    public required (int Emitter, double Weight)[][] DoorEmitters { get; init; }

    /// <summary>Unit vector from the track into the platform.</summary>
    public required Point2 Inward { get; init; }

    /// <summary>Unit vector along the platform edge.</summary>
    public required Point2 Along { get; init; }

    /// <summary>The train's outline while it's in (four corners), on the track side of the doors.</summary>
    public required Point2[] Box { get; init; }

    /// <summary>Time between trains, seconds.</summary>
    public required double HeadwayS { get; init; }

    /// <summary>First arrival, seconds.</summary>
    public required double OffsetS { get; init; }

    /// <summary>Doors-open time, seconds.</summary>
    public required double DwellS { get; init; }

    /// <summary>How long getting off takes, seconds.</summary>
    public required double AlightOverS { get; init; }

    /// <summary>People getting off each train before the load curve.</summary>
    public required int AlightPerTrain { get; init; }

    /// <summary>Boarding places per train.</summary>
    public required int Capacity { get; init; }

    /// <summary>Compiled load curve minutes (empty = flat).</summary>
    public required double[] CurveMinutes { get; init; }

    /// <summary>Compiled load curve multipliers.</summary>
    public required double[] CurveMultipliers { get; init; }
}
