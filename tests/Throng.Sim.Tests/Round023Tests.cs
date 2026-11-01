using Throng.Core.Model;
using Throng.Sim.Engine;
using Throng.Sim.Replay;

namespace Throng.Sim.Tests;

/// <summary>Round 023: replay recording (D-373, D-374) and stable agent serials.</summary>
[TestClass]
public sealed class Round023Tests
{
    [TestMethod]
    public void Frames_EveryHalfSecond_AndReadBackAsTheSnapshot()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60, pattern: EmissionPattern.Poisson, seed: 4));
        var engine = new SimEngine(model);
        var recording = new ReplayRecording();
        recording.CaptureIfDue(engine);
        float[] expected = [];
        int expectedCount = 0;
        while (engine.TimeS < 60 - 1e-9)
        {
            engine.Step();
            recording.CaptureIfDue(engine);
            if (Math.Abs(engine.TimeS - 30) < 1e-6)
            {
                expectedCount = engine.WriteSnapshot(ref expected);
            }
        }

        Assert.AreEqual(121, recording.FrameCount, "A frame at 0, 0.5 … 60 s.");
        Assert.AreEqual(0, recording.StartS, 1e-9);
        Assert.AreEqual(60, recording.EndS, 1e-6);
        Assert.IsTrue(expectedCount > 5, $"{expectedCount} people at 30 s.");

        float[] actual = [];
        Assert.IsTrue(recording.TryReadAgents(30, ref actual, out int count));
        Assert.AreEqual(expectedCount, count);
        const int F = SimEngine.FloatsPerAgent;
        for (int i = 0; i < count; i++)
        {
            int o = i * F;
            Assert.AreEqual(expected[o], actual[o], 1e-6, "x exact");
            Assert.AreEqual(expected[o + 1], actual[o + 1], 1e-6, "y exact");
            Assert.AreEqual(expected[o + 2], actual[o + 2], 0.01, "z at half precision");
            Assert.AreEqual(expected[o + 3], actual[o + 3], 0.01, "heading at half precision");
            Assert.AreEqual(expected[o + 4], actual[o + 4], 1e-6, "colour from the palette");
            Assert.AreEqual(expected[o + 7], actual[o + 7], 0.006, "radius to the centimetre");
            Assert.AreEqual(expected[o + 8], actual[o + 8], 0.011, "height to 2 cm");
            Assert.AreEqual(expected[o + 10], actual[o + 10], 0.003, "stress to 1/255");
        }

        ReplayState? state = recording.StateAt(30.2);
        Assert.IsNotNull(state);
        Assert.AreEqual(30.2, state!.TimeS, 1e-9);
        Assert.AreEqual(30.2, state.Stats.TimeS, 1e-9, "Counters carry the replayed time.");
    }

    [TestMethod]
    public void BetweenFrames_PeopleMovePartWay()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60, pattern: EmissionPattern.Poisson, seed: 5));
        var engine = new SimEngine(model);
        var recording = new ReplayRecording();
        for (int i = 0; i < 400; i++)
        {
            engine.Step();
            recording.CaptureIfDue(engine);
        }

        float[] a = [], m = [], b = [];
        recording.TryReadAgents(30, ref a, out int na);
        recording.TryReadAgents(30.25, ref m, out int nm);
        recording.TryReadAgents(30.5, ref b, out _);
        Assert.AreEqual(na, nm, "Between frames the earlier frame's people show.");
        const int F = SimEngine.FloatsPerAgent;
        int moved = 0;
        for (int i = 0; i < na; i++)
        {
            float x0 = a[i * F], x1 = m[i * F];
            if (Math.Abs(x1 - x0) > 1e-4)
            {
                moved++;
                Assert.IsTrue(x1 >= -0.5 && x1 <= 30.5, "Positions stay in the corridor.");
            }
        }

        Assert.IsTrue(moved > 0, "Walking people are part-way between frames.");
    }

    [TestMethod]
    public void OverTheCap_OlderHalfThins_EndsKept()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60, seed: 6));
        var engine = new SimEngine(model);
        var recording = new ReplayRecording(capBytes: 40_000);
        recording.CaptureIfDue(engine);
        for (int i = 0; i < 1200; i++)
        {
            engine.Step();
            recording.CaptureIfDue(engine);
        }

        Assert.IsTrue(recording.FrameCount < 241, $"{recording.FrameCount} frames kept of 241.");
        Assert.IsTrue(recording.Bytes <= 40_000 || recording.FrameCount < 4, $"{recording.Bytes} bytes.");
        Assert.AreEqual(0, recording.StartS, 1e-9, "The first frame stays.");
        Assert.IsTrue(recording.EndS <= engine.TimeS + 1e-9 && recording.EndS > engine.TimeS - ReplayRecording.FrameIntervalS, "The latest frame stays.");
    }

    [TestMethod]
    public void Recording_NeverChangesTheRun()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60, pattern: EmissionPattern.Poisson, seed: 7));
        var plain = new SimEngine(model);
        var recorded = new SimEngine(model);
        var recording = new ReplayRecording();
        for (int i = 0; i < 900; i++)
        {
            plain.Step();
            recorded.Step();
            recording.CaptureIfDue(recorded);
        }

        Assert.AreEqual(plain.Stats, recorded.Stats);
    }

    [TestMethod]
    public void Serials_AreUniqueAndSurviveRemovals()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 60, seed: 8));
        var engine = new SimEngine(model);
        for (int i = 0; i < 1500; i++)
        {
            engine.Step();
        }

        AgentStore agents = engine.Agents;
        Assert.IsTrue(engine.Stats.Arrived > 0, "Some people left (swap-removes happened).");
        var seen = new HashSet<int>();
        for (int i = 0; i < agents.Count; i++)
        {
            Assert.IsTrue(seen.Add(agents.Serial[i]), $"Serial {agents.Serial[i]} is unique.");
        }

        engine.Reset();
        Assert.AreEqual(0, engine.Agents.Count);
    }
}
