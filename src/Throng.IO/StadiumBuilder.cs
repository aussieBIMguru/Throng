using System.Globalization;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Builds the stadium demos (round 025, D-379, D-381, D-404): one concourse sector of about 5,000 seats, copied side by
/// side (six sectors ≈ 30,000 seats) to test stadium-scale crowds. Each sector has a gate plaza on the ground with three
/// turnstile lines, two 3.6 m stairs and an escalator pair up to a concourse 5 m above, food and toilet zones there, and
/// three vomitories through the bowl wall to seat-block zones. Fans arrive in an event surge over the first hour and
/// leave from minute 70 within ten minutes.
/// </summary>
/// <remarks>
/// The bowl is unrolled into a straight row of sectors (X east, Y north into the bowl): it keeps the concourse long and
/// the geometry simple, while every sector carries its own share of the crowd. No step-free route is modelled (no ramp
/// or lift), so the mix has no wheelchair users.
/// </remarks>
public static class StadiumBuilder
{
    /// <summary>Display name of the six-sector demo.</summary>
    public const string DemoName = "Demo: Stadium";

    /// <summary>Display name of the one-sector demo.</summary>
    public const string SectorDemoName = "Demo: Stadium sector";

    /// <summary>Sectors in the full demo (D-404).</summary>
    public const int FullSectors = 6;

    /// <summary>Seats per sector, about (spread over its three blocks).</summary>
    public const int SeatsPerSector = 5000;

    /// <summary>Width of a sector along the concourse, metres.</summary>
    public const double SectorWidthM = 60;

    /// <summary>Concourse height above the plaza, metres.</summary>
    public const double ConcourseM = 5.0;

    /// <summary>Zone tag of the food concessions (the ingress journey's dwell).</summary>
    public const string FoodTag = "Food";

    /// <summary>Zone tag of the toilet blocks.</summary>
    public const string ToiletTag = "Toilets";

    /// <summary>Key of the ingress journey the demo embeds.</summary>
    public const string IngressJourneyKey = "journey.stadium-in";

    /// <summary>Minute the egress starts in <see cref="StadiumScenario.InAndOut"/>.</summary>
    public const double EgressStartMin = 70;

    /// <summary>Minutes over which every seat block empties.</summary>
    public const double EgressOverMin = 10;

    /// <summary>Fixed seed so runs are reproducible.</summary>
    public const int DemoSeed = 20261105;

    // Ingress rate per sector: the event-surge curve integrates to about 50 rate-minutes over the hour.
    private const double SurgeRateMinutes = 50.2;

    /// <summary>The six-sector stadium (≈ 30,000 seats).</summary>
    public static Study Build() => Build(FullSectors, StadiumScenario.InAndOut);

    /// <summary>One sector (≈ 5,000 seats), light enough to watch live.</summary>
    public static Study BuildSector() => Build(1, StadiumScenario.InAndOut);

    /// <summary>Builds a stadium of <paramref name="sectors"/> sectors (1–12) running <paramref name="scenario"/>.</summary>
    public static Study Build(int sectors, StadiumScenario scenario)
    {
        sectors = Math.Clamp(sectors, 1, 12);
        CultureInfo c = CultureInfo.InvariantCulture;
        int seats = sectors * SeatsPerSector;
        var study = new Study
        {
            Name = sectors == 1 ? SectorDemoName : DemoName,
            Description = string.Create(c,
                $"{sectors} stadium sector{(sectors == 1 ? string.Empty : "s")} (about {seats:N0} seats): gate plazas with turnstiles, stairs and escalators up to the concourse, food and toilets, vomitories to the seat blocks. Fans arrive in an event surge over the first hour and leave from minute {EgressStartMin:0}."),
            Settings = new StudySettings
            {
                Seed = DemoSeed,
                StartTimeOfDay = new TimeSpan(18, 0, 0),
                EndCondition = StudyEndCondition.FixedDuration,
                DurationS = scenario == StadiumScenario.Egress ? 1200 : 5400,
                WarmUpS = scenario == StadiumScenario.Egress ? 0 : 300,
                LongWaitS = 300,
            },
        };

        var plaza = new Level { Name = "Plaza", ElevationM = 0, HeightM = ConcourseM };
        var concourse = new Level { Name = "Concourse", ElevationM = ConcourseM, HeightM = 4.0 };
        study.Levels.Add(plaza);
        study.Levels.Add(concourse);
        EmbedTemplates(study);
        JourneyTemplate ingressJourney = AddIngressJourney(study);

        for (int s = 0; s < sectors; s++)
        {
            BuildSector(study, plaza, concourse, s, scenario, ingressJourney.Id);
        }

        return study;
    }

    /// <summary>One sector at x = <paramref name="index"/> × 60 m with its plaza, gates, vertical, concourse and seats.</summary>
    private static void BuildSector(Study study, Level plaza, Level concourse, int index, StadiumScenario scenario, Guid ingressJourney)
    {
        double x0 = index * SectorWidthM;
        string name = ((char)('A' + index)).ToString();

        // Plaza: the gate approach (y 0–40), under the concourse from y 20.
        Add(study, plaza, $"Plaza {name}", RegionKind.Walkable, ZoneKind.None, Rect(x0, 0, x0 + SectorWidthM, 40));
        Region entry = Add(study, plaza, $"Gate plaza {name}", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x0 + 5, 0, x0 + 55, 2));
        foreach (double gx in new[] { 12.0, 30.0, 48.0 })
        {
            PlaceDevice(study, "device.gate-line", plaza, new Point2(x0 + gx, 12), 90);
        }

        // Up to the concourse: two wide stairs and an escalator pair, all running north.
        Place(study, "connector.stair-3600", plaza, concourse, new Point2(x0 + 8, 22), $"Stair {name}1");
        Place(study, "connector.stair-3600", plaza, concourse, new Point2(x0 + 52, 22), $"Stair {name}2");
        Place(study, "connector.escalator-pair", plaza, concourse, new Point2(x0 + 30, 22), $"Escalators {name}");

        // Concourse (y 20–60): food each end, toilets in the middle, the bowl wall at y 56–57 with three vomitories.
        Add(study, concourse, $"Concourse {name}", RegionKind.Walkable, ZoneKind.None, Rect(x0, 20, x0 + SectorWidthM, 60));
        Add(study, concourse, $"Food {name}1", RegionKind.Zone, ZoneKind.None, Rect(x0 + 2, 44, x0 + 14, 50), tag: FoodTag);
        Add(study, concourse, $"Food {name}2", RegionKind.Zone, ZoneKind.None, Rect(x0 + 46, 44, x0 + 58, 50), tag: FoodTag);
        Add(study, concourse, $"Toilets {name}", RegionKind.Zone, ZoneKind.None, Rect(x0 + 22, 46, x0 + 38, 52), tag: ToiletTag);
        (double From, double To)[] wall = [(0, 9.5), (13.5, 28), (32, 46.5), (50.5, 60)];
        foreach ((double from, double to) in wall)
        {
            Add(study, concourse, $"Bowl wall {name}", RegionKind.Obstacle, ZoneKind.None, Rect(x0 + from, 56, x0 + to, 57), height: 2.5);
        }

        var blocks = new List<Region>
        {
            Add(study, concourse, $"Seats {name}1", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x0 + 1, 57.5, x0 + 19, 60)),
            Add(study, concourse, $"Seats {name}2", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x0 + 21, 57.5, x0 + 39, 60)),
            Add(study, concourse, $"Seats {name}3", RegionKind.Zone, ZoneKind.SpawnSink, Rect(x0 + 41, 57.5, x0 + 59, 60)),
        };

        Guid fan = TemplateIds.FromKey("agent.fan");
        Guid fans = TemplateIds.FromKey("group.fans");
        Guid family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);
        if (scenario != StadiumScenario.Egress)
        {
            // About 5,000 people per sector over the hour; 30 % stop for food, 15 % for the toilets (spawn tags).
            SpawnTag[] tags = [SpawnTag.Of("hungry", 30), SpawnTag.Of("loo", 15)];
            entry.EmissionRules.Add(new EmissionRule
            {
                Mix =
                [
                    MixLine.Of(fan, ingressJourney, 70, tags),
                    MixLine.OfGroup(fans, ingressJourney, 20, tags),
                    MixLine.OfGroup(family, ingressJourney, 10, tags),
                ],
                RatePerMinute = Math.Round(SeatsPerSector / SurgeRateMinutes),
                Pattern = EmissionPattern.Poisson,
                RateCurve = RateCurves.PointsOf(RateCurvePreset.EventSurge),
                DestinationMode = DestinationMode.ChosenSinks,
                Destinations = [.. blocks.Select(b => new SinkWeight { SinkId = b.Id, Weight = 1 })],
            });
        }

        if (scenario != StadiumScenario.Ingress)
        {
            // Every block empties over ten minutes, straight out to the gate plaza.
            double start = scenario == StadiumScenario.Egress ? 0 : EgressStartMin;
            List<RateCurvePoint> curve = start <= 0
                ? [RateCurvePoint.At(0, 1), RateCurvePoint.At(EgressOverMin, 1), RateCurvePoint.At(EgressOverMin + 0.5, 0)]
                : [RateCurvePoint.At(0, 0), RateCurvePoint.At(start - 0.5, 0), RateCurvePoint.At(start, 1), RateCurvePoint.At(start + EgressOverMin, 1), RateCurvePoint.At(start + EgressOverMin + 0.5, 0)];
            double perBlock = SeatsPerSector / 3.0 / EgressOverMin;
            foreach (Region block in blocks)
            {
                block.EmissionRules.Add(new EmissionRule
                {
                    Mix = [MixLine.Of(fan, null, 75), MixLine.OfGroup(fans, null, 15), MixLine.OfGroup(family, null, 10)],
                    RatePerMinute = Math.Round(perBlock),
                    Pattern = EmissionPattern.Poisson,
                    RateCurve = curve,
                    DestinationMode = DestinationMode.ChosenSinks,
                    Destinations = [new SinkWeight { SinkId = entry.Id, Weight = 1 }],
                });
            }
        }
    }

    /// <summary>The ingress journey: through a turnstile, food and toilets for those who want them, then to the seats.</summary>
    private static JourneyTemplate AddIngressJourney(Study study)
    {
        var journey = new JourneyTemplate
        {
            Id = TemplateIds.FromKey(IngressJourneyKey),
            Key = IngressJourneyKey,
            Name = "Turnstile, food, toilets, seat",
            Category = BuiltInLibrary.Categories.Journeys,
            ColorHex = "#F87171",
            Description = "Through a turnstile line, then food (if hungry) and the toilets (if needed) on the concourse, then to the seat block.",
            Source = TemplateSource.User,
            Steps =
            [
                JourneyStep.Device("Gate"),
                JourneyStep.DwellIn(FoodTag, Distribution.Uniform(120, 300), needs: "hungry"),
                JourneyStep.DwellIn(ToiletTag, Distribution.Uniform(60, 180), needs: "loo"),
                JourneyStep.ExitStep(),
            ],
        };
        JourneyGraph.Ensure(journey);
        study.Library.Journeys ??= [];
        study.Library.Journeys.Add(journey);
        return journey;
    }

    private static void EmbedTemplates(Study study)
    {
        foreach (string key in new[] { "agent.fan", "agent.infrequent", "agent.child", "group.fans", BuiltInLibrary.FamilyGroupKey })
        {
            if (BuiltInLibrary.Instance.FindByKey(key) is { } template)
            {
                if (template is GroupTemplate)
                {
                    study.Library.Groups ??= [];
                }

                study.Library.AddCopy(template, TemplateSource.Embedded);
            }
        }
    }

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

    /// <summary>Places a built-in connector preset running north from <paramref name="at"/>; pairs get "(up)" / "(down)".</summary>
    private static void Place(Study study, string presetKey, Level bottom, Level top, Point2 at, string name)
    {
        if (BuiltInLibrary.Instance.FindByKey(presetKey) is not ConnectorTemplate template)
        {
            return;
        }

        List<Connector> placed = ConnectorFactory.FromTemplate(study, template, bottom.Id, top.Id, at, angleDeg: 90, TurnSide.Left);
        TemplateBase embedded = study.Library.AddCopy(template, TemplateSource.Embedded);
        foreach (Connector connector in placed)
        {
            connector.Name = placed.Count == 1 ? name : $"{name} ({(connector.Direction == ConnectorDirection.Down ? "down" : "up")})";
            connector.TemplateId = embedded.Id;
        }

        study.Connectors.AddRange(placed);
    }

    private static Region Add(Study study, Level level, string name, RegionKind kind, ZoneKind zone, List<Point2> outline, double height = 1.2, string? tag = null)
    {
        var region = new Region
        {
            LevelId = level.Id,
            Name = name,
            Kind = kind,
            ZoneKind = zone,
            Outline = outline,
            HeightM = height,
            Tag = tag,
        };
        study.Regions.Add(region);
        return region;
    }

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
}
