using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Stress (frustration) per agent (D-145).</summary>
[TestClass]
public sealed class StressTests
{
    [TestMethod]
    public void Stress_ZeroUntilHalfPatience_ThenLinearToOne()
    {
        Assert.AreEqual(0.0, SimEngine.StressFrom(0, 180), 1e-9);
        Assert.AreEqual(0.0, SimEngine.StressFrom(90, 180), 1e-9);
        Assert.AreEqual(0.5, SimEngine.StressFrom(135, 180), 1e-9);
        Assert.AreEqual(1.0, SimEngine.StressFrom(180, 180), 1e-9);
        Assert.AreEqual(1.0, SimEngine.StressFrom(1000, 180), 1e-9);
    }

    [TestMethod]
    public void HeldTime_BuildsFasterForLowTolerance_AndDrainsSlowly()
    {
        Assert.AreEqual(1.0, SimEngine.HeldStep(0, heldUp: true, densityTolerance: 0.5, dt: 1), 1e-9);
        Assert.AreEqual(1.5, SimEngine.HeldStep(0, heldUp: true, densityTolerance: 0.0, dt: 1), 1e-9);
        Assert.AreEqual(0.5, SimEngine.HeldStep(0, heldUp: true, densityTolerance: 1.0, dt: 1), 1e-9);
        Assert.AreEqual(9.5, SimEngine.HeldStep(10, heldUp: false, densityTolerance: 0.5, dt: 1), 1e-9);
        Assert.AreEqual(0.0, SimEngine.HeldStep(0.2, heldUp: false, densityTolerance: 0.5, dt: 1), 1e-9);
    }

    [TestMethod]
    public void Stress_Recovers_AfterFreeFlow()
    {
        double held = 0;
        for (int i = 0; i < 1500; i++)
        {
            held = SimEngine.HeldStep(held, heldUp: true, densityTolerance: 0.5, dt: 0.1); // 150 s held up
        }

        Assert.IsTrue(SimEngine.StressFrom(held, 180) > 0.6);
        for (int i = 0; i < 1200; i++)
        {
            held = SimEngine.HeldStep(held, heldUp: false, densityTolerance: 0.5, dt: 0.1); // 120 s walking freely
        }

        Assert.AreEqual(0.0, SimEngine.StressFrom(held, 180), 1e-9);
    }

    [TestMethod]
    public void FreeFlowCorridor_StaysCalm()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 30)));
        for (int i = 0; i < 600; i++)
        {
            engine.Step();
        }

        for (int i = 0; i < engine.Agents.Count; i++)
        {
            Assert.AreEqual(0f, engine.Agents.Stress[i], $"Agent {i} is stressed in a free-flowing corridor.");
        }
    }
}
