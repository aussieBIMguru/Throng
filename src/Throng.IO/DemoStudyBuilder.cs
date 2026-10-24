using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Builds the built-in demo study, "Demo: Interchange Station" (§21). Generated in code so it always matches
/// the current model and file format. It grows each milestone and doubles as the round acceptance test.
/// </summary>
/// <remarks>
/// M0 content: three levels with walkable floors, obstacles, voids, a wall and typed zones, plus the agent
/// profiles and schedules the study will use embedded in its library. Stairs, escalators, devices and the lift
/// are added in M1–M4.
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
        EmbedTemplates(study);
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

        Add(study, level, "Stair void to concourse", RegionKind.Obstacle, ZoneKind.None, Rect(12, 14, 26, 19), height: 1.1, tag: "Void");
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

        Add(study, level, "Stair void to platform", RegionKind.Obstacle, ZoneKind.None, Rect(34, 11, 46, 16), height: 1.1, tag: "Void");

        // Gate line barrier: two solid runs leaving a 6 m gap where the gates go (M3).
        Add(study, level, "Gate line barrier south", RegionKind.Obstacle, ZoneKind.None, Rect(27.6, 0, 28.4, 12), height: 1.1);
        Add(study, level, "Gate line barrier north", RegionKind.Obstacle, ZoneKind.None, Rect(27.6, 18, 28.4, 30), height: 1.1);
        Add(study, level, "Gate line count", RegionKind.Zone, ZoneKind.Measurement, Rect(27, 12, 29, 18));

        Add(study, level, "Information desk", RegionKind.Obstacle, ZoneKind.None, Rect(48, 25, 52, 27), height: 1.1);
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
