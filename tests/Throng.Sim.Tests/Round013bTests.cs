using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 013b: lift priority for people who need accessible devices (D-247), others avoiding a lift they're waiting at
/// (D-248), the demo's lifts (D-246) and the office tower demo (D-250).
/// </summary>
[TestClass]
public sealed class Round013bTests
{
    private static AgentProfileTemplate Profile(string key) => (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey(key)!;

    [TestMethod]
    public void Priority_IsForPeopleWhoNeedAccessibleDevices()
    {
        Assert.IsTrue(RoutePlanner.HasLiftPriority(Profile(BuiltInLibrary.MobilityProfileKey)));
        Assert.IsFalse(RoutePlanner.HasLiftPriority(Profile("agent.commuter")));
        Assert.IsFalse(RoutePlanner.HasLiftPriority(Profile("agent.luggage")), "Luggage keeps its soft lift preference only.");
        Assert.IsFalse(RoutePlanner.HasLiftPriority(null));
        Assert.AreEqual(MobilityClass.StepFree, NavRoutes.ClassOf(new AgentProfileTemplate { NeedsAccessibleDevices = true }), "They keep to lifts and ramps (D-247).");
    }

    [TestMethod]
    public void Penalty_OnlyPeopleWithoutPriorityFeelIt()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        SimModel model = SimTestStudies.Compile(study);
        int floor = model.World.FloorByLevel[study.Levels[0].Id];
        var penalty = new double[model.World.Connectors.Count];
        penalty[model.Lifts[0].Connector] = SimEngine.LiftPriorityPenaltyS;

        AgentProfileTemplate commuter = Profile("agent.commuter");
        double plain = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.Normal, commuter, null, out _, out _);
        double felt = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.Normal, commuter, null, out _, out _, priorityPenaltyS: penalty);
        Assert.AreEqual(plain + SimEngine.LiftPriorityPenaltyS, felt, 1e-6, "The lift is the only way up, so the whole 60 s shows (unscaled by lift preference).");

        AgentProfileTemplate wheelchair = Profile(BuiltInLibrary.MobilityProfileKey);
        double own = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.StepFree, wheelchair, null, out _, out _);
        double ownWithPenalty = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.StepFree, wheelchair, null, out _, out _, priorityPenaltyS: penalty);
        Assert.AreEqual(own, ownWithPenalty, 1e-9, "People with priority don't avoid a lift for each other.");

        double unseen = RoutePlanner.Choose(model.Routes, floor, 1, 5, 0, MobilityClass.Normal, commuter, null, out _, out _, sightRadius: 0.01, priorityPenaltyS: penalty);
        Assert.AreEqual(plain, unseen, 1e-6, "Out of sight, nobody knows who's waiting (D-179).");
    }

    [TestMethod]
    public void Priority_WheelchairUsersWaitLess_AtAPassengerLift()
    {
        double on = WheelchairWaitShare(new SimTuning());
        double off = WheelchairWaitShare(new SimTuning { LiftPriority = false });
        Assert.IsTrue(on < off, $"Wheelchair users' share of the waiting: {on:P1} with priority vs {off:P1} without (D-247).");
    }

    [TestMethod]
    public void Demo_HasTwoAccessibleLifts()
    {
        Study demo = DemoStudyBuilder.Build();
        SimModel model = SimTestStudies.Compile(demo);
        Assert.AreEqual(2, model.Lifts.Count, "Street ↔ concourse and concourse ↔ platform (D-246).");
        Assert.IsTrue(model.Lifts.All(l => l.Stops == 2 && l.AccessiblePriority));
        Assert.IsTrue(demo.Regions.SelectMany(r => r.EmissionRules).SelectMany(r => r.Mix).Any(m => m.ProfileId == Round013Tests.Wheelchair), "Wheelchair users are in the flows.");
    }

    [TestMethod]
    public void OfficeTower_PeopleRideTheBankUp()
    {
        Study tower = OfficeTowerBuilder.Build();
        Assert.AreEqual(0, StudyValidator.Validate(tower).Count(f => f.Severity == ValidationSeverity.Error));
        SimModel model = SimTestStudies.Compile(tower);
        Assert.AreEqual(1, model.Lifts.Count);
        Assert.AreEqual(Lift.MaxCars, model.Lifts[0].Cars);
        Assert.AreEqual(OfficeTowerBuilder.OfficeFloors + 1, model.Lifts[0].Stops);

        var engine = new SimEngine(model);
        int steps = (int)Math.Round(600 / model.TimeStepS);
        int busiest = 0;
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            for (int car = 0; car < engine.LiftCarCount; car++)
            {
                busiest = Math.Max(busiest, engine.LiftCarState(car).Riders);
            }
        }

        Assert.IsTrue(busiest >= 2, $"Cars carried {busiest} at most.");
        Assert.IsTrue(engine.Stats.Arrived >= 20, $"{engine.Stats.Arrived} reached their desks in 10 min.");
        Assert.IsTrue(engine.Stats.StuckRemoved <= 2, $"{engine.Stats.StuckRemoved} removed as stuck.");
    }

    /// <summary>
    /// The lift study at 30 a minute (near the car's capacity), 80% commuters and 20% wheelchair users: the
    /// wheelchair users' share of all person-seconds spent waiting at the landing, over 600 s.
    /// </summary>
    private static double WheelchairWaitShare(SimTuning tuning)
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 30);
        Region spawn = study.Regions.Single(r => r.EmissionRules.Count > 0);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter, share: 80), MixLine.Of(Round013Tests.Wheelchair, share: 20)],
        };

        SimModel model = SimTestStudies.Compile(study);
        int wheelchair = model.Profiles.Single(p => p.Template.NeedsAccessibleDevices).Index;
        var engine = new SimEngine(model, tuning);
        double theirs = 0, all = 0;
        int steps = (int)Math.Round(600 / model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                if (a.Phase[i] == AgentPhase.LiftWait)
                {
                    all++;
                    theirs += a.Profile[i] == wheelchair ? 1 : 0;
                }
            }
        }

        Assert.IsTrue(all > 0, "Someone waited.");
        return theirs / all;
    }
}
