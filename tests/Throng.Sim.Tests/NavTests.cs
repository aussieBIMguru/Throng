using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

[TestClass]
public sealed class NavTests
{
    [TestMethod]
    public void Raster_RectangleWithHole_CountsCellCentres()
    {
        // 4 × 4 m outer (CCW) with a 2 × 2 m hole (CW) at 0.5 m cells: 64 − 16 = 48 cells.
        Point2[] outer = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];
        Point2[] hole = [new(1, 1), new(1, 3), new(3, 3), new(3, 1)];
        int count = 0;
        GridRaster.Fill([outer, hole], new Point2(0, 0), 0.5, 8, 8, _ => count++);
        Assert.AreEqual(48, count);
    }

    [TestMethod]
    public void Raster_HalfOpenEdges_DoNotDoubleCount()
    {
        // A 1 × 1 m square aligned to the grid covers exactly four 0.5 m cells.
        var cells = new HashSet<int>();
        GridRaster.Fill([new Point2[] { new(1, 1), new(2, 1), new(2, 2), new(1, 2) }], new Point2(0, 0), 0.5, 6, 6, c => Assert.IsTrue(cells.Add(c)));
        Assert.AreEqual(4, cells.Count);
    }

    [TestMethod]
    public void SinkField_RisesAwayFromTheSink()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor());
        NavWorld world = model.World;
        NavSurface floor = world.Surfaces[world.Sinks[0].Floor];
        FlowField field = model.Routes.SinkFields[0];

        float previous = -1;
        for (double x = 27; x >= 3; x -= 2)
        {
            float cost = field.Sample(floor, x, 2);
            Assert.IsTrue(float.IsFinite(cost), $"Unreached at x = {x}");
            Assert.IsTrue(cost > previous, $"Cost should rise away from the sink (x = {x}).");
            previous = cost;
        }

        // About 25 m at the nominal 1.34 m/s.
        Assert.AreEqual(25 / NavWorld.NominalSpeed, field.Sample(floor, 3, 2), 3.0);
    }

    [TestMethod]
    public void TwoLevelStair_HasTwoPortalsAndAReachableSink()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.stair-2400", 10));
        NavWorld world = model.World;
        Assert.AreEqual(2, world.Portals.Count, "A straight stair opens onto both floors.");
        Assert.IsTrue(world.Portals.All(p => p.AllowsEntry && p.AllowsExit));

        int ground = world.Surfaces.First(s => s.Kind == NavSurfaceKind.Floor && Math.Abs(s.Z0) < 1e-9).Index;
        double cost = RoutePlanner.Choose(model.Routes, ground, 1.5, 10, 0, MobilityClass.Normal, null, null, out bool direct, out RouteOption option);
        Assert.IsTrue(double.IsFinite(cost));
        Assert.IsFalse(direct);
        Assert.AreEqual(0, option.Connector);
    }

    [TestMethod]
    public void Escalator_IsOneWay()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.escalator-up", 10));
        NavWorld world = model.World;
        Assert.AreEqual(2, world.Portals.Count);
        NavPortal bottom = world.Portals.OrderBy(p => p.ElevationM).First();
        NavPortal top = world.Portals.OrderBy(p => p.ElevationM).Last();
        Assert.IsTrue(bottom.AllowsEntry && !bottom.AllowsExit, "Up escalator: on at the bottom only.");
        Assert.IsTrue(top.AllowsExit && !top.AllowsEntry, "Up escalator: off at the top only.");
    }

    [TestMethod]
    public void Demo_CompilesAndEveryRuleHasARoute()
    {
        Study demo = DemoStudyBuilder.Build();
        SimSetupResult result = SimSetup.Build(demo);
        Assert.IsTrue(result.IsSuccess, string.Join("\n", result.Findings.Select(f => $"[{f.Severity}] {f.Message}")));
        Assert.AreEqual(0, result.Findings.Count(f => f.Severity == ValidationSeverity.Error));

        SimModel model = result.Model!;
        Assert.AreEqual((3 * 2) + 12, model.Emitters.Count, "Two rules per street entrance, one per platform door.");
        Assert.IsTrue(model.Emitters.All(e => e.Destinations.Length > 0));

        // Every chosen destination is reachable (no rule lost a sink to an unreachable route).
        Assert.IsFalse(result.Findings.Any(f => f.Message.Contains("can't reach", StringComparison.Ordinal)),
            string.Join("\n", result.Findings.Select(f => f.Message)));
    }

    [TestMethod]
    public void Demo_StaffOnlyStair_IsLeftOutOfPublicRouting()
    {
        Study demo = DemoStudyBuilder.Build();
        SimModel model = SimTestStudies.Compile(demo);
        Assert.IsTrue(demo.Connectors.Any(c => c.StaffOnly));
        foreach (Connector staff in demo.Connectors.Where(c => c.StaffOnly))
        {
            Assert.IsFalse(model.World.Connectors.Any(c => c.ConnectorId == staff.Id));
        }
    }
}
