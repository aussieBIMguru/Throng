using Throng.Core.Model;

namespace Throng.Core.Tests;

/// <summary>Round 007 part 2: rate curve presets and evaluation (D-129).</summary>
[TestClass]
public sealed class RateCurveTests
{
    [TestMethod]
    public void NoPoints_IsFlat()
    {
        Assert.AreEqual(1.0, RateCurves.MultiplierAt([], 0));
        Assert.AreEqual(1.0, RateCurves.MultiplierAt([], 500));
        Assert.AreEqual(0, RateCurves.PointsOf(RateCurvePreset.Flat).Count);
    }

    [TestMethod]
    public void Interpolates_AndHoldsBothEnds()
    {
        // Given out of order, with one bad point that must be ignored.
        List<RateCurvePoint> curve = [RateCurvePoint.At(30, 3), RateCurvePoint.At(10, 1), RateCurvePoint.At(20, -5)];
        Assert.AreEqual(1.0, RateCurves.MultiplierAt(curve, 0), 1e-9, "Holds the first value before it.");
        Assert.AreEqual(2.0, RateCurves.MultiplierAt(curve, 20), 1e-9, "Straight line from 1 at 10 to 3 at 30.");
        Assert.AreEqual(3.0, RateCurves.MultiplierAt(curve, 90), 1e-9, "Holds the last value after it.");
    }

    [TestMethod]
    public void Presets_AreValidAndFreshCopies()
    {
        foreach (RateCurvePreset preset in RateCurves.All.Where(p => p != RateCurvePreset.Flat))
        {
            List<RateCurvePoint> points = RateCurves.PointsOf(preset);
            Assert.IsTrue(points.Count >= 2 && points.All(RateCurves.IsValid), RateCurves.NameOf(preset));
            points.Clear();
            Assert.AreNotEqual(0, RateCurves.PointsOf(preset).Count, "Editing a copy mustn't change the preset.");
        }

        Assert.AreEqual(1.8, RateCurves.MultiplierAt(RateCurves.PointsOf(RateCurvePreset.AmPeak), 30), 1e-9);
    }

    [TestMethod]
    public void CopyWithNewId_CopiesTheCurve()
    {
        var rule = new EmissionRule { RateCurve = RateCurves.PointsOf(RateCurvePreset.PmPeak) };
        EmissionRule copy = rule.CopyWithNewId();
        Assert.AreNotSame(rule.RateCurve, copy.RateCurve);
        CollectionAssert.AreEqual(rule.RateCurve, copy.RateCurve);
    }
}
