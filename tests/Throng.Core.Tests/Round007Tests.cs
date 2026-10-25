using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.Core.Tests;

/// <summary>Round 007: narrow gaps between connector footprints are closed (D-126).</summary>
[TestClass]
public sealed class Round007Tests
{
    [TestMethod]
    public void EscalatorPair_GapBetweenUnits_IsNotWalkable()
    {
        (Study study, Level lower, Level upper, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        study.Connectors.Remove(stair);
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.escalator-pair")!;
        Connector[] pair = [.. ConnectorFactory.FromTemplate(study, template, lower.Id, upper.Id, new Point2(10, 10), 0, TurnSide.Left)];
        study.Connectors.AddRange(pair);

        ConnectorLayout a = ConnectorLayout.Compute(pair[0], study).Value!;
        ConnectorLayout b = ConnectorLayout.Compute(pair[1], study).Value!;
        Point2 between = (a.BottomLanding + b.BottomLanding) * 0.5;
        var net = WalkableArea.Compute(study, lower.Id, ClipperPolygonOps.Instance).Value!;

        Assert.IsFalse(WalkableArea.Contains(net, between + (a.BottomAxis * 3)), "The 0.6 m strip between the units is closed.");
        Assert.IsTrue(WalkableArea.Contains(net, between - (a.BottomAxis * 0.5)), "The floor in front of the entries stays open.");
    }
}
