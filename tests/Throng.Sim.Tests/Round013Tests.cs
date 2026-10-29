using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 013a: lifts (D-236–D-244): layout, routing, cars, boarding, car space, dispatch schedule.</summary>
[TestClass]
public sealed class Round013Tests
{
    public static readonly Guid Wheelchair = TemplateIds.FromKey(BuiltInLibrary.MobilityProfileKey);

    [TestMethod]
    public void Layout_TwoCars_FrontRearAndLandings()
    {
        // A 4 × 2.4 m shaft drawn from its south-west corner: edge 0 is the south side.
        List<Point2> outline = ShapeFactory.Rectangle(new(10, 4), new(14, 6.4));
        LiftLayout layout = LiftLayout.Compute(outline, 0, 2, 2.5).Value!;
        Assert.AreEqual(2, layout.Cars.Count);
        Assert.AreEqual(1.0, layout.Cars[0].HalfWidth, 1e-9, "4 m front edge, two cars.");
        Assert.AreEqual(1.2, layout.Cars[0].HalfDepth, 1e-9);
        Assert.AreEqual(-1, layout.Front.Outward.Y, 1e-9, "The front (south) side opens south.");
        Assert.IsNotNull(layout.Rear, "The north edge faces the front.");
        Assert.AreEqual(1, layout.Rear!.Outward.Y, 1e-9);
        Assert.AreEqual(4 - 2.5, layout.Front.Landing.Min(p => p.Y), 1e-9, "The landing runs 2.5 m out from the door.");
        (Point2 a, Point2 b) = layout.Cars[1].FrontDoor;
        Assert.AreEqual(13, (a.X + b.X) / 2, 1e-9, "The second car's door is in the middle of its half of the edge.");
        Assert.IsNotNull(layout.Cars[1].RearDoor);
    }

    [TestMethod]
    public void Wheelchairs_RideTheLift_ToTheUpperLevel()
    {
        Study study = LiftStudy(Wheelchair, 6);
        SimModel model = SimTestStudies.Compile(study);
        Assert.AreEqual(1, model.Lifts.Count);
        Assert.AreEqual(2, model.Lifts[0].Stops);
        var engine = new SimEngine(model);
        int maxRiders = 0;
        bool rode = false;
        for (int s = 0; s < 3000; s++)
        {
            engine.Step();
            (int _, double z, LiftCarStatus _, int riders, int _) = engine.LiftCarState(0);
            maxRiders = Math.Max(maxRiders, riders);
            rode |= riders > 0 && z > 0.5 && z < 4.5;
        }

        Assert.IsTrue(rode, "Someone rode the car between the levels.");
        Assert.IsTrue(engine.Stats.Arrived >= 10, $"{engine.Stats.Arrived} wheelchair users reached the upper exit in 300 s.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved, "No one stuck.");
        Assert.IsTrue(maxRiders <= 4, $"13 places ÷ 3.24 per wheelchair = 4 at most (D-243); saw {maxRiders}.");
    }

    [TestMethod]
    public void NoLift_LiftOnlyPeopleCantGoUp()
    {
        Study study = LiftStudy(Wheelchair, 6);
        study.Shafts[0].Lift = null;
        SimSetupResult result = SimSetup.Build(study);
        Assert.IsTrue(result.Model is null || result.Model.Lifts.Count == 0);
    }

    [TestMethod]
    public void Commuters_CanTakeTheLift_WhenItsTheOnlyWayUp()
    {
        var engine = new SimEngine(SimTestStudies.Compile(LiftStudy(SimTestStudies.Commuter, 10)));
        for (int s = 0; s < 2400; s++)
        {
            engine.Step();
        }

        Assert.IsTrue(engine.Stats.Arrived >= 15, $"{engine.Stats.Arrived} arrived.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved);
    }

    [TestMethod]
    public void Cars_StartAtTheLobby()
    {
        Study study = LiftStudy(Wheelchair, 6);
        study.Shafts[0].Lift!.LobbyLevelId = study.Levels[1].Id;
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Assert.AreEqual(5, engine.LiftCarState(0).Z, 1e-9, "The upper level is the lobby (D-244).");
    }

    [TestMethod]
    public void Schedule_SwitchesDispatch_ByTimeOfDay()
    {
        Study study = LiftStudy(Wheelchair, 6);
        study.Shafts[0].Lift!.Schedule =
        [
            new LiftScheduleEntry { From = new TimeSpan(9, 30, 0), Dispatch = LiftDispatch.Collective },
            new LiftScheduleEntry { From = new TimeSpan(7, 30, 0), Dispatch = LiftDispatch.UpPeak },
        ];
        SimLift lift = SimTestStudies.Compile(study).Lifts[0];
        Assert.AreEqual(LiftDispatch.Collective, lift.DispatchAt(new TimeSpan(7, 0, 0)), "Before the first row: the lift's own mode.");
        Assert.AreEqual(LiftDispatch.UpPeak, lift.DispatchAt(new TimeSpan(8, 0, 0)));
        Assert.AreEqual(LiftDispatch.Collective, lift.DispatchAt(new TimeSpan(10, 0, 0)));

        study.Shafts[0].Lift!.Dispatch = LiftDispatch.Shuttle;
        Assert.AreEqual(LiftDispatch.Shuttle, SimTestStudies.Compile(study).Lifts[0].DispatchAt(new TimeSpan(8, 0, 0)), "A shuttle ignores the schedule.");
    }

    [TestMethod]
    public void Validation_WarnsAboutALiftWithOneDoorLevel()
    {
        Study study = LiftStudy(Wheelchair, 6);
        Lift lift = study.Shafts[0].Lift!;
        lift.Doors = [new LiftLevelDoors { LevelId = study.Levels[1].Id, Front = LiftDoorUse.Closed, Rear = LiftDoorUse.Closed }];
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        Assert.IsTrue(findings.Any(f => f.Severity == ValidationSeverity.Warning && f.Message.Contains("fewer than two levels", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Two 20 × 10 m floors 5 m apart, joined only by a one-car lift in a 2.4 m shaft in the middle (doors south on
    /// both levels). People appear at the west end of the ground floor and leave at the east end of the upper floor.
    /// </summary>
    internal static Study LiftStudy(Guid profile, double perMinute)
    {
        var study = new Study { Name = "Lift" };
        study.Settings.Seed = 3;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var ground = new Level { Name = "Ground", ElevationM = 0, HeightM = 5 };
        var upper = new Level { Name = "Upper", ElevationM = 5, HeightM = 5 };
        study.Levels.AddRange([ground, upper]);
        study.Regions.Add(new Region { LevelId = ground.Id, Name = "Ground floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        study.Regions.Add(new Region { LevelId = upper.Id, Name = "Upper floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        Shaft shaft = ConnectorFactory.CreateShaft(study, ground.Id, upper.Id, ShapeFactory.Rectangle(new(8.8, 5), new(11.2, 7.4)));
        var preset = (LiftTemplate)BuiltInLibrary.Instance.FindByKey("lift.passenger-13")!;
        shaft.Lift = LiftFactory.FromTemplate(shaft, preset, preset.Id);
        study.Shafts.Add(shaft);

        Region sink = SimTestStudies.Zone(study, upper, "Exit", ZoneKind.Sink, new(18, 0), new(20, 10));
        Region spawn = SimTestStudies.Zone(study, ground, "Entry", ZoneKind.Spawn, new(0, 0), new(2, 10));
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(profile)],
            RatePerMinute = perMinute,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = sink.Id, Weight = 1 }],
        });
        return study;
    }
}
