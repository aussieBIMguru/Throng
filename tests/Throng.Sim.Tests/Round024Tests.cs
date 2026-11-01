using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 024a: groups (D-382–D-389): spawned together, sharing journey, tags, sink and pace; walking close; queueing
/// at the same device and waiting past it; boarding lifts all at once; group hues in the snapshot.
/// </summary>
[TestClass]
public sealed class Round024Tests
{
    private static readonly Guid Family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);
    private static readonly Guid WheelchairCompanion = TemplateIds.FromKey("group.wheelchair-companion");

    [TestMethod]
    public void GroupLine_SpawnsMembersSideBySide_SharingJourneyTagsSinkAndPace()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 20);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(Family, null, 1, SpawnTag.Of("ticket", 50))] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 20);

        AgentStore a = engine.Agents;
        Assert.IsTrue(engine.GroupCount > 0, "Groups spawned.");
        foreach (IGrouping<int, int> group in Enumerable.Range(0, a.Count).GroupBy(i => a.Group[i]))
        {
            Assert.IsTrue(group.Key >= 0, "Everyone on a group line is in a group.");
            int[] m = [.. group];
            Assert.IsTrue(m.Length is >= 1 and <= 5, $"Family groups are 2–5 ({m.Length} left in the study).");
            Assert.IsTrue(m.All(i => a.Journey[i] == a.Journey[m[0]] && a.Tags[i] == a.Tags[m[0]] && a.Sink[i] == a.Sink[m[0]]), "Shared journey, tags and sink (D-389).");
            Assert.IsTrue(m.All(i => a.V0[i] == a.V0[m[0]]), "One pace: the slowest member's (D-383).");
            Assert.IsTrue(m.All(i => a.Route[i] == a.Route[m[0]]), "One route profile (D-387).");
        }
    }

    [TestMethod]
    public void GroupShares_AreSharesOfPeople()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 60, pattern: EmissionPattern.Poisson);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, share: 1), MixLine.OfGroup(Family, share: 1)] };
        SimModel model = SimTestStudies.Compile(study);
        Assert.AreEqual(1.0, model.Emitters[0].Lines[0].PickWeight, 1e-9, "Single lines pick by share, as before.");
        Assert.AreEqual(1.0 / 3.5, model.Emitters[0].Lines[1].PickWeight, 1e-9, "Group lines by share ÷ mean size (D-386).");

        var engine = new SimEngine(model);
        var grouped = new HashSet<int>();
        var single = new HashSet<int>();
        int steps = (int)Math.Round(300 / model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                (a.Group[i] >= 0 ? grouped : single).Add(a.Serial[i]);
            }
        }

        double share = grouped.Count / (double)(grouped.Count + single.Count);
        Assert.IsTrue(share is > 0.35 and < 0.65, $"{share:P0} of people came in groups (expected about half).");
        Assert.IsTrue(grouped.Count + single.Count is > 220 and < 380, $"{grouped.Count + single.Count} people at 60 a minute over 5 min.");
    }

    [TestMethod]
    public void Groups_WalkClose_AndAtOnePace()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 15);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(Family)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        int close = 0, samples = 0;
        int steps = (int)Math.Round(120 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            if (s % 20 != 0)
            {
                continue;
            }

            AgentStore a = engine.Agents;
            foreach (IGrouping<int, int> group in Enumerable.Range(0, a.Count).Where(i => a.Group[i] >= 0 && a.X[i] > 5 && a.X[i] < 25).GroupBy(i => a.Group[i]))
            {
                int[] m = [.. group];
                if (m.Length < 2)
                {
                    continue;
                }

                double spread = m.SelectMany(i => m.Select(j => Point2.Distance(new Point2(a.X[i], a.Y[i]), new Point2(a.X[j], a.Y[j])))).Max();
                samples++;
                close += spread < 4.0 ? 1 : 0;
            }
        }

        Assert.IsTrue(samples > 20, $"{samples} samples.");
        Assert.IsTrue(close >= samples * 0.9, $"{close} of {samples} group samples within 4 m.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved, "Nobody stuck.");
    }

    [TestMethod]
    public void Groups_UseOneMachine_AndWaitPastIt()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 6);
        Level level = study.Levels[0];
        var machine = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.ticket-machine")!;
        study.Devices.Add(DeviceFactory.FromTemplate(study, machine, level.Id, new Point2(12, 0.3), 90));
        study.Devices.Add(DeviceFactory.FromTemplate(study, machine, level.Id, new Point2(16, 0.3), 90));
        var journey = new JourneyTemplate
        {
            Key = "journey.test-group-ticket", Name = "Ticket", Category = "Journeys",
            Steps = [JourneyStep.Device("TicketMachine", lacks: "ticket", grants: "ticket"), JourneyStep.ExitStep()],
        };
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(Family, journey.Id)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));

        bool regrouped = false;
        int split = 0;
        // A family queues at one machine (20–60 s each) and waits for its last member, so a family of 4–5 needs
        // about 4 minutes before anyone leaves: run long enough for the first families to get out.
        int steps = (int)Math.Round(400 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                regrouped |= a.Phase[i] == AgentPhase.Regroup;
                for (int j = i + 1; j < a.Count; j++)
                {
                    if (a.Group[i] >= 0 && a.Group[i] == a.Group[j] && a.QDevice[i] >= 0 && a.QDevice[j] >= 0 && a.QDevice[i] != a.QDevice[j]
                        && a.Step[i] == a.Step[j])
                    {
                        split++;
                    }
                }
            }
        }

        Assert.IsTrue(engine.ServedAt(0) + engine.ServedAt(1) >= 6, "People were served.");
        Assert.IsTrue(regrouped, "Someone waited past the machine for the rest (D-384).");
        Assert.AreEqual(0, split, "Mates never head for different machines on the same step (D-389, D-384).");
        Assert.IsTrue(engine.Stats.Arrived >= 4, $"{engine.Stats.Arrived} arrived, {engine.ServedAt(0) + engine.ServedAt(1)} served, {engine.Stats.StuckRemoved} removed as stuck.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved, "Nobody waiting for their group is removed as stuck.");
    }

    [TestMethod]
    public void Lifts_TakeAGroupAllAtOnce()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 6);
        Region spawn = study.Regions.Single(r => r.EmissionRules.Count > 0);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.OfGroup(WheelchairCompanion)] };
        SimModel model = SimTestStudies.Compile(study);
        Assert.AreEqual(Nav.MobilityClass.StepFree, model.RouteProfiles[model.Emitters[0].Lines[0].RouteProfile].Mobility,
            "Companions take the wheelchair user's step-free route (D-387).");

        var engine = new SimEngine(model);
        int splits = 0;
        int steps = (int)Math.Round(600 / model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                if (a.Phase[i] != AgentPhase.LiftRide || engine.LiftCarState(a.LiftCar[i]).State != LiftCarStatus.Moving)
                {
                    continue;
                }

                for (int j = 0; j < a.Count; j++)
                {
                    if (j != i && a.Group[j] == a.Group[i] && a.Phase[j] == AgentPhase.LiftWait)
                    {
                        splits++;
                    }
                }
            }
        }

        Assert.AreEqual(0, splits, "No car leaves with part of a group while the rest wait (D-384).");
        Assert.IsTrue(engine.Stats.Arrived >= 10, $"{engine.Stats.Arrived} arrived upstairs.");
    }

    [TestMethod]
    public void Snapshot_CarriesGroupHues_ZeroForSingles()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 30);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter), MixLine.OfGroup(Family)] };
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 30);
        float[] buffer = [];
        int n = engine.WriteSnapshot(ref buffer);
        AgentStore a = engine.Agents;
        for (int i = 0; i < n; i++)
        {
            float hue = buffer[(i * SimEngine.FloatsPerAgent) + 11];
            Assert.AreEqual(a.Group[i] >= 0, hue >= 1, $"Agent {i}: hue {hue}.");
        }
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS, MidpointRounding.AwayFromZero);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }
    }
}
