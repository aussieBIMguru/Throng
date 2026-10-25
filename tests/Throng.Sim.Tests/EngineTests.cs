using Throng.Core.Model;
using Throng.IO;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

[TestClass]
public sealed class EngineTests
{
    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }
    }

    [TestMethod]
    public void Corridor_AgentsWalkToTheSink()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 20)));
        Run(engine, 120);
        SimStats stats = engine.Stats;
        Assert.IsTrue(stats.Spawned >= 38, $"Expected about 40 emitted, got {stats.Spawned}.");
        Assert.IsTrue(stats.Arrived >= 25, $"Most should have walked 26 m in two minutes; {stats.Arrived} arrived.");
        Assert.AreEqual(0, stats.StuckRemoved);

        // 26 m at about 1.45 m/s is roughly 18 s.
        Assert.IsTrue(stats.MeanJourneyS is > 12 and < 35, $"Mean journey {stats.MeanJourneyS:0.0} s.");
    }

    [TestMethod]
    public void SameSeed_SameRun()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson));
        var a = new SimEngine(model);
        var b = new SimEngine(model);
        Run(a, 60);
        Run(b, 60);
        Assert.AreEqual(a.Stats, b.Stats);
        Assert.AreEqual(a.Agents.Count, b.Agents.Count);
        for (int i = 0; i < a.Agents.Count; i++)
        {
            Assert.AreEqual(a.Agents.X[i], b.Agents.X[i]);
            Assert.AreEqual(a.Agents.Y[i], b.Agents.Y[i]);
        }

        // Reset replays the same run.
        SimStats first = a.Stats;
        a.Reset();
        Run(a, 60);
        Assert.AreEqual(first, a.Stats);
    }

    [TestMethod]
    public void Poisson_RateIsAboutRight()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 30, pattern: EmissionPattern.Poisson, seed: 11)));
        Run(engine, 1200);
        SimStats stats = engine.Stats;
        long emitted = stats.Spawned + stats.Waiting;
        Assert.AreEqual(600, emitted, 75, "30 a minute for 20 minutes, within about three standard deviations.");
    }

    [TestMethod]
    public void Stair_CarriesPeopleUpALevel()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.stair-2400", 20)));
        Run(engine, 180);
        Assert.IsTrue(engine.Stats.Arrived > 20, $"Only {engine.Stats.Arrived} reached the upper floor.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved);
    }

    [TestMethod]
    public void Escalator_StepLoadingCapsThroughput()
    {
        // 200 a minute asked for; the escalator takes 100 a minute at most (D-107).
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.escalator-up", 200)));
        Run(engine, 240);
        long arrived = engine.Stats.Arrived;
        Assert.IsTrue(arrived > 100, $"Only {arrived} rode up in four minutes.");
        Assert.IsTrue(arrived <= 100 * 4 + 5, $"{arrived} rode up in four minutes; the cap is 100 a minute.");
    }

    [TestMethod]
    public void Demo_PeopleReachThePlatform()
    {
        var engine = new SimEngine(SimTestStudies.Compile(DemoStudyBuilder.Build()));
        Run(engine, 300);
        SimStats stats = engine.Stats;
        Assert.IsTrue(stats.Spawned > 200, $"Emitted {stats.Spawned}.");
        Assert.IsTrue(stats.Arrived > 50, $"Only {stats.Arrived} arrived in five minutes.");
        Assert.IsTrue(stats.StuckRemoved < stats.Arrived / 10 + 5, $"{stats.StuckRemoved} removed as stuck vs {stats.Arrived} arrived.");
    }

    [TestMethod]
    public void Snapshot_HasElevenFloatsPerAgent()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60)));
        Run(engine, 10);
        float[] buffer = [];
        int n = engine.WriteSnapshot(ref buffer);
        Assert.AreEqual(engine.Agents.Count, n);
        Assert.IsTrue(n > 0);
        Assert.IsTrue(buffer.Length >= n * SimEngine.FloatsPerAgent);
        Assert.AreEqual((float)engine.Agents.X[0], buffer[0]);
        Assert.AreEqual(0f, buffer[2], 1e-6f, "Ground floor height.");
        Assert.AreEqual(11, SimEngine.FloatsPerAgent, "x, y, z, heading, r, g, b, radius, height, stuck, stress (D-145).");
        Assert.AreEqual(engine.Agents.Stress[0], buffer[10]);
    }
}
