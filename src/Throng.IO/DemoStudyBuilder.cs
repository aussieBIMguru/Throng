using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Builds the built-in demo study, "Demo: Interchange Station" (§21). Generated in code so it always matches
/// the current model and file format. It grows each milestone and doubles as the round acceptance test.
/// </summary>
/// <remarks>
/// Content: three levels with walkable floors, obstacles, a wall and typed zones; the vertical set (round 004,
/// D-061): street ↔ concourse wide stair and escalator pair, concourse ↔ platform switchback stair and escalator
/// pair, and the accessible lift's shaft (the lift itself arrives in M4). Openings are cut automatically by the
/// connectors. Round 005 adds devices, floor-to-floor columns, lift core walls spanning two levels, a goods lift
/// shaft that skips the concourse and a back-of-house stair core with one circuit per level (staff only, D-110).
/// Round 006 adds constant-rate emission rules between the street entrances and the platform doors (D-109). The agent
/// profiles, schedules, connector and device presets used are embedded in the study library.
/// Plan origin is the south-west corner of the concourse; X runs east, Y north.
/// </remarks>
public static class DemoStudyBuilder
{
    /// <summary>Display name of the demo study.</summary>
    public const string DemoName = "Demo: Interchange Station";

    /// <summary>Fixed seed so demo runs are reproducible.</summary>
    public const int DemoSeed = 20261024;

    /// <summary>Builds a fresh demo study.</summary>
    public static Study Build()
    {
        var study = new Study
        {
            Name = DemoName,
            Description =
                "Compact three-level interchange: street entry hall, ticketing concourse and an island platform. " +
                "The gate line and the platform stair are deliberately tight so results show pressure points.",
            Settings = new StudySettings
            {
                Seed = DemoSeed,
                StartTimeOfDay = new TimeSpan(7, 30, 0),
                EndCondition = StudyEndCondition.FixedDuration,
                DurationS = 3600,
                WarmUpS = 300,
            },
        };

        Level street = AddLevel(study, "Street", 0.0, 5.0);
        Level concourse = AddLevel(study, "Concourse", -5.0, 5.0);
        Level platform = AddLevel(study, "Platform", -11.0, 6.0);

        BuildStreet(study, street);
        BuildConcourse(study, concourse);
        BuildPlatform(study, platform);
        BuildVertical(study, street, concourse, platform);
        BuildDevices(study, concourse);
        BuildSpanning(study, street, concourse, platform);
        EmbedTemplates(study);
        BuildDemand(study);
        return study;
    }

    private static Level AddLevel(Study study, string name, double elevation, double height)
    {
        var level = new Level { Name = name, ElevationM = elevation, HeightM = height };
        study.Levels.Add(level);
        return level;
    }

    private static void BuildStreet(Study study, Level level)
    {
        // Irregular entry hall: the north-east corner is chamfered to a plaza edge.
        Add(study, level, "Entry hall", RegionKind.Walkable, ZoneKind.None,
            [new(0, 0), new(60, 0), new(60, 20), new(52, 26), new(0, 26)]);

        Add(study, level, "Newsagent kiosk", RegionKind.Obstacle, ZoneKind.None, Rect(30, 15, 34, 18), height: 2.6);
        foreach (double x in new[] { 10.0, 20.0, 30.0, 40.0, 50.0 })
        {
            Add(study, level, $"Column S{x:0}", RegionKind.Obstacle, ZoneKind.None, Square(x, 8, 0.6), height: 5.0);
        }

        Add(study, level, "West entrance", RegionKind.Zone, ZoneKind.SpawnSink, Rect(0, 9, 1.5, 17));
        Add(study, level, "East entrance", RegionKind.Zone, ZoneKind.SpawnSink, Rect(58.5, 6, 60, 14));
        Add(study, level, "Bus stop", RegionKind.Zone, ZoneKind.SpawnSink, Rect(20, 24.5, 32, 26));
        Add(study, level, "Retail frontage", RegionKind.Zone, ZoneKind.Dwell, Rect(38, 2, 52, 7));
    }

    private static void BuildConcourse(Study study, Level level)
    {
        Add(study, level, "Ticket hall and paid concourse", RegionKind.Walkable, ZoneKind.None, Rect(0, 0, 60, 30));

        // Gate line barrier: two solid runs leaving a 6 m gap where the gates go (M3).
        Add(study, level, "Gate line barrier south", RegionKind.Obstacle, ZoneKind.None, Rect(27.6, 0, 28.4, 12), height: 1.1);
        Add(study, level, "Gate line barrier north", RegionKind.Obstacle, ZoneKind.None, Rect(27.6, 18, 28.4, 30), height: 1.1);
        Add(study, level, "Gate line barrier middle", RegionKind.Obstacle, ZoneKind.None, Rect(27.6, 16.0, 28.4, 16.8), height: 1.1);
        Add(study, level, "Gate line count", RegionKind.Zone, ZoneKind.Measurement, Rect(27, 12, 29, 18));

        foreach (double x in new[] { 10.0, 20.0, 40.0, 50.0 })
        {
            Add(study, level, $"Column C{x:0}S", RegionKind.Obstacle, ZoneKind.None, Square(x, 6, 0.6), height: 5.0);
            Add(study, level, $"Column C{x:0}N", RegionKind.Obstacle, ZoneKind.None, Square(x, 24, 0.6), height: 5.0);
        }

        Add(study, level, "Back-of-house wall", RegionKind.Wall, ZoneKind.None, [new(56, 12), new(56, 22), new(60, 22)], height: 3.0, thickness: 0.3);

        Add(study, level, "Ticket machine crowd", RegionKind.Zone, ZoneKind.Queue, Rect(3, 1.5, 13, 6));
        Add(study, level, "Ticket counter queue", RegionKind.Zone, ZoneKind.Queue, Rect(4, 20, 12, 28));
        Add(study, level, "Seating area", RegionKind.Zone, ZoneKind.Wait, Rect(50, 3, 58, 9));
    }

    private static void BuildPlatform(Study study, Level level)
    {
        Add(study, level, "Island platform", RegionKind.Walkable, ZoneKind.None, Rect(0, 7, 60, 23));

        Add(study, level, "South platform edge", RegionKind.Zone, ZoneKind.Slow, Rect(0, 7, 60, 7.6), speedFactor: 0.7);
        Add(study, level, "North platform edge", RegionKind.Zone, ZoneKind.Slow, Rect(0, 22.4, 60, 23), speedFactor: 0.7);

        double[] doorStarts = [5, 14, 23, 34, 43, 52];
        for (int i = 0; i < doorStarts.Length; i++)
        {
            double x = doorStarts[i];
            Add(study, level, $"Southbound door {i + 1}", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x, 7.6, x + 3, 8.6));
            Add(study, level, $"Northbound door {i + 1}", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x, 21.4, x + 3, 22.4));
        }

        foreach (double x in new[] { 8.0, 16.0, 24.0, 52.0 })
        {
            Add(study, level, $"Column P{x:0}", RegionKind.Obstacle, ZoneKind.None, Square(x, 15, 0.6), height: 6.0);
        }

        Add(study, level, "Platform seating", RegionKind.Zone, ZoneKind.Wait, Rect(26, 17, 32, 19));
    }

    /// <summary>
    /// Vertical set (D-061). Every connector starts on its lower level and runs east (+X); the openings in the
    /// upper levels are cut automatically (D-055), which replaces the hand-drawn voids of rounds 001–003.
    /// </summary>
    private static void BuildVertical(Study study, Level street, Level concourse, Level platform)
    {
        // Street ↔ concourse: 3.6 m stair from the unpaid side of the concourse up to the entry hall, with an
        // escalator pair beside it.
        Place(study, "connector.stair-3600", concourse, street, new Point2(12, 16.5), "Street stair");
        Place(study, "connector.escalator-pair", concourse, street, new Point2(12, 21), "Street escalator");

        // Concourse ↔ platform: a deliberately tight 1.5 m switchback (§21.1) returning on the north side, and an
        // escalator pair to the south, both on the paid side of the gate line.
        Place(study, "connector.stair-switchback-1500", platform, concourse, new Point2(36, 14), "Platform stair");
        Place(study, "connector.escalator-pair", platform, concourse, new Point2(36, 10), "Platform escalator");

        // Accessible lift shaft, concourse ↔ platform (the car and its doors arrive in M4, D-068).
        Shaft shaft = ConnectorFactory.CreateShaft(study, platform.Id, concourse.Id, Rect(30, 19.5, 32.4, 21.9));
        shaft.Name = "Accessible lift shaft";
        shaft.Tag = "Lift";
        study.Shafts.Add(shaft);
    }

    /// <summary>
    /// Devices (D-089): ticket machines along the south wall, two staffed counters on the north wall, the gate line
    /// and accessible gate in the barrier gap, and the information desk. Queues and behaviour arrive in M3.
    /// </summary>
    private static void BuildDevices(Study study, Level concourse)
    {
        foreach (double x in new[] { 5.0, 7.0, 9.0, 11.0 })
        {
            PlaceDevice(study, "device.ticket-machine", concourse, new Point2(x, 0.3), 90);
        }

        PlaceDevice(study, "device.ticket-counter", concourse, new Point2(6, 29.5), 270);
        PlaceDevice(study, "device.ticket-counter", concourse, new Point2(10, 29.5), 270);
        PlaceDevice(study, "device.gate-line", concourse, new Point2(28, 14.0), 0);
        PlaceDevice(study, "device.gate-accessible", concourse, new Point2(28, 17.4), 0);
        PlaceDevice(study, "device.info-desk", concourse, new Point2(50, 26.5), 270);
    }

    /// <summary>
    /// Multi-level elements (D-076–D-093): columns run floor to floor; the accessible lift's core walls rise from
    /// the platform to the concourse; a goods lift shaft runs from the platform to the street without stopping at
    /// the concourse; a back-of-house switchback stair core climbs all three levels, one circuit per level.
    /// </summary>
    private static void BuildSpanning(Study study, Level street, Level concourse, Level platform)
    {
        foreach (Region column in study.Regions.Where(r => r.Name.StartsWith("Column ", StringComparison.Ordinal)))
        {
            column.HeightMode = RegionHeightMode.FullStorey;
        }

        // Three sides of the accessible lift shaft; the south side is the door.
        Region core = Add(study, platform, "Lift core walls", RegionKind.Wall, ZoneKind.None,
            [new(29.8, 19.5), new(29.8, 22.1), new(32.6, 22.1), new(32.6, 19.5)], height: 3.0, thickness: 0.2);
        core.HeightMode = RegionHeightMode.FullStorey;
        core.TopLevelId = concourse.Id;

        Shaft goods = ConnectorFactory.CreateShaft(study, platform.Id, street.Id, Rect(56, 9, 58.4, 11.4));
        goods.Name = "Goods lift shaft";
        goods.Tag = "Lift";
        goods.NoStopLevelIds = [concourse.Id];
        study.Shafts.Add(goods);

        Place(study, "connector.stair-switchback-1500", platform, street, new Point2(4, 18), "Back-of-house stair");

        // Staff only (D-110): otherwise it's the quickest public route and everyone skips the gate line.
        foreach (Connector stair in study.Connectors.Where(c => c.Name == "Back-of-house stair"))
        {
            stair.StaffOnly = true;
        }
    }

    /// <summary>
    /// Constant-rate flows across all three levels (D-109): each street entrance sends commuters and some
    /// infrequent travellers to the twelve platform doors (equal weights); each door sends people back up to the
    /// entrances and the bus stop (west 40, east 40, bus 20). Train timetables replace the door flows in round 007.
    /// </summary>
    private static void BuildDemand(Study study)
    {
        Guid commuter = TemplateIds.FromKey("agent.commuter");
        Guid infrequent = TemplateIds.FromKey("agent.infrequent");
        List<Region> doors = [.. study.Regions.Where(r => r.Name.Contains(" door ", StringComparison.Ordinal) && r.IsSink)];
        Region? west = study.Regions.FirstOrDefault(r => r.Name == "West entrance");
        Region? east = study.Regions.FirstOrDefault(r => r.Name == "East entrance");
        Region? bus = study.Regions.FirstOrDefault(r => r.Name == "Bus stop");
        if (west is null || east is null || bus is null || doors.Count == 0)
        {
            return;
        }

        List<SinkWeight> toDoors = [.. doors.Select(d => new SinkWeight { SinkId = d.Id, Weight = 1 })];
        foreach (Region entrance in new[] { west, east, bus })
        {
            entrance.EmissionRules.Add(Rule(commuter, 16, toDoors));
            entrance.EmissionRules.Add(Rule(infrequent, 4, toDoors));
        }

        List<SinkWeight> toStreet =
        [
            new() { SinkId = west.Id, Weight = 40 },
            new() { SinkId = east.Id, Weight = 40 },
            new() { SinkId = bus.Id, Weight = 20 },
        ];
        foreach (Region door in doors)
        {
            door.EmissionRules.Add(Rule(commuter, 3, toStreet));
        }
    }

    private static EmissionRule Rule(Guid profileId, double perMinute, List<SinkWeight> destinations) => new()
    {
        ProfileId = profileId,
        RatePerMinute = perMinute,
        Pattern = EmissionPattern.Constant,
        DestinationMode = DestinationMode.ChosenSinks,
        Destinations = [.. destinations],
    };

    private static void PlaceDevice(Study study, string presetKey, Level level, Point2 at, double angleDeg)
    {
        if (BuiltInLibrary.Instance.FindByKey(presetKey) is not DeviceTemplate template)
        {
            return;
        }

        Device device = DeviceFactory.FromTemplate(study, template, level.Id, at, angleDeg);
        device.TemplateId = study.Library.AddCopy(template, TemplateSource.Embedded).Id;
        study.Devices.Add(device);
    }

    /// <summary>Places a built-in connector preset; pairs get "(up)" / "(down)" suffixes.</summary>
    private static void Place(Study study, string presetKey, Level bottom, Level top, Point2 at, string name)
    {
        if (BuiltInLibrary.Instance.FindByKey(presetKey) is not ConnectorTemplate template)
        {
            return;
        }

        List<Connector> placed = ConnectorFactory.FromTemplate(study, template, bottom.Id, top.Id, at, angleDeg: 0, TurnSide.Left);
        for (int i = 0; i < placed.Count; i++)
        {
            Connector connector = placed[i];
            connector.Name = placed.Count == 1 ? name : $"{name} ({(connector.Direction == ConnectorDirection.Down ? "down" : "up")})";
            if (study.Library.AddCopy(template, TemplateSource.Embedded) is { } embedded)
            {
                connector.TemplateId = embedded.Id;
            }
        }

        study.Connectors.AddRange(placed);
    }

    /// <summary>Embeds copies of the built-in templates the demo will use, so the file is self-contained (§22.6).</summary>
    private static void EmbedTemplates(Study study)
    {
        TemplateLibrary builtIns = BuiltInLibrary.Instance;
        string[] keys =
        [
            "agent.commuter", "agent.infrequent", "agent.luggage", "agent.mobility", "agent.staff",
            "schedule.always-open", "schedule.am-peak", "schedule.train-6min", "schedule.bus-10min", "schedule.escalator-reversal",
        ];

        foreach (string key in keys)
        {
            TemplateBase? template = builtIns.FindByKey(key);
            if (template is not null)
            {
                study.Library.AddCopy(template, TemplateSource.Embedded);
            }
        }
    }

    private static Region Add(
        Study study,
        Level level,
        string name,
        RegionKind kind,
        ZoneKind zone,
        List<Point2> outline,
        double height = 1.2,
        double thickness = 0.2,
        double speedFactor = 1.0,
        string? tag = null)
    {
        var region = new Region
        {
            LevelId = level.Id,
            Name = name,
            Kind = kind,
            ZoneKind = zone,
            Outline = outline,
            HeightM = height,
            ThicknessM = thickness,
            SpeedFactor = speedFactor,
            Tag = tag,
        };
        study.Regions.Add(region);
        return region;
    }

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    private static List<Point2> Square(double cx, double cy, double size)
    {
        double h = size * 0.5;
        return Rect(cx - h, cy - h, cx + h, cy + h);
    }
}
