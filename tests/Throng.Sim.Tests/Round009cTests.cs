using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>Round 009c: stair effort in route choice, so people prefer an escalator unless it's queued (D-174).</summary>
[TestClass]
public sealed class Round009cTests
{
    [TestMethod]
    public void BuiltIns_StairEffort_CommutersMildest_LuggageElderlyFamilyStrongest()
    {
        static AgentProfileTemplate P(string key) => (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey(key)!;
        Assert.AreEqual(AgentProfileTemplate.DefaultStairUpEffort, P("agent.infrequent").StairUpEffort);
        Assert.AreEqual(AgentProfileTemplate.DefaultStairDownEffort, P("agent.infrequent").StairDownEffort);
        Assert.IsTrue(P("agent.commuter").StairUpEffort < P("agent.infrequent").StairUpEffort);
        foreach (string key in new[] { "agent.luggage", "agent.elderly", "agent.family" })
        {
            Assert.IsTrue(P(key).StairUpEffort > P("agent.infrequent").StairUpEffort, key);
            Assert.IsTrue(P(key).StairDownEffort > P("agent.infrequent").StairDownEffort, key);
        }

        Assert.IsTrue(new AgentProfileTemplate().StairUpEffort > 1, "Profiles saved before 009c get the default, not time only.");
    }

    [TestMethod]
    public void StairEffort_PeopleNearerTheStairStillTakeTheEscalator()
    {
        double off = EscalatorShare(new SimTuning { StairEffort = false, StandingRest = false });
        double on = EscalatorShare(SimTuning.Default);
        Assert.IsTrue(on > off, $"Escalator share {on:P0} with stair effort vs {off:P0} without.");
        Assert.IsTrue(on > 0.5, $"Only {on:P0} took the escalator with stair effort and standing rest on (most should).");
    }

    /// <summary>
    /// Two levels with an up escalator at y = 15 and a stair at y = 5; infrequent travellers (mostly standers) appear
    /// at the south-west, nearer the stair, at a rate the escalator carries without a queue. Returns the share of
    /// people who used the escalator.
    /// </summary>
    private static double EscalatorShare(SimTuning tuning)
    {
        Study study = SimTestStudies.TwoLevels("connector.escalator-up", 6);
        study.Connectors[0].BottomLanding = new Point2(10, 15);
        Level ground = study.Levels[0];
        Level upper = study.Levels[1];
        var stair = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-3600")!;
        study.Connectors.AddRange(ConnectorFactory.FromTemplate(study, stair, ground.Id, upper.Id, new Point2(10, 5), 0, TurnSide.Left));
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.Outline = ShapeFactory.Rectangle(new(0, 0), new(3, 6));
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(TemplateIds.FromKey("agent.infrequent"))] };

        var engine = new SimEngine(SimTestStudies.Compile(study), tuning);
        NavWorld world = engine.Model.World;
        var escalator = new HashSet<double>();
        var stairs = new HashSet<double>();
        int steps = (int)Math.Round(300 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                int connector = world.Surfaces[a.Surface[i]].ConnectorIndex;
                if (connector < 0)
                {
                    continue;
                }

                (world.Connectors[connector].Kind == ConnectorKind.Escalator ? escalator : stairs).Add(a.SpawnTime[i]);
            }
        }

        int total = escalator.Count + stairs.Count;
        Assert.IsTrue(total > 15, $"Only {total} people went up.");
        return (double)escalator.Count / total;
    }
}
