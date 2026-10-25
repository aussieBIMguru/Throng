using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 007 part 2: rate curves scale emission over the run (D-129).</summary>
[TestClass]
public sealed class RateCurveSimTests
{
    [TestMethod]
    public void CurveOfTwo_DoublesArrivals()
    {
        long flat = Spawned(SimTestStudies.Corridor(20), 120);
        long doubled = Spawned(WithCurve(SimTestStudies.Corridor(20), [RateCurvePoint.At(0, 2)]), 120);
        Assert.AreEqual(40.0, flat, 2.0, "Flat: 20/min for two minutes.");
        Assert.AreEqual(80.0, doubled, 2.0, "×2 all the way through.");
    }

    [TestMethod]
    public void CurveDroppingToZero_StopsEmission()
    {
        // 1× for the first minute, then down to nothing over six seconds.
        Study study = WithCurve(SimTestStudies.Corridor(20), [RateCurvePoint.At(1, 1), RateCurvePoint.At(1.1, 0)]);
        long spawned = Spawned(study, 180);
        Assert.AreEqual(21.0, spawned, 2.0, "About 20 in the first minute, one more on the ramp down, then no one.");
    }

    private static Study WithCurve(Study study, List<RateCurvePoint> curve)
    {
        Region spawn = study.Regions.First(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { RateCurve = curve };
        return study;
    }

    private static long Spawned(Study study, double seconds)
    {
        var engine = new SimEngine(SimTestStudies.Compile(study));
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }

        return engine.Stats.Spawned;
    }
}
