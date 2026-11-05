using System.Globalization;

namespace Throng.Core.Model;

/// <summary>
/// What a service is (round 025, D-398): it only changes the words, the plan label and the values a new service starts
/// with. Every kind runs on the same timetable engine (arrive, people get off, people board until full, leave).
/// </summary>
public enum ServiceKind
{
    /// <summary>A train at a platform (the round 010 meaning, and the default for older files).</summary>
    Train,

    /// <summary>A plane boarding at a gate (usually nobody gets off).</summary>
    Plane,

    /// <summary>A bus or coach at a stop.</summary>
    Bus,

    /// <summary>A ride (a rollercoaster train, a boat ride): small loads often.</summary>
    Ride,

    /// <summary>Anything else that arrives on a timetable and takes people away.</summary>
    Generic,
}

/// <summary>Words and starting values per <see cref="ServiceKind"/> (round 025, D-398).</summary>
public static class ServiceKinds
{
    /// <summary>Every kind, in menu order.</summary>
    public static IReadOnlyList<ServiceKind> All { get; } = [ServiceKind.Train, ServiceKind.Plane, ServiceKind.Bus, ServiceKind.Ride, ServiceKind.Generic];

    /// <summary>Menu label: "Train", "Plane", "Bus / coach", "Ride", "Other".</summary>
    public static string Label(ServiceKind kind) => kind switch
    {
        ServiceKind.Plane => "Plane",
        ServiceKind.Bus => "Bus / coach",
        ServiceKind.Ride => "Ride",
        ServiceKind.Generic => "Other",
        _ => "Train",
    };

    /// <summary>The vehicle in sentences: "train", "plane", "bus", "ride car", "vehicle".</summary>
    public static string Vehicle(ServiceKind kind) => kind switch
    {
        ServiceKind.Plane => "plane",
        ServiceKind.Bus => "bus",
        ServiceKind.Ride => "ride car",
        ServiceKind.Generic => "vehicle",
        _ => "train",
    };

    /// <summary>The door zones in sentences: "doors", "boarding gates", "loading gates".</summary>
    public static string Doors(ServiceKind kind) => kind switch
    {
        ServiceKind.Plane => "boarding gates",
        ServiceKind.Ride => "loading gates",
        _ => "doors",
    };

    /// <summary>Short caps label drawn on the vehicle in plan: TRAIN, PLANE, BUS, RIDE, SERVICE.</summary>
    public static string Badge(ServiceKind kind) => kind == ServiceKind.Generic ? "SERVICE" : Vehicle(kind).Split(' ')[0].ToUpper(CultureInfo.InvariantCulture);

    /// <summary>
    /// The timetable a new service of this kind starts with: headway, doors-open time, people getting off, how long
    /// getting off takes, places. Indicative values, editable on every service.
    /// </summary>
    public static (double HeadwayS, double DwellS, int AlightPerTrain, double AlightOverS, int Capacity) Defaults(ServiceKind kind) => kind switch
    {
        // A flight an hour boarding over 25 minutes, nobody getting off at the gate.
        ServiceKind.Plane => (3600, 1500, 0, 1, 180),
        ServiceKind.Bus => (600, 30, 20, 10, 60),

        // A rollercoaster train every 90 s: 24 seats, the last riders off first.
        ServiceKind.Ride => (90, 40, 24, 15, 24),
        _ => (TrainService.DefaultHeadwayS, TrainService.DefaultDwellS, 150, 20, 300),
    };
}
