namespace Throng.Sim.Tests;

[TestClass]
public sealed class DeterministicRandomTests
{
    [TestMethod]
    public void SameSeed_SameSequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (int i = 0; i < 1000; i++)
        {
            Assert.AreEqual(a.NextUInt64(), b.NextUInt64());
        }
    }

    [TestMethod]
    public void KnownFirstValue_GuardsAgainstAlgorithmChanges()
    {
        // SplitMix64 reference: seed 0 produces 0xE220A8397B1DCDAF first.
        Assert.AreEqual(0xE220A8397B1DCDAFUL, new DeterministicRandom(0).NextUInt64());
    }

    [TestMethod]
    public void NextDouble_IsInUnitInterval()
    {
        var random = new DeterministicRandom(7);
        double sum = 0;
        for (int i = 0; i < 10000; i++)
        {
            double d = random.NextDouble();
            Assert.IsTrue(d is >= 0 and < 1);
            sum += d;
        }

        Assert.AreEqual(0.5, sum / 10000, 0.02, "Mean should be close to 0.5.");
    }

    [TestMethod]
    public void DerivedStreams_Differ()
    {
        ulong s0 = DeterministicRandom.DeriveSeed(1, 0);
        ulong s1 = DeterministicRandom.DeriveSeed(1, 1);
        Assert.AreNotEqual(s0, s1);
        Assert.AreEqual(s0, DeterministicRandom.DeriveSeed(1, 0));
    }

    [TestMethod]
    public void NextInt_StaysInRange()
    {
        var random = new DeterministicRandom(3);
        for (int i = 0; i < 1000; i++)
        {
            int v = random.NextInt(6);
            Assert.IsTrue(v is >= 0 and < 6);
        }

        Assert.AreEqual(0, random.NextInt(0));
    }
}
