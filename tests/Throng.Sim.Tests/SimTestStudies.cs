using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Sim.Tests;

/// <summary>Small studies for the movement tests.</summary>
internal static class SimTestStudies
{
    public static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    /// <summary>A 30 × 4 m corridor on one level: a spawn at the west end, a sink at the east end.</summary>
    public static Study Corridor(double ratePerMinute = 20, EmissionPattern pattern = EmissionPattern.Constant, int seed = 7)
    {
        var study = new Study { Name = "Corridor" };
        study.Settings.Seed = seed;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(30, 4)) });
        Region sink = Zone(study, level, "East", ZoneKind.Sink, new(28, 0), new(30, 4));
        Region spawn = Zone(study, level, "West", ZoneKind.Spawn, new(0, 0), new(2, 4));
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(Commuter)],
            RatePerMinute = ratePerMinute,
            Pattern = pattern,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = sink.Id, Weight = 1 }],
        });
        return study;
    }

    /// <summary>
    /// Two 40 × 20 m floors, 5 m apart, joined by one connector placed from <paramref name="presetKey"/> at (10, 10)
    /// running east. People appear at the west end of the ground floor and leave at the east end of the upper floor.
    /// </summary>
    public static Study TwoLevels(string presetKey, double ratePerMinute)
    {
        var study = new Study { Name = "Two levels" };
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var ground = new Level { Name = "Ground", ElevationM = 0, HeightM = 5 };
        var upper = new Level { Name = "Upper", ElevationM = 5, HeightM = 5 };
        study.Levels.AddRange([ground, upper]);
        study.Regions.Add(new Region { LevelId = ground.Id, Name = "Ground floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 20)) });
        study.Regions.Add(new Region { LevelId = upper.Id, Name = "Upper floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 20)) });

        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey(presetKey)!;
        study.Connectors.AddRange(ConnectorFactory.FromTemplate(study, template, ground.Id, upper.Id, new Point2(10, 10), 0, TurnSide.Left));

        Region sink = Zone(study, upper, "Exit", ZoneKind.Sink, new(37, 0), new(40, 20));
        Region spawn = Zone(study, ground, "Entry", ZoneKind.Spawn, new(0, 0), new(3, 20));
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(Commuter)],
            RatePerMinute = ratePerMinute,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = sink.Id, Weight = 1 }],
        });
        return study;
    }

    public static Region Zone(Study study, Level level, string name, ZoneKind kind, Point2 a, Point2 b)
    {
        var region = new Region { LevelId = level.Id, Name = name, Kind = RegionKind.Zone, ZoneKind = kind, Outline = ShapeFactory.Rectangle(a, b) };
        study.Regions.Add(region);
        return region;
    }

    /// <summary>Compiles a study, failing the test with the findings when it can't run.</summary>
    public static SimModel Compile(Study study)
    {
        SimSetupResult result = SimSetup.Build(study);
        Assert.IsTrue(result.IsSuccess, string.Join("\n", result.Findings.Select(f => $"[{f.Severity}] {f.Message}")));
        return result.Model!;
    }
}
