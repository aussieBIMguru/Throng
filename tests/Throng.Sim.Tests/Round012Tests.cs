using Throng.Analysis;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 012: measurement zone stats (D-228), journey stats and the warm-up rule (D-229–D-231), CSVs (D-233).</summary>
[TestClass]
public sealed class Round012Tests
{
    [TestMethod]
    public void OrientedBounds_ShortSide_OfARotatedRectangle()
    {
        // A 6 × 2 m rectangle turned 30°: the narrower side is 2 m whatever the rotation.
        double a = Math.PI / 6;
        var u = new Point2(Math.Cos(a), Math.Sin(a));
        var v = new Point2(-u.Y, u.X);
        Point2 o = new(3, 4);
        List<Point2> outline = [o, o + (u * 6), o + (u * 6) + (v * 2), o + (v * 2)];
        OrientedBounds box = OrientedBounds.Of(outline);
        Assert.AreEqual(6, box.LongSideM, 1e-9);
        Assert.AreEqual(2, box.ShortSideM, 1e-9);
        Assert.AreEqual(0, OrientedBounds.Of([o]).ShortSideM);
    }

    [TestMethod]
    public void Zone_CountsOccupancyAndEntries_OnTheCorridor()
    {
        Study study = SimTestStudies.Corridor(60);
        Level level = study.Levels[0];
        SimTestStudies.Zone(study, level, "Count", ZoneKind.Measurement, new(14, 0), new(16, 4));
        SimModel model = SimTestStudies.Compile(study);
        Assert.AreEqual(1, model.Zones.Count);
        Assert.AreEqual(2, model.Zones[0].WidthM, 1e-9, "Narrower side of a 2 × 4 m zone.");
        Assert.AreEqual(8, model.Zones[0].AreaM2, 1e-9);

        var engine = new SimEngine(model);
        Run(engine, 120);
        RunResults r = engine.CopyResults();
        ZoneResult zone = r.Zones.Single();
        long arrived = engine.Stats.Arrived;
        Assert.IsTrue(arrived > 40, $"{arrived} arrived.");
        Assert.IsTrue(zone.Entries >= arrived, $"Everyone who reached the exit walked through the zone ({zone.Entries} entries, {arrived} arrived).");
        Assert.IsTrue(zone.Entries <= engine.Stats.Spawned * 1.1, $"Roughly one entry each ({zone.Entries} for {engine.Stats.Spawned} spawned).");
        Assert.IsTrue(zone.PeakOccupancy >= 1 && zone.MeanOccupancy > 0 && zone.MeanOccupancy <= zone.PeakOccupancy);
        Assert.AreEqual(zone.EntriesPerMin / 2, zone.EntriesPerMinPerM, 1e-9);
        Assert.AreEqual(zone.Entries / (r.MeasuredS / 60), zone.EntriesPerMin, 1e-6);
        Assert.AreEqual(r.SampleCount, r.ZoneOccupancy.Length, "One occupancy value per sample per zone (D-233).");

        int inside = 0;
        for (int i = 0; i < engine.Agents.Count; i++)
        {
            if (engine.Agents.X[i] is >= 14 and < 16)
            {
                inside++;
            }
        }

        Assert.AreEqual(inside, r.ZoneAt(r.SampleCount - 1, 0), 3, "The last sample matches who is in the zone now.");

        engine.Reset();
        RunResults cleared = engine.CopyResults();
        Assert.AreEqual(0, cleared.Zones.Single().Entries, "Reset clears the zone counts.");
        Assert.AreEqual(0, cleared.ZoneOccupancy.Length);
    }

    [TestMethod]
    public void Zone_EntriesWaitForTheWarmUp()
    {
        Study study = SimTestStudies.Corridor(60);
        study.Settings.WarmUpS = 60;
        SimTestStudies.Zone(study, study.Levels[0], "Count", ZoneKind.Measurement, new(14, 0), new(16, 4));
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 50);
        RunResults early = engine.CopyResults();
        Assert.AreEqual(0, early.Zones.Single().Entries, "Nothing counted during the warm-up.");
        Assert.AreEqual(0, early.Zones.Single().PeakOccupancy);
        Assert.IsTrue(early.ZoneOccupancy.Max() > 0, "The occupancy series records from the start.");
    }

    [TestMethod]
    public void Journeys_CountPeopleSpawnedAfterTheWarmUp()
    {
        Study study = SimTestStudies.Corridor(60);
        study.Settings.WarmUpS = 30;
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 30);
        long before = engine.Stats.Spawned;
        Run(engine, 90);
        RunResults r = engine.CopyResults();

        JourneyResult row = r.Journeys.Single();
        Assert.AreEqual(SimEngine.NoJourneyName, row.Journey, "Straight to a sink: the (no journey) row.");
        Assert.IsFalse(row.IsAll, "One journey, so no ALL row.");
        Assert.AreEqual(engine.Stats.Spawned - before, row.Finished + row.Stuck + row.InProgress, "Spawned after the warm-up = finished + stuck + in progress (D-230).");
        Assert.IsTrue(row.Finished > 30, $"{row.Finished} finished.");
        Assert.IsTrue(row.MeanS is > 15 and < 40, $"About 28 m at walking pace: mean {row.MeanS:0.0} s.");
        Assert.IsTrue(row.P95S >= row.MeanS * 0.9);
        Assert.AreEqual(100, row.WithinAcceptablePct, 1e-9, "Well within the commuter's acceptable time.");
        Assert.AreEqual(0, row.LongWaitPct, 1e-9, "No queues in a corridor.");
    }

    [TestMethod]
    public void Journeys_RowPerJourney_AndAnAllRow_WithLongWaits()
    {
        Study study = Round009bTests.WideGates(SimTestStudies.Commuter, 40);
        study.Settings.LongWaitS = 0.001;
        Region west = study.Regions.Single(r => r.Name == "West");
        EmissionRule rule = west.EmissionRules[0];
        west.EmissionRules[0] = rule with { Mix = [.. rule.Mix, MixLine.Of(SimTestStudies.Commuter)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 180);
        RunResults r = engine.CopyResults();

        Assert.AreEqual(3, r.Journeys.Count, string.Join(", ", r.Journeys.Select(j => j.Journey)));
        JourneyResult direct = r.Journeys[0], gates = r.Journeys[1], all = r.Journeys[2];
        Assert.AreEqual(SimEngine.NoJourneyName, direct.Journey, "No journey sorts first.");
        Assert.IsTrue(all.IsAll && all.Journey == SimEngine.AllJourneysName);
        Assert.AreEqual(direct.Finished + gates.Finished, all.Finished);
        Assert.AreEqual(direct.InProgress + gates.InProgress, all.InProgress);
        Assert.IsTrue(gates.Finished > 10 && direct.Finished > 10, $"gates {gates.Finished}, direct {direct.Finished}");
        Assert.IsTrue(gates.LongWaitPct > 0, "Through the gates, everyone waits a moment, which is over a 1 ms Long wait.");
        Assert.AreEqual(0, direct.LongWaitPct, 1e-9, "Walking straight out, no one queues.");
        Assert.AreEqual(0.001, r.LongWaitS, 1e-12);
    }

    [TestMethod]
    public void Csv_ZonesJourneysAndZoneColumns()
    {
        var results = new RunResults
        {
            WarmUpS = 0,
            MeasuredS = 60,
            LongWaitS = 120,
            Devices = [],
            DeviceNames = [],
            Times = [5, 10],
            InModel = [3, 6],
            ArrivalsPerMin = [36, 36],
            ExitsPerMin = [0, 12],
            PeakDensity = [0.5f, 1.25f],
            Queues = [],
            Zones = [new ZoneResult(0, "Gate line, count", 12, 2, 5, 2.25, 30, 30, 15)],
            ZoneNames = ["Gate line, count"],
            ZoneOccupancy = [2, 5],
            Journeys = [new JourneyResult("Commuter", "Through gates", false, 20, 61.24, 90, 600, 95, 10, 1, 0, 4)],
        };

        string[] zones = ResultsCsv.ZonesCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("Zone,AreaM2,WidthM,PeakOccupancy,MeanOccupancy,PeakDensityPm2,Entries,EntriesPerMin,EntriesPerMinPerM", zones[0]);
        Assert.AreEqual("\"Gate line, count\",12.0,2.00,5,2.3,0.42,30,30.0,15.0", zones[1]);

        string[] journeys = ResultsCsv.JourneysCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("Profile,Journey,Finished,MeanS,P95S,AcceptableS,WithinAcceptablePct,LongWaitS,LongWaitPct,GaveUp,Stuck,InProgress", journeys[0]);
        Assert.AreEqual("Commuter,Through gates,20,61.2,90.0,600,95.0,120,10.0,1,0,4", journeys[1]);

        string[] series = ResultsCsv.TimeSeriesCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("TimeS,WarmUp,InModel,ArrivalsPerMin,ExitsPerMin,PeakDensityPm2,\"Zone Gate line, count\"", series[0]);
        Assert.AreEqual("10.0,0,6,36.0,12.0,1.25,5", series[2]);
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
        }
    }
}
