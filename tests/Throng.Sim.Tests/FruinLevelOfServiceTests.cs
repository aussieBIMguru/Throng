using Throng.Analysis;

namespace Throng.Sim.Tests;

/// <summary>Analysis tests live here alongside the engine tests (see BRIEF §2.1).</summary>
[TestClass]
public sealed class FruinLevelOfServiceTests
{
    [TestMethod]
    [DataRow(5.0, LevelOfService.A)]
    [DataRow(3.0, LevelOfService.B)]
    [DataRow(2.0, LevelOfService.C)]
    [DataRow(1.0, LevelOfService.D)]
    [DataRow(0.6, LevelOfService.E)]
    [DataRow(0.3, LevelOfService.F)]
    public void Walkway_Grades(double areaPerPed, LevelOfService expected)
    {
        Assert.AreEqual(expected, FruinLevelOfService.FromAreaPerPedestrian(areaPerPed, LosAreaType.Walkway));
    }

    [TestMethod]
    public void TablesAreAppliedPerAreaType()
    {
        // The same 1.0 m²/ped grades differently on each Fruin table.
        Assert.AreEqual(LevelOfService.D, FruinLevelOfService.FromAreaPerPedestrian(1.0, LosAreaType.Walkway));
        Assert.AreEqual(LevelOfService.C, FruinLevelOfService.FromAreaPerPedestrian(1.0, LosAreaType.Stair));
        Assert.AreEqual(LevelOfService.B, FruinLevelOfService.FromAreaPerPedestrian(1.0, LosAreaType.Queue));
    }

    [TestMethod]
    public void Density_ZeroIsA_AndHighIsF()
    {
        Assert.AreEqual(LevelOfService.A, FruinLevelOfService.FromDensity(0, LosAreaType.Walkway));
        Assert.AreEqual(LevelOfService.F, FruinLevelOfService.FromDensity(4, LosAreaType.Walkway));
    }

    [TestMethod]
    public void AbstractLabels()
    {
        Assert.AreEqual("Low", FruinLevelOfService.AbstractLabel(LevelOfService.A));
        Assert.AreEqual("Medium", FruinLevelOfService.AbstractLabel(LevelOfService.D));
        Assert.AreEqual("High", FruinLevelOfService.AbstractLabel(LevelOfService.E));
        Assert.AreEqual("Critical", FruinLevelOfService.AbstractLabel(LevelOfService.F));
    }
}
