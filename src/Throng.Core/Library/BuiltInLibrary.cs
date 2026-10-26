using System.Globalization;

namespace Throng.Core.Library;

/// <summary>
/// The read-only built-in template catalogue (§22). Defined in code so it is compile-checked (D-019);
/// IDs are derived from keys, so a built-in keeps its identity across app versions.
/// </summary>
/// <remarks>
/// Values are indicative defaults for conceptual studies, not design standards. Every value is editable once
/// a template is duplicated into the user library or placed in a study.
/// </remarks>
public static class BuiltInLibrary
{
    /// <summary>Library browser category names.</summary>
    public static class Categories
    {
        public const string Agents = "Agent profiles";
        public const string Devices = "Devices";
        public const string Vertical = "Vertical circulation";
        public const string Lifts = "Lifts";
        public const string Schedules = "Schedules";
        public const string Journeys = "Journeys";
    }

    /// <summary>Key of the accessible wide gate preset, which wheelchair and luggage profiles favour (D-166).</summary>
    public const string AccessibleGateKey = "device.gate-accessible";

    private static readonly Lazy<TemplateLibrary> Cached = new(Build);

    /// <summary>
    /// The shared built-in catalogue. Treat as read-only; use <see cref="TemplateLibrary.AddCopy"/> or
    /// <see cref="TemplateCloner"/> to take editable copies.
    /// </summary>
    public static TemplateLibrary Instance => Cached.Value;

    /// <summary>Builds a fresh copy of the catalogue (used by tests and by <see cref="Instance"/>).</summary>
    public static TemplateLibrary Build()
    {
        var library = new TemplateLibrary();
        AddAgents(library);
        AddDevices(library);
        AddConnectors(library);
        AddLifts(library);
        AddSchedules(library);
        AddJourneys(library);
        return library;
    }

    /// <summary>Sets the common template fields for a built-in.</summary>
    private static T Init<T>(T template, string key, string name, string category, string colorHex, string description, int version = 1)
        where T : TemplateBase
    {
        template.Id = TemplateIds.FromKey(key);
        template.Key = key;
        template.Name = name;
        template.Category = category;
        template.ColorHex = colorHex;
        template.Description = description;
        template.Source = TemplateSource.BuiltIn;
        template.Version = version;
        return template;
    }

    /// <summary>
    /// Built-in journeys (D-154). Steps ask for device types and zone tags, so they work in any study; the ticket
    /// step is skipped by anyone already holding a "ticket" tag (D-150).
    /// </summary>
    private static void AddJourneys(TemplateLibrary lib)
    {
        const string cat = Categories.Journeys;
        const string ticket = "ticket";

        lib.Add(Init(new JourneyTemplate
        {
            Steps = [JourneyStep.ExitStep()],
        }, "journey.straight-exit", "Straight to exit", cat, "#4ADE80", "Walk straight to a sink. The same as having no journey."));

        lib.Add(Init(new JourneyTemplate
        {
            Steps = [JourneyStep.Device("Gate"), JourneyStep.ExitStep()],
        }, "journey.through-gates", "Through gates", cat, "#22D3EE", "Pass a ticket gate (whichever is quickest, queue included), then leave."));

        lib.Add(Init(new JourneyTemplate
        {
            Steps = [JourneyStep.Device("TicketMachine", lacks: ticket, grants: ticket), JourneyStep.Device("Gate"), JourneyStep.ExitStep()],
        }, "journey.ticket-gates", "Ticket then gates", cat, "#FACC15", "Buy a ticket at a machine unless already holding one, pass a gate, then leave."));

        lib.Add(Init(new JourneyTemplate
        {
            Steps =
            [
                JourneyStep.DwellIn("Retail", Distribution.Uniform(30, 180)),
                JourneyStep.Device("TicketMachine", lacks: ticket, grants: ticket),
                JourneyStep.Device("Gate"),
                JourneyStep.ExitStep(),
            ],
        }, "journey.browse-gates", "Browse then gates", cat, "#FBBF24", "Linger 30–180 s in a zone tagged Retail, buy a ticket if needed, pass a gate, then leave."));
    }

    private static void AddAgents(TemplateLibrary lib)
    {
        const string cat = Categories.Agents;

        lib.Add(Init(new AgentProfileTemplate
        {
            Speed = Distribution.Normal(1.45, 0.20, 0.9, 2.0),
            PatienceS = 180, Decisiveness = 0.9, LiftPreference = -0.6, MechanisedPreference = -0.2, EscalatorWalkProbability = 0.5, AcceptableJourneyS = 600,
            StairUpEffort = 1.5, StairDownEffort = 1.2, SightRadiusM = 40,
        }, "agent.commuter", "Commuter", cat, "#22D3EE", "Regular traveller. Fast, decisive, low patience; walks on escalators and avoids lifts.", version: 3));

        lib.Add(Init(new AgentProfileTemplate
        {
            Speed = Distribution.Normal(1.20, 0.22, 0.6, 1.8),
            PatienceS = 420, Decisiveness = 0.4, MechanisedPreference = 0.3, EscalatorWalkProbability = 0.1, AcceptableJourneyS = 900,
            SightRadiusM = 20,
        }, "agent.infrequent", "Infrequent traveller", cat, "#FACC15", "Tourist or occasional user. Slower and less decisive; often needs a ticket and the info desk.", version: 2));

        lib.Add(Init(new AgentProfileTemplate
        {
            RadiusM = 0.40, Speed = Distribution.Normal(1.10, 0.20, 0.6, 1.6),
            StairUpFactor = 0.45, StairDownFactor = 0.55, PatienceS = 360, LiftPreference = 0.5, MechanisedPreference = 0.8, EscalatorWalkProbability = 0.05, AcceptableJourneyS = 900,
            DevicePreferences = [new DevicePreference { Target = AccessibleGateKey, BonusS = 15 }],
            StairUpEffort = 3.0, StairDownEffort = 2.0,
        }, "agent.luggage", "Luggage traveller", cat, "#FB923C", "Carrying bags or a case. Larger footprint, slower, prefers escalators and lifts over stairs, and the accessible gate.", version: 3));

        lib.Add(Init(new AgentProfileTemplate
        {
            RadiusM = 0.45, HeightM = 1.35, Speed = Distribution.Normal(0.90, 0.15, 0.5, 1.3),
            PatienceS = 600, LiftPreference = 1.0, MechanisedPreference = 1.0, LiftOnly = true, EscalatorWalkProbability = 0, AcceptableJourneyS = 1200,
            DevicePreferences = [new DevicePreference { Target = AccessibleGateKey, BonusS = 30 }],
        }, "agent.mobility", "Mobility-impaired (wheelchair)", cat, "#A78BFA", "Wheelchair user. Lifts and ramps only; uses accessible gates.", version: 2));

        lib.Add(Init(new AgentProfileTemplate
        {
            Speed = Distribution.Normal(0.95, 0.18, 0.5, 1.4),
            StairUpFactor = 0.45, StairDownFactor = 0.50, PatienceS = 600, Decisiveness = 0.5, LiftPreference = 0.4, MechanisedPreference = 0.6, EscalatorWalkProbability = 0.05, AcceptableJourneyS = 1200,
            StairUpEffort = 3.0, StairDownEffort = 2.0,
        }, "agent.elderly", "Elderly", cat, "#F9A8D4", "Slower walker with a higher stair penalty; more patient in queues.", version: 2));

        lib.Add(Init(new AgentProfileTemplate
        {
            RadiusM = 0.35, Speed = Distribution.Normal(1.00, 0.20, 0.5, 1.5),
            PatienceS = 300, Decisiveness = 0.5, LiftPreference = 0.2, MechanisedPreference = 0.5, EscalatorWalkProbability = 0.05, AcceptableJourneyS = 900,
            StairUpEffort = 3.0, StairDownEffort = 2.0,
        }, "agent.family", "Family / group", cat, "#86EFAC", "Small group modelled as one wider, slower agent. True group movement comes later.", version: 2));

        lib.Add(Init(new AgentProfileTemplate
        {
            Speed = Distribution.Normal(1.40, 0.15, 1.0, 1.9),
            PatienceS = 900, Decisiveness = 1.0, EscalatorWalkProbability = 0.5, AcceptableJourneyS = 1800,
            StairUpEffort = 1.5, StairDownEffort = 1.2,
        }, "agent.staff", "Staff", cat, "#94A3B8", "Operator staff. Pinned routes and bypasses public queues (from M3).", version: 2));

        lib.Add(Init(new AgentProfileTemplate
        {
            Speed = Distribution.Normal(1.30, 0.25, 0.7, 2.0),
            PatienceS = 150, Decisiveness = 0.6, EscalatorWalkProbability = 0.4, AcceptableJourneyS = 1200, DensityTolerance = 0.9,
        }, "agent.fan", "Event fan", cat, "#F87171", "Stadium or event attendee. Arrives in surges, tolerates density, impatient at exits."));
    }

    private static void AddDevices(TemplateLibrary lib)
    {
        const string cat = Categories.Devices;

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "TicketMachine", ServiceTime = Distribution.Triangular(20, 35, 60), Queue = QueueMode.AutoCrowd, WidthM = 0.8, DepthM = 0.6,
        }, "device.ticket-machine", "Ticket machine", cat, "#FACC15", "Self-service ticket machine. One user at a time; users turn around when done."));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "TicketCounter", ServiceTime = Distribution.Triangular(45, 70, 120), Queue = QueueMode.Shared, WidthM = 1.8, DepthM = 1.0,
        }, "device.ticket-counter", "Staffed ticket counter", cat, "#FDE047", "Staffed window. Place several fed by one shared serpentine queue."));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "Gate", Servers = 4, ServiceTime = Distribution.Uniform(1.0, 2.0), Queue = QueueMode.AutoCrowd, PassThrough = true, WidthM = 4.0, DepthM = 1.2,
        }, "device.gate-line", "Ticket gate line (4 lanes)", cat, "#22D3EE", "Four-lane fare gate line, about 30–40 people a minute per lane. Fast pass-through; crowds form when a train empties.", version: 2));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "Gate", ServiceTime = Distribution.Uniform(3, 5), Queue = QueueMode.AutoCrowd, PassThrough = true, WidthM = 1.2, DepthM = 1.2, ChoiceWeight = -10,
        }, AccessibleGateKey, "Accessible wide gate", cat, "#67E8F9", "Single wide gate. Slower; preferred by mobility-impaired and luggage profiles."));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "Security", ServiceTime = Distribution.Triangular(20, 28, 40), Queue = QueueMode.Linear, PassThrough = true, WidthM = 1.0, DepthM = 4.0,
        }, "device.security-lane", "Security lane", cat, "#F87171", "Bag and body screening lane with a drawn queue."));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "InfoDesk", ServiceTime = Distribution.Triangular(30, 50, 90), Queue = QueueMode.AutoCrowd, WidthM = 2.0, DepthM = 1.0,
        }, "device.info-desk", "Information desk", cat, "#60A5FA", "Staffed enquiry point."));

        lib.Add(Init(new DeviceTemplate
        {
            Kind = DeviceKind.Zone, TypeTag = "Retail", Dwell = Distribution.Uniform(0, 180),
        }, "device.retail-zone", "Retail / dwell zone", cat, "#FBBF24", "Shops or concessions. Agents optionally linger for a dwell time."));

        lib.Add(Init(new DeviceTemplate
        {
            Kind = DeviceKind.Zone, TypeTag = "Waiting", Dwell = Distribution.Uniform(60, 600),
        }, "device.seating-zone", "Seating / waiting zone", cat, "#C084FC", "Agents wait here until a trigger such as a train arrival."));

        lib.Add(Init(new DeviceTemplate
        {
            Kind = DeviceKind.Zone, TypeTag = "Toilets", Capacity = 8, Dwell = Distribution.Uniform(60, 180),
        }, "device.toilets", "Toilet block", cat, "#93C5FD", "Capacity-limited dwell zone."));

        lib.Add(Init(new DeviceTemplate
        {
            Kind = DeviceKind.Zone, TypeTag = "Entrance", IsSpawn = true, IsSink = true,
        }, "device.entrance", "Entrance / exit", cat, "#4ADE80", "Building entry and exit. Spawns and absorbs agents without limit."));

        lib.Add(Init(new DeviceTemplate
        {
            Kind = DeviceKind.Zone, TypeTag = "VehicleDoor", IsSpawn = true, IsSink = true, Queue = QueueMode.AutoCrowd,
        }, "device.vehicle-door", "Train / vehicle door zone", cat, "#2DD4BF", "Unloads and boards agents on a vehicle timetable schedule."));

        lib.Add(Init(new DeviceTemplate
        {
            TypeTag = "BoardingGate", Servers = 2, ServiceTime = Distribution.Uniform(3, 6), Queue = QueueMode.BatchRelease, PassThrough = true, WidthM = 2.4, DepthM = 1.2,
        }, "device.boarding-gate", "Boarding gate", cat, "#E879F9", "Batch-release gate for timed boarding or entry."));
    }

    private static void AddConnectors(TemplateLibrary lib)
    {
        const string cat = Categories.Vertical;

        foreach ((double width, string label) in new[] { (1.5, "1500"), (2.4, "2400"), (3.6, "3600") })
        {
            lib.Add(Init(new ConnectorTemplate
            {
                Kind = ConnectorKind.Stair, WidthM = width, Direction = ConnectorDirection.Bidirectional, FlowPerMetrePerMin = 50,
            }, $"connector.stair-{label}", $"Stair {width.ToString("0.0", CultureInfo.InvariantCulture)} m", cat, "#FB923C", "Bidirectional stair with a width-based flow limit. 170 mm risers, 280 mm goings, 18 risers max per flight."));
        }

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Stair, WidthM = 1.5, Direction = ConnectorDirection.Bidirectional, FlowPerMetrePerMin = 50,
            Shape = ConnectorShape.Switchback, SwitchbackGapM = 0.2,
        }, "connector.stair-switchback-1500", "Stair switchback 1.5 m", cat, "#FB923C", "Two 1.5 m runs joined by a half landing; the return run comes back alongside (Tab flips the side while placing)."));

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Escalator, WidthM = 1.0, Direction = ConnectorDirection.Up, BeltSpeedMps = 0.5, ThroughputPerMin = 100,
        }, "connector.escalator-up", "Escalator (up)", cat, "#F97316", "1.0 m step escalator running up. 30° incline, 1.2 m flat run-off each end."));

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Escalator, WidthM = 1.0, Direction = ConnectorDirection.Down, BeltSpeedMps = 0.5, ThroughputPerMin = 100,
        }, "connector.escalator-down", "Escalator (down)", cat, "#F97316", "1.0 m step escalator running down. 30° incline, 1.2 m flat run-off each end."));

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Escalator, WidthM = 1.0, Direction = ConnectorDirection.Reversible, BeltSpeedMps = 0.5, ThroughputPerMin = 100, IsPaired = true,
        }, "connector.escalator-pair", "Escalator pair", cat, "#EA580C", "Linked up + down pair, 0.6 m apart; each unit's direction can be flipped, and reversed on a schedule later."));

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Travelator, WidthM = 1.0, Direction = ConnectorDirection.OneWay, BeltSpeedMps = 0.65, ThroughputPerMin = 90,
        }, "connector.travelator", "Travelator", cat, "#FDBA74", "Horizontal moving walk, one way."));

        lib.Add(Init(new ConnectorTemplate
        {
            Kind = ConnectorKind.Ramp, WidthM = 1.8, Direction = ConnectorDirection.Bidirectional, Gradient = 1.0 / 14.0, FlowPerMetrePerMin = 60,
        }, "connector.ramp-1-14", "Ramp 1:14", cat, "#FED7AA", "Accessible ramp; usable by every profile. 1.2 m landing every 9 m of run."));
    }

    private static void AddLifts(TemplateLibrary lib)
    {
        const string cat = Categories.Lifts;

        lib.Add(Init(new LiftTemplate
        {
            CapacityPersons = 13, SpeedMps = 1.6, CarWidthM = 1.6, CarDepthM = 1.4,
        }, "lift.passenger-13", "Passenger lift (13 persons)", cat, "#38BDF8", "Typical commercial passenger lift, collective dispatch."));

        lib.Add(Init(new LiftTemplate
        {
            CapacityPersons = 21, SpeedMps = 1.6, CarWidthM = 1.8, CarDepthM = 2.1, DwellS = 4,
        }, "lift.passenger-21", "Passenger lift (21 persons)", cat, "#0EA5E9", "Larger passenger lift for busier banks."));

        lib.Add(Init(new LiftTemplate
        {
            CapacityPersons = 17, SpeedMps = 1.0, DoorOpenS = 3.0, DoorCloseS = 3.5, DwellS = 5, CarWidthM = 1.4, CarDepthM = 2.4, AccessiblePriority = true,
        }, "lift.accessible", "Accessible / goods lift", cat, "#7DD3FC", "Deep car, slower doors, priority for mobility-impaired agents."));

        lib.Add(Init(new LiftTemplate
        {
            CapacityPersons = 13, SpeedMps = 1.0, Dispatch = LiftDispatch.Shuttle,
        }, "lift.shuttle", "Shuttle lift (2-stop)", cat, "#BAE6FD", "Back-and-forth between two levels, e.g. street to platform."));
    }

    private static void AddSchedules(TemplateLibrary lib)
    {
        const string cat = Categories.Schedules;

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Availability, Windows = [new TimeWindow { Start = TimeSpan.Zero, End = TimeSpan.FromDays(1) }],
        }, "schedule.always-open", "Always open", cat, "#4ADE80", "Available all day. The default for most objects."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Availability, Windows = [TimeWindow.Of(7, 0, 19, 0)],
        }, "schedule.business-hours", "Business hours", cat, "#86EFAC", "Open 07:00–19:00."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission,
            RateCurve = [RatePoint.At(6, 30, 5), RatePoint.At(7, 30, 40), RatePoint.At(8, 15, 60), RatePoint.At(9, 0, 30), RatePoint.At(9, 30, 10)],
        }, "schedule.am-peak", "AM peak", cat, "#FACC15", "Demand builds from 06:30, peaks around 08:15 and tails off by 09:30."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission,
            RateCurve = [RatePoint.At(16, 0, 10), RatePoint.At(17, 15, 60), RatePoint.At(18, 0, 40), RatePoint.At(19, 0, 5)],
        }, "schedule.pm-peak", "PM peak", cat, "#FDE047", "Demand builds from 16:00, peaks around 17:15 and tails off by 19:00."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission, RatePerMinute = 10,
        }, "schedule.constant-rate", "Constant rate", cat, "#A3E635", "Evenly spaced arrivals at a fixed rate."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission, RatePerMinute = 10, Poisson = true,
        }, "schedule.poisson", "Random arrivals (Poisson)", cat, "#BEF264", "Random arrivals averaging a fixed rate."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.VehicleTimetable, IntervalS = 360, BatchSize = 300, UnloadDurationS = 45, BoardWindowS = 60, AlternateDirections = true,
        }, "schedule.train-6min", "Train every 6 min", cat, "#2DD4BF", "Arrive, unload 300 over 45 s, board for 60 s, depart. Alternates platform faces."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.VehicleTimetable, IntervalS = 600, BatchSize = 40, UnloadDurationS = 30, BoardWindowS = 45,
        }, "schedule.bus-10min", "Bus every 10 min", cat, "#5EEAD4", "Smaller batches through a single door."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission, EventStart = new TimeSpan(19, 30, 0),
            RateCurve = [RatePoint.At(18, 0, 20), RatePoint.At(18, 45, 120), RatePoint.At(19, 15, 400), RatePoint.At(19, 30, 150), RatePoint.At(19, 45, 20)],
        }, "schedule.event-ingress", "Event ingress ramp", cat, "#F87171", "Gates open 90 min before a 19:30 start; demand builds to a peak just before kick-off."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Emission, EventStart = new TimeSpan(22, 0, 0),
            RateCurve = [RatePoint.At(22, 0, 1500), RatePoint.At(22, 5, 800), RatePoint.At(22, 10, 200), RatePoint.At(22, 20, 0)],
        }, "schedule.event-egress", "Event egress dump", cat, "#FCA5A5", "Most of the crowd released within minutes of the final whistle."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.DirectionReversal, SwitchTime = new TimeSpan(12, 0, 0), StateBefore = "Up", StateAfter = "Down",
        }, "schedule.escalator-reversal", "Escalator AM up / PM down", cat, "#FB923C", "Reverses an escalator's direction at midday."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.DispatchSwitch, SwitchTime = new TimeSpan(9, 30, 0), StateBefore = "UpPeak", StateAfter = "Collective",
        }, "schedule.lift-uppeak-am", "Lift up-peak AM / collective", cat, "#38BDF8", "Up-peak dispatch until 09:30, then collective."));

        lib.Add(Init(new ScheduleTemplate
        {
            Kind = ScheduleKind.Closure, Windows = [TimeWindow.Of(10, 0, 10, 30)],
        }, "schedule.closure", "Closure window", cat, "#94A3B8", "Unavailable 10:00–10:30, for maintenance or incident testing."));
    }
}
