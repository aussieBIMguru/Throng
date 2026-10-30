using Throng.Analysis.Checks;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 016: the full CHECK (D-271–D-274): rules on every finding, capacity, unused devices, narrow gaps.</summary>
[TestClass]
public sealed class Round016Tests
{
    [TestMethod]
    public void Demo_ChecksClean_AndEveryFindingHasARule()
    {
        IReadOnlyList<ValidationMessage> findings = StudyCheck.Run(DemoStudyBuilder.Build());
        Assert.AreEqual(0, findings.Count(f => f.Severity == ValidationSeverity.Error), string.Join("\n", findings.Select(f => f.Message)));
        Assert.IsTrue(findings.All(f => f.Rule != ValidationRules.General), "Every finding is tagged with its rule.");
        Assert.IsTrue(findings.Any(f => f.Rule == ValidationRules.Capacity), "The capacity check says something about the demo.");
    }

    [TestMethod]
    public void Capacity_ABusyLiftWarnsWithTheQueueToExpect()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 90);
        study.Settings.EndCondition = StudyEndCondition.FixedDuration;
        study.Settings.DurationS = 1800;
        IReadOnlyList<ValidationMessage> findings = StudyCheck.Run(study);
        ValidationMessage lift = findings.Single(f => f.Rule == ValidationRules.Capacity && f.ObjectId == study.Shafts[0].Id && f.Severity == ValidationSeverity.Warning);
        StringAssert.Contains(lift.Message, "queue peaks");
        StringAssert.Contains(lift.Message, "still waiting at the end", "Demand stays over capacity for the whole run.");

        Study quiet = Round013Tests.LiftStudy(SimTestStudies.Commuter, 2);
        Assert.IsFalse(StudyCheck.Run(quiet).Any(f => f.Rule == ValidationRules.Capacity && f.Severity == ValidationSeverity.Warning), "A quiet lift is fine.");
    }

    [TestMethod]
    public void Queue_FollowsDemandMinuteByMinute()
    {
        // A surge over capacity builds a queue that then drains: it clears by the end (r016 fix).
        (double peak, int at, double end) = CapacityCheck.Queue([20, 20, 2, 2, 2, 2, 2, 2, 2, 2], 12.5);
        Assert.AreEqual(15, peak, 1e-9);
        Assert.AreEqual(1, at);
        Assert.AreEqual(0, end, 1e-9);

        // The rate curve shapes the series rather than the peak lasting all run.
        var rule = new EmissionRule { RatePerMinute = 60, RateCurve = [new RateCurvePoint { Minute = 0, Multiplier = 1 }, new RateCurvePoint { Minute = 10, Multiplier = 0.1 }] };
        double[] series = CapacityCheck.RateSeries(rule, 20);
        Assert.IsTrue(series[0] > 50 && series[19] < 10, $"{series[0]:0.0} → {series[19]:0.0} a minute.");
    }

    [TestMethod]
    public void StepShare_FollowsTags()
    {
        var ticket = new SimStep
        {
            Kind = JourneyStepKind.GoToDevice,
            Targets = [0],
            Needs = 0,
            Lacks = 1,
            Grants = 1,
            Dwell = Distribution.Uniform(1, 2),
            Label = "Ticket",
        };
        var held = new Dictionary<ulong, double> { [1] = 0.8 };
        Assert.AreEqual(0.2, CapacityCheck.AppliesShare(ticket, held), 1e-9, "Only people without a ticket buy one.");
        var gate = new SimStep { Kind = JourneyStepKind.GoToDevice, Targets = [0], Needs = 2, Lacks = 0, Grants = 0, Dwell = Distribution.Uniform(1, 2), Label = "Gate" };
        Assert.AreEqual(0, CapacityCheck.AppliesShare(gate, held), 1e-9, "Nobody holds bit 2.");
    }

    [TestMethod]
    public void Lifts_AWheelchairCrowdNeverJamsTheDoors()
    {
        // More wheelchair users than the car can take: the landing crowds, but people get off, and the car keeps
        // moving (lift courtesy, D-281–D-284).
        var engine = new SimEngine(SimTestStudies.Compile(Round013Tests.LiftStudy(Round013Tests.Wheelchair, 12)));
        double openSince = double.NaN, longestOpen = 0;
        int steps = (int)Math.Round(900 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            bool open = engine.LiftCarState(0).State is LiftCarStatus.Open or LiftCarStatus.Opening;
            if (open && double.IsNaN(openSince))
            {
                openSince = engine.TimeS;
            }
            else if (!open)
            {
                openSince = double.NaN;
            }

            longestOpen = Math.Max(longestOpen, double.IsNaN(openSince) ? 0 : engine.TimeS - openSince);
        }

        Assert.IsTrue(longestOpen < 90, $"A car stood open for {longestOpen:0} s.");
        Assert.IsTrue(engine.Stats.Arrived >= 30, $"{engine.Stats.Arrived} arrived.");
    }

    [TestMethod]
    public void UnusedDevice_IsFlagged()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        var device = new Device { LevelId = study.Levels[0].Id, Name = "Lonely machine", TypeTag = "TicketMachine", Position = new(5, 5) };
        study.Devices.Add(device);
        IReadOnlyList<ValidationMessage> findings = StudyCheck.Run(study);
        Assert.IsTrue(findings.Any(f => f.Rule == ValidationRules.UnusedDevice && f.ObjectId == device.Id), string.Join("\n", findings.Select(f => f.Message)));
    }

    [TestMethod]
    public void NarrowGap_FindsAPinchPoint_NotAWideCorridor()
    {
        IPolygonOps ops = ClipperPolygonOps.Instance;
        IReadOnlyList<Point2>[] narrow =
        [
            ShapeFactory.Rectangle(new(0, 0), new(10, 10)),
            ShapeFactory.Rectangle(new(10, 4.8), new(14, 5.2)),
            ShapeFactory.Rectangle(new(14, 0), new(24, 10)),
        ];
        List<IReadOnlyList<Point2>> pieces = NarrowGapCheck.NarrowPieces(ops.Union(narrow), ops, 0.3);
        Assert.AreEqual(1, pieces.Count, "The 0.4 m corridor.");
        Assert.AreEqual(0.4, OrientedBounds.Of(pieces[0]).ShortSideM, 0.05);

        IReadOnlyList<Point2>[] wide = [narrow[0], ShapeFactory.Rectangle(new(10, 4.4), new(14, 5.6)), narrow[2]];
        Assert.AreEqual(0, NarrowGapCheck.NarrowPieces(ops.Union(wide), ops, 0.3).Count, "1.2 m is wide enough; square corners come back whole.");
    }

    [TestMethod]
    public void Check_PlacesANarrowGapOnItsLevel()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        Level ground = study.Levels[0];
        study.Regions.Add(new Region { LevelId = ground.Id, Name = "Pinch", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(20, 4.8), new(24, 5.2)) });
        study.Regions.Add(new Region { LevelId = ground.Id, Name = "Annex", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(24, 0), new(30, 10)) });
        ValidationMessage gap = StudyCheck.Run(study).First(f => f.Rule == ValidationRules.NarrowGap);
        Assert.AreEqual(ground.Id, gap.LevelId);
        Assert.IsNotNull(gap.At);
        Assert.AreEqual(22, gap.At!.Value.X, 0.5);
    }
}
