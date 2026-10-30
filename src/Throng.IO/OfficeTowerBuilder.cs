using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Builds the second built-in demo, "Demo: Office Tower" (round 013b, D-250), to test lifts on their own: a ground
/// lobby and ten office floors joined by a bank of four 13-person passenger cars, running up-peak in the morning
/// and collective later, with people arriving at the street door and heading for desks on every floor. Round 014
/// (D-257) adds one switchback stair east of the core, every floor, open to everyone, so short trips show the
/// lift-vs-stair choice.
/// </summary>
/// <remarks>
/// Floor plates are 40 × 24 m. The 8.8 × 2.4 m lift shaft sits in the middle with its doors facing south onto the lift
/// lobby; core walls close its other three sides from the ground to the top floor. Plan origin is the south-west
/// corner; X runs east, Y north.
/// </remarks>
public static class OfficeTowerBuilder
{
    /// <summary>Display name of the office tower demo.</summary>
    public const string DemoName = "Demo: Office Tower";

    /// <summary>Office floors above the ground lobby.</summary>
    public const int OfficeFloors = 10;

    /// <summary>Preset of the tower stair (D-257).</summary>
    public const string StairPresetKey = "connector.stair-switchback-1500";

    /// <summary>Fixed seed so runs are reproducible.</summary>
    public const int DemoSeed = 20261029;

    /// <summary>Ground floor-to-floor height, metres.</summary>
    public const double LobbyHeightM = 4.5;

    /// <summary>Office floor-to-floor height, metres.</summary>
    public const double FloorHeightM = 3.6;

    /// <summary>Builds a fresh office tower study.</summary>
    public static Study Build()
    {
        var study = new Study
        {
            Name = DemoName,
            Description =
                "Ground lobby and ten office floors served by a four-car lift bank (up-peak from 07:30, collective from " +
                "09:30) and one stair east of the core. People arrive at the street door and go to desks on every floor.",
            Settings = new StudySettings
            {
                Seed = DemoSeed,
                StartTimeOfDay = new TimeSpan(8, 0, 0),
                EndCondition = StudyEndCondition.FixedDuration,
                DurationS = 3600,
                WarmUpS = 120,
            },
        };

        var ground = new Level { Name = "Ground lobby", ElevationM = 0, HeightM = LobbyHeightM };
        study.Levels.Add(ground);
        var floors = new List<Level>();
        for (int n = 1; n <= OfficeFloors; n++)
        {
            var level = new Level { Name = $"Level {n}", ElevationM = LobbyHeightM + ((n - 1) * FloorHeightM), HeightM = FloorHeightM };
            study.Levels.Add(level);
            floors.Add(level);
        }

        Add(study, ground, "Lobby", RegionKind.Walkable, ZoneKind.None, Rect(0, 0, 40, 24));
        Add(study, ground, "Reception desk", RegionKind.Obstacle, ZoneKind.None, Rect(4, 6, 9, 7.2), height: 1.1);
        Region door = Add(study, ground, "Street door", RegionKind.Zone, ZoneKind.Spawn, Rect(16, 0, 24, 1.5));

        var desks = new List<Region>();
        foreach (Level level in floors)
        {
            Add(study, level, $"{level.Name} floor", RegionKind.Walkable, ZoneKind.None, Rect(0, 0, 40, 24));
            desks.Add(Add(study, level, $"{level.Name} desks", RegionKind.Zone, ZoneKind.Sink, Rect(3, 18, 37, 22.5)));
        }

        BuildCore(study, ground, floors[^1]);
        BuildStair(study, ground, floors[^1]);
        EmbedTemplates(study);

        // About 1,000 people over the hour, peaking around 08:30 (the AM peak curve on 16 a minute).
        Guid commuter = TemplateIds.FromKey("agent.commuter");
        Guid wheelchair = TemplateIds.FromKey(BuiltInLibrary.MobilityProfileKey);
        door.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(commuter, share: 97), MixLine.Of(wheelchair, share: 3)],
            RatePerMinute = 16,
            Pattern = EmissionPattern.Poisson,
            RateCurve = RateCurves.PointsOf(RateCurvePreset.AmPeak),
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [.. desks.Select(d => new SinkWeight { SinkId = d.Id, Weight = 1 })],
        });

        return study;
    }

    /// <summary>The lift shaft (ground to top), its four-car bank and the core walls on its other three sides.</summary>
    private static void BuildCore(Study study, Level ground, Level top)
    {
        Shaft shaft = ConnectorFactory.CreateShaft(study, ground.Id, top.Id, Rect(15.6, 12, 24.4, 14.4));
        shaft.Name = "Lift core";
        shaft.Tag = "Lift";
        if (BuiltInLibrary.Instance.FindByKey("lift.passenger-13") is LiftTemplate preset)
        {
            TemplateBase embedded = study.Library.AddCopy(preset, TemplateSource.Embedded);
            Lift lift = LiftFactory.FromTemplate(shaft, preset, embedded.Id);
            lift.Name = "Lifts A–D";
            lift.Cars = Lift.MaxCars;
            lift.LobbyLevelId = ground.Id;
            lift.Dispatch = LiftDispatch.Collective;
            lift.Schedule =
            [
                new LiftScheduleEntry { From = new TimeSpan(7, 30, 0), Dispatch = LiftDispatch.UpPeak },
                new LiftScheduleEntry { From = new TimeSpan(9, 30, 0), Dispatch = LiftDispatch.Collective },
            ];
            shaft.Lift = lift;
        }

        study.Shafts.Add(shaft);

        // Edge 0 (south) is the door side the walls leave open.
        Region core = Add(study, ground, "Core walls", RegionKind.Wall, ZoneKind.None,
            [new(15.4, 12), new(15.4, 14.6), new(24.6, 14.6), new(24.6, 12)], height: 3.0, thickness: 0.2);
        core.HeightMode = RegionHeightMode.FullStorey;
        core.TopLevelId = top.Id;
    }

    /// <summary>
    /// The tower stair (D-257): a 1.5 m switchback from the ground lobby to the top floor (one circuit per storey), east
    /// of the core and south of the desks, open to everyone.
    /// </summary>
    private static void BuildStair(Study study, Level ground, Level top)
    {
        if (BuiltInLibrary.Instance.FindByKey(StairPresetKey) is not ConnectorTemplate template)
        {
            return;
        }

        List<Connector> placed = ConnectorFactory.FromTemplate(study, template, ground.Id, top.Id, new Point2(28, 7), angleDeg: 0, TurnSide.Left);
        TemplateBase embedded = study.Library.AddCopy(template, TemplateSource.Embedded);
        foreach (Connector stair in placed)
        {
            stair.Name = "Tower stair";
            stair.TemplateId = embedded.Id;
        }

        study.Connectors.AddRange(placed);
    }

    private static void EmbedTemplates(Study study)
    {
        foreach (string key in new[] { "agent.commuter", BuiltInLibrary.MobilityProfileKey })
        {
            if (BuiltInLibrary.Instance.FindByKey(key) is { } template)
            {
                study.Library.AddCopy(template, TemplateSource.Embedded);
            }
        }
    }

    private static Region Add(Study study, Level level, string name, RegionKind kind, ZoneKind zone, List<Point2> outline, double height = 1.2, double thickness = 0.2)
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
        };
        study.Regions.Add(region);
        return region;
    }

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
}
