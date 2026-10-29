using Throng.Analysis;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 011b: cumulative maps (D-216–D-218) and the hard accessible-device rule (D-213, D-223–D-225).</summary>
[TestClass]
public sealed class Round011bTests
{
    private static readonly Guid Mobility = TemplateIds.FromKey(BuiltInLibrary.MobilityProfileKey);

    [TestMethod]
    public void LosEOrWorseDensity_AgreesWithTheFruinTables()
    {
        foreach (DensityCellKind kind in new[] { DensityCellKind.Walkway, DensityCellKind.Queue, DensityCellKind.Stair })
        {
            double e = DensityGrid.LosEOrWorseDensity(kind);
            LosAreaType type = HeatmapPainter.AreaType(kind);
            Assert.IsTrue(FruinLevelOfService.FromDensity(e * 1.001, type) >= LevelOfService.E, $"{kind}: just above is E or worse.");
            Assert.IsTrue(FruinLevelOfService.FromDensity(e * 0.99, type) < LevelOfService.E, $"{kind}: just below is D or better.");
        }

        Assert.IsTrue(double.IsPositiveInfinity(DensityGrid.LosEOrWorseDensity(DensityCellKind.None)));
    }

    [TestMethod]
    public void CumulativeMaps_FillAfterTheWarmUp_AndResetClears()
    {
        Study study = SimTestStudies.Corridor(120);
        study.Settings.WarmUpS = 10;
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 8);
        DensityGrid grid = engine.Density;
        Assert.AreEqual(0, grid.AccumulatedS, "Nothing gathered during the warm-up (D-218).");
        Assert.AreEqual(0, Sum(grid, DensityMeasure.Footfall), 1e-9);
        Assert.AreEqual(0, Sum(grid, DensityMeasure.Max), 1e-9);

        Run(engine, 30);
        Assert.IsTrue(grid.AccumulatedS > 25 && grid.AccumulatedS < 31, $"{grid.AccumulatedS:0.0} s gathered after the warm-up.");
        Assert.IsTrue(Sum(grid, DensityMeasure.Footfall) > 50, "People walking the corridor leave a trail.");
        ReadOnlySpan<float> max = grid.Values(DensityMeasure.Max), mean = grid.Values(DensityMeasure.Mean), live = grid.Smoothed;
        for (int c = 0; c < grid.CellCount; c++)
        {
            Assert.IsTrue(mean[c] <= max[c] + 1e-5, "Mean never above max.");
            Assert.IsTrue(live[c] <= max[c] + 1e-5, "The live map never above max.");
        }

        engine.Reset();
        Assert.AreEqual(0, grid.AccumulatedS);
        Assert.AreEqual(0, Sum(grid, DensityMeasure.Footfall) + Sum(grid, DensityMeasure.Max) + Sum(grid, DensityMeasure.Mean) + Sum(grid, DensityMeasure.LosEOrWorseS), 1e-9);
    }

    [TestMethod]
    public void Painter_RunMaps_LegendsAndScale()
    {
        Assert.AreEqual(6, HeatmapPainter.Legend(HeatmapMode.MaxDensity, HeatmapRamp.Classic).Count, "Max uses the LoS bands.");
        Assert.AreEqual(5, HeatmapPainter.Legend(HeatmapMode.MeanDensity, HeatmapRamp.Classic).Count, "Mean uses the density ramp.");
        Assert.AreEqual(5, HeatmapPainter.Legend(HeatmapMode.Footfall, HeatmapRamp.Classic, 200).Count);
        Assert.IsTrue(HeatmapPainter.IsCumulative(HeatmapMode.LosEOrWorse));
        Assert.IsFalse(HeatmapPainter.IsCumulative(HeatmapMode.Los));
        Assert.AreEqual(DensityMeasure.Footfall, HeatmapPainter.MeasureOf(HeatmapMode.Footfall));

        DensityCellKind[] kinds = [DensityCellKind.Walkway, DensityCellKind.Walkway, DensityCellKind.None];
        Assert.AreEqual(200, HeatmapPainter.ScaleFor(HeatmapMode.Footfall, kinds, [130f, 12f, 999f]), "Rounded up to 1/2/5 × 10ⁿ; unmapped cells ignored.");
        Assert.AreEqual(10, HeatmapPainter.ScaleFor(HeatmapMode.LosEOrWorse, kinds, [0f, 0f, 0f]), "At least 10.");
        Assert.AreEqual(0, HeatmapPainter.ScaleFor(HeatmapMode.MaxDensity, kinds, [1f, 1f, 1f]));
    }

    [TestMethod]
    public void AccessibleRule_WithNoAccessibleGate_WheelchairUsersStillGetThrough()
    {
        Study study = Round009bTests.WideGates(Mobility, 8);
        study.Devices.RemoveAll(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        SimEngine engine = Round009bTests.Run(study, 240);
        SimDevice line = engine.Model.Devices.Single();
        Assert.IsTrue(engine.ServedAt(line.Index) > 10, $"{engine.ServedAt(line.Index)} served at the gate line: with no accessible gate they use what's there.");
    }

    [TestMethod]
    public void AccessibleRule_OverrideMakesAnOrdinaryGateAccessible()
    {
        Study study = Round009bTests.WideGates(Mobility, 8);
        Device lineDevice = study.Devices.Single(d => d.PresetKey == "device.gate-line");
        Device wideDevice = study.Devices.Single(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        Assert.IsTrue(StudyTemplates.IsAccessible(study, wideDevice), "From the preset (D-223).");
        Assert.IsFalse(StudyTemplates.IsAccessible(study, lineDevice));

        wideDevice.AccessibleOverride = false;
        lineDevice.AccessibleOverride = true;
        SimEngine engine = Round009bTests.Run(study, 240);
        SimDevice line = engine.Model.Devices.Single(d => d.PresetKey == "device.gate-line");
        SimDevice wide = engine.Model.Devices.Single(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        Assert.IsTrue(line.Accessible && !wide.Accessible, "Overrides reach the sim.");
        long total = engine.ServedAt(line.Index) + engine.ServedAt(wide.Index);
        Assert.IsTrue(total > 10);
        Assert.IsTrue(engine.ServedAt(wide.Index) <= total * 0.05, $"{engine.ServedAt(wide.Index)} of {total} at the gate that's no longer accessible.");
    }

    [TestMethod]
    public void Check_WarnsWhenWheelchairUsersHaveNoAccessibleGateOnALevel()
    {
        Study study = Round009bTests.WideGates(Mobility, 8);
        Assert.IsFalse(StudyValidator.Validate(study).Any(m => m.Message.Contains("no accessible", StringComparison.Ordinal)), "The accessible gate is there.");

        study.Devices.RemoveAll(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        ValidationMessage warning = StudyValidator.Validate(study).Single(m => m.Message.Contains("no accessible Gate", StringComparison.Ordinal));
        Assert.AreEqual(ValidationSeverity.Warning, warning.Severity, "A warning, not an error (D-225).");

        Study commuters = Round009bTests.WideGates(SimTestStudies.Commuter, 8);
        commuters.Devices.RemoveAll(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        Assert.IsFalse(StudyValidator.Validate(commuters).Any(m => m.Message.Contains("no accessible", StringComparison.Ordinal)), "Only for people who need it.");
    }

    private static double Sum(DensityGrid grid, DensityMeasure measure)
    {
        double total = 0;
        foreach (float v in grid.Values(measure))
        {
            total += v;
        }

        return total;
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
