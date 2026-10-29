using Throng.Analysis;
using Throng.Core.Model;
using Throng.IO;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>Round 011a: the live density map (D-201–D-205) and the wheelchair gate fix (D-209).</summary>
[TestClass]
public sealed class Round011Tests
{
    [TestMethod]
    public void DensityMap_StandingCrowdAddsUpToThePeopleOnIt()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(120)));
        Run(engine, 30);
        AgentStore a = engine.Agents;
        Assert.IsTrue(a.Count > 5, $"Only {a.Count} people in the corridor.");

        // Sample the same positions for a minute so the 5 s smoothing settles on them.
        var grid = new DensityGrid(engine.Model);
        for (int k = 1; k <= 120; k++)
        {
            grid.Sample(a, k * DensityGrid.SampleIntervalS);
        }

        double people = Integral(grid);
        Assert.AreEqual(a.Count, people, a.Count * 0.02, "Each person adds one to the map, spread over the floor around them.");
    }

    [TestMethod]
    public void DensityMap_SmoothsOverAboutFiveSeconds()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(120)));
        Run(engine, 30);
        AgentStore a = engine.Agents;
        var grid = new DensityGrid(engine.Model);
        grid.Sample(a, 0.5);
        grid.Sample(a, 0.6);
        long version = grid.Version;
        grid.Sample(a, 0.7);
        Assert.AreEqual(version, grid.Version, "No new sample within 0.5 s.");

        // After 5 s of the same crowd about 63 % (1 − 1/e) of it shows.
        for (double t = 1.0; t <= 5.0 + 1e-9; t += DensityGrid.SampleIntervalS)
        {
            grid.Sample(a, t);
        }

        double share = Integral(grid) / a.Count;
        Assert.IsTrue(share > 0.55 && share < 0.72, $"{share:P0} of the crowd shows after 5 s.");
    }

    [TestMethod]
    public void DensityMap_StairFlightsUseTheStairTable_BeltsAreNotMapped()
    {
        var stair = new DensityGrid(SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.stair-3600", 60)));
        Assert.AreEqual(2, stair.Layers.Count(l => l.IsFloor), "Both floors are mapped.");
        Assert.IsTrue(stair.Layers.Any(l => !l.IsFloor && HasKind(stair, l, DensityCellKind.Stair)), "Flights use the stair table (D-205).");
        Assert.IsTrue(stair.Layers.Where(l => !l.IsFloor).All(l => l.Label.Length > 0), "Pieces carry their connector's name for the ticker.");

        SimModel beltModel = SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.escalator-up", 60));
        var belts = new DensityGrid(beltModel);
        foreach (NavSurface s in beltModel.World.Surfaces)
        {
            if (s.Motion == NavMotion.Belt)
            {
                Assert.AreEqual(-1, belts.LayerOfSurface(s.Index), $"Belt {s.Name} isn't mapped.");
            }
        }
    }

    [TestMethod]
    public void DensityMap_DemoQueuesUseTheQueueTable()
    {
        SimModel model = SimTestStudies.Compile(DemoStudyBuilder.Build());
        var grid = new DensityGrid(model);
        Assert.IsTrue(model.Devices.Count > 0);
        int queueCells = 0;
        foreach (DensityCellKind kind in grid.Kinds)
        {
            queueCells += kind == DensityCellKind.Queue ? 1 : 0;
        }

        Assert.IsTrue(queueCells > 10, $"{queueCells} queue cells around the demo's device lines.");
    }

    [TestMethod]
    public void DensityMap_EngineSamplesWhileRunning_AndResetClears()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(120)));
        Run(engine, 20);
        Assert.IsTrue(Integral(engine.Density) > 1, "The engine's own map fills as people walk.");
        engine.Reset();
        Assert.AreEqual(0, Integral(engine.Density), 1e-9, "Reset clears the map.");
    }

    [TestMethod]
    public void Heatmap_LosBandsFollowTheTableForThePlace()
    {
        // 1.0 p/m² = 1.0 m²/person: walkway D, queuing B, stair C (BRIEF §11.3).
        Assert.AreEqual(LevelOfService.D, FruinLevelOfService.FromDensity(1.0, HeatmapPainter.AreaType(DensityCellKind.Walkway)));
        Assert.AreEqual(LevelOfService.B, FruinLevelOfService.FromDensity(1.0, HeatmapPainter.AreaType(DensityCellKind.Queue)));
        Assert.AreEqual(LevelOfService.C, FruinLevelOfService.FromDensity(1.0, HeatmapPainter.AreaType(DensityCellKind.Stair)));
        Assert.AreEqual(6, HeatmapPainter.Legend(HeatmapMode.Los, HeatmapRamp.Classic).Count);
        Assert.AreEqual(0, HeatmapPainter.Legend(HeatmapMode.Off, HeatmapRamp.Classic).Count);
    }

    [TestMethod]
    public void Heatmap_PaintsMappedCellsOnly_AndFindsThePeak()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(120)));
        Run(engine, 30);
        DensityGrid grid = engine.Density;
        DensityLayer floor = grid.Layers.First(l => l.IsFloor);
        byte[] pixels = new byte[floor.CellCount * 4];
        HeatmapPainter.PaintLayer(floor, grid.Kinds, grid.Smoothed, HeatmapMode.Los, HeatmapRamp.Classic, pixels);
        int painted = 0;
        for (int c = 0; c < floor.CellCount; c++)
        {
            bool mapped = grid.Kinds[floor.Base + c] != DensityCellKind.None;
            Assert.AreEqual(mapped, pixels[(c * 4) + 3] > 0, $"Cell {c}: only mapped cells are drawn.");
            painted += mapped ? 1 : 0;
        }

        Assert.IsTrue(painted > 0);
        HeatPeak? peak = HeatmapPainter.FindPeak(grid.Layers, grid.Kinds, grid.Smoothed);
        Assert.IsNotNull(peak, "People on the map give a peak.");
        Assert.IsTrue(peak.Value.Density > 0);
    }

    [TestMethod]
    public void LaneClear_DiagnosticsSwitchExists()
    {
        Assert.IsTrue(SimTuning.Default.LaneClear, "D-209 is on by default; the wheelchair gate test is un-skipped.");
    }

    private static bool HasKind(DensityGrid grid, DensityLayer layer, DensityCellKind kind)
    {
        for (int c = 0; c < layer.CellCount; c++)
        {
            if (grid.Kinds[layer.Base + c] == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static double Integral(DensityGrid grid)
    {
        double total = 0;
        foreach (DensityLayer layer in grid.Layers)
        {
            double area = layer.CellU * layer.CellV;
            for (int c = 0; c < layer.CellCount; c++)
            {
                total += grid.Smoothed[layer.Base + c] * area;
            }
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
