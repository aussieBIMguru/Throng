using Throng.Analysis;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 024b: group × journey results (D-390) live, in batches and in journeys.csv; group tags in the snapshot shared
/// by mates (hover tethers, D-391); studies without groups unchanged.
/// </summary>
[TestClass]
public sealed class Round024bTests
{
    private static readonly Guid Family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);

    [TestMethod]
    public void GroupRows_TimeUntilTheLastMemberLeaves()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 20);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter), MixLine.OfGroup(Family)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 240);

        RunResults results = engine.CopyResults();
        GroupJourneyResult row = results.Groups.Single();
        Assert.AreEqual("Family (group)", row.Label);
        Assert.AreEqual(SimEngine.NoJourneyName, row.Journey);
        Assert.IsTrue(row.Finished > 0, "Families finished.");
        Assert.IsTrue(row.MeanSize is >= 2 and <= 5, $"Family groups are 2–5 people ({row.MeanSize:0.0}).");
        Assert.AreEqual(row.People, (long)Math.Round(row.MeanSize * row.Finished), "People = groups × mean size.");
        Assert.IsTrue(row.P95S >= row.MeanS * 0.5 && row.MeanS > 0, $"Times: mean {row.MeanS:0.0} s, 95th {row.P95S:0.0} s.");
        Assert.IsTrue(row.SpreadPct is >= 0 and <= 100, $"Spread {row.SpreadPct:0.0} %.");
        Assert.AreEqual(0L, row.Stuck);

        // A group takes at least as long as its slowest member's journey: the group mean can't beat the members' mean.
        double membersMean = results.Journeys.Where(j => !j.IsAll && j.Profile != "Commuter" && j.Finished > 0).Select(j => j.MeanS).DefaultIfEmpty(0).Min();
        Assert.IsTrue(row.MeanS >= membersMean - 1, $"Group {row.MeanS:0.0} s vs members {membersMean:0.0} s.");

        string[] csv = ResultsCsv.JourneysCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        StringAssert.EndsWith(csv[0], ",InProgress,GroupSize,People,SpreadS,SpreadPct");
        Assert.IsTrue(csv.Any(l => l.StartsWith("Family (group),", StringComparison.Ordinal)), "journeys.csv carries the group row.");
    }

    [TestMethod]
    public void StudiesWithoutGroups_HaveNoGroupRows_AndRunAsBefore()
    {
        var a = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 30)));
        var b = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 30)));
        Run(a, 90);
        Run(b, 90);
        Assert.AreEqual(0, a.CopyResults().Groups.Count);
        Assert.AreEqual(a.Stats, b.Stats, "Group results draw no random numbers.");
    }

    [TestMethod]
    public void SnapshotTags_AreSharedByMates_AndDifferBetweenGroups()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 30);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(Family)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 40);
        float[] buffer = [];
        int n = engine.WriteSnapshot(ref buffer);
        AgentStore agents = engine.Agents;
        var tagOf = new Dictionary<int, float>();
        for (int i = 0; i < n; i++)
        {
            float tag = buffer[(i * SimEngine.FloatsPerAgent) + 11];
            Assert.IsTrue(tag >= 1, "Everyone here walks with a group.");
            if (tagOf.TryGetValue(agents.Group[i], out float seen))
            {
                Assert.AreEqual(seen, tag, "Mates share their group's tag.");
            }
            else
            {
                tagOf[agents.Group[i]] = tag;
            }
        }

        Assert.IsTrue(tagOf.Count >= 2, $"{tagOf.Count} groups in the study.");
        Assert.AreEqual(tagOf.Count, tagOf.Values.Distinct().Count(), "Each group its own tag.");
    }

    [TestMethod]
    [TestCategory("Slow")] // long run: skipped by publish.ps1 unless -AllTests (D-430)
    public void Batch_AveragesGroupRows_AndWritesThemToJourneysCsv()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 20, seed: 3);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(Family)] };
        SimModel model = SimTestStudies.Compile(study);
        BatchResults batch = new BatchRunner(model, new BatchOptions(3, 3, 180, Parallelism: 2)).Run();
        GroupJourneyBatchResult row = batch.Groups.Single();
        Assert.AreEqual("Family (group)", row.Label);
        Assert.AreEqual(3, row.Finished.Runs, "Counts cover every run.");
        Assert.IsTrue(row.Finished.Mean > 0);
        Assert.AreEqual(1, batch.Mean.Groups.Count, "The mean results carry the group row for the overlay and tables.");
        Assert.IsTrue(BatchCsv.JourneysCsv(batch).Contains("Family (group),", StringComparison.Ordinal));
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS, MidpointRounding.AwayFromZero);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }
    }
}
