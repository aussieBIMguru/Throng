using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 014: lift finish-off: patience at landings with a bar on the bank (D-253, D-254), destination dispatch
/// (D-255, D-256) and the door opening share behind the 3D sliding doors (D-258).
/// </summary>
[TestClass]
public sealed class Round014Tests
{
    [TestMethod]
    public void Bar_TakesTheLiftOutOfRouteChoice()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        SimModel model = SimTestStudies.Compile(study);
        int floor = model.World.FloorByLevel[study.Levels[0].Id];
        var commuter = (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey("agent.commuter")!;

        double open = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.Normal, commuter, null, out _, out _);
        double barred = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.Normal, commuter, null, out _, out _, barredConnector: model.Lifts[0].Connector);
        Assert.IsTrue(double.IsFinite(open), "The lift is the way up.");
        Assert.IsTrue(double.IsPositiveInfinity(barred), "With the lift barred there's no way up (D-253).");
    }

    [TestMethod]
    public void Patience_PeopleWithNoOtherWay_KeepWaiting()
    {
        // 30 a minute at one 13-person car: long waits, but the lift is the only way up.
        var engine = new SimEngine(SimTestStudies.Compile(Round013Tests.LiftStudy(SimTestStudies.Commuter, 30)));
        int steps = (int)Math.Round(600 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
        }

        Assert.AreEqual(0, engine.LiftGaveUp(0), "Nobody leaves a lift that is their only way (D-253, D-254).");
        Assert.IsTrue(engine.Stats.Arrived >= 20, $"{engine.Stats.Arrived} arrived.");
        Assert.IsTrue(engine.Stats.StuckRemoved <= 2, $"{engine.Stats.StuckRemoved} removed as stuck.");
    }

    [TestMethod]
    public void Destination_EveryWaiterIsGivenACar_AndPeopleArrive()
    {
        foreach (bool on in new[] { true, false })
        {
            Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 10);
            Lift lift = study.Shafts[0].Lift!;
            lift.Cars = 2;
            lift.Dispatch = LiftDispatch.DestinationDispatch;
            SimModel model = SimTestStudies.Compile(study);
            Assert.AreEqual(LiftDispatch.DestinationDispatch, model.Lifts[0].DispatchAt(new TimeSpan(8, 0, 0)), "No longer runs as Collective (D-255).");

            var engine = new SimEngine(model, new SimTuning { DestinationDispatch = on });
            int given = 0, waiting = 0;
            int steps = (int)Math.Round(240 / model.TimeStepS);
            for (int s = 0; s < steps; s++)
            {
                engine.Step();
                AgentStore a = engine.Agents;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a.Phase[i] == AgentPhase.LiftWait)
                    {
                        waiting++;
                        given += a.LiftCar[i] >= 0 ? 1 : 0;
                    }
                }
            }

            Assert.IsTrue(waiting > 0, "Someone waited.");
            if (on)
            {
                Assert.IsTrue(given > waiting * 0.9, $"{given} of {waiting} waiter-steps had a car (given on the next step after arriving).");
            }
            else
            {
                Assert.AreEqual(0, given, "With the switch off it runs as Collective: nobody is given a car.");
            }

            Assert.IsTrue(engine.Stats.Arrived >= 15, $"{engine.Stats.Arrived} arrived (destination {on}).");
            Assert.AreEqual(0, engine.Stats.StuckRemoved, $"Nobody stuck (destination {on}).");
        }
    }

    [TestMethod]
    public void Schedule_CanSwitchToDestination()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        study.Shafts[0].Lift!.Schedule = [new LiftScheduleEntry { From = new TimeSpan(8, 0, 0), Dispatch = LiftDispatch.DestinationDispatch }];
        SimLift lift = SimTestStudies.Compile(study).Lifts[0];
        Assert.AreEqual(LiftDispatch.Collective, lift.DispatchAt(new TimeSpan(7, 0, 0)));
        Assert.AreEqual(LiftDispatch.DestinationDispatch, lift.DispatchAt(new TimeSpan(8, 30, 0)), "Destination in a schedule row (D-256).");
    }

    [TestMethod]
    public void DoorShare_FollowsTheDoors()
    {
        var engine = new SimEngine(SimTestStudies.Compile(Round013Tests.LiftStudy(Round013Tests.Wheelchair, 6)));
        Assert.AreEqual(0, engine.LiftDoorShare(0), 1e-9, "Shut while idle.");
        bool sawOpen = false, sawPartial = false;
        int steps = (int)Math.Round(300 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            double share = engine.LiftDoorShare(0);
            Assert.IsTrue(share is >= 0 and <= 1, $"Share {share} out of range.");
            LiftCarStatus state = engine.LiftCarState(0).State;
            if (state == LiftCarStatus.Open)
            {
                Assert.AreEqual(1, share, 1e-9, "Fully open while Open.");
                sawOpen = true;
            }
            else if (state is LiftCarStatus.Idle or LiftCarStatus.Moving)
            {
                Assert.AreEqual(0, share, 1e-9, "Shut while idle or moving.");
            }
            else
            {
                sawPartial |= share > 0 && share < 1;
            }
        }

        Assert.IsTrue(sawOpen, "The doors opened.");
        Assert.IsTrue(sawPartial, "The doors slid (part open while opening or closing, D-258).");
    }
}
