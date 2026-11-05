using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 018a: journey graphs (D-302–D-306, D-314), step-list conversion and profile default journeys (D-304).</summary>
[TestClass]
public sealed class Round018Tests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    [TestMethod]
    public void FromSteps_ConditionsBecomeTagChecks_ThatMergeBack()
    {
        var id = Guid.NewGuid();
        var notes = new List<string>();
        (List<JourneyNode> nodes, List<JourneyWire> wires) = JourneyGraph.FromSteps(id,
        [
            JourneyStep.Device("Kiosk", needs: "hungry"),
            JourneyStep.Device("TicketMachine", lacks: "ticket", grants: "ticket"),
            JourneyStep.Device("Gate"),
            JourneyStep.ExitStep(),
            JourneyStep.Device("Never"),
        ], notes);
        var journey = new JourneyTemplate { Id = id, Nodes = nodes, Wires = wires };

        Assert.AreEqual(0, notes.Count);
        Assert.IsFalse(nodes.Any(n => n.TargetTag == "Never"), "Steps after the exit could never run (dropped as before).");
        Assert.AreEqual(1, nodes.Count(n => n.Kind == JourneyNodeKind.Spawn));
        JourneyNode hungry = nodes.Single(n => n.Kind == JourneyNodeKind.TagCheck && n.TargetTag == "hungry");
        JourneyNode ticket = nodes.Single(n => n.Kind == JourneyNodeKind.TagCheck && n.TargetTag == "ticket");
        JourneyNode kiosk = nodes.Single(n => n.TargetTag == "Kiosk");
        JourneyNode machine = nodes.Single(n => n.TargetTag == "TicketMachine");
        JourneyNode gate = nodes.Single(n => n.TargetTag == "Gate");

        Assert.AreEqual(kiosk.Id, JourneyGraph.Next(journey, hungry.Id, 0)?.Id, "Needs: 'has' does the step.");
        Assert.AreEqual(ticket.Id, JourneyGraph.Next(journey, hungry.Id, 1)?.Id, "'hasn't' goes around it.");
        Assert.AreEqual(ticket.Id, JourneyGraph.Next(journey, kiosk.Id, 0)?.Id, "…and both merge at the next step.");
        Assert.AreEqual(machine.Id, JourneyGraph.Next(journey, ticket.Id, 1)?.Id, "Lacks: 'hasn't' does the step.");
        Assert.AreEqual(gate.Id, JourneyGraph.Next(journey, ticket.Id, 0)?.Id);
        Assert.AreEqual("ticket", machine.GrantsTag);
        Assert.AreEqual(JourneyNodeKind.Sink, JourneyGraph.Next(journey, gate.Id, 0)?.Kind);
        Assert.IsFalse(JourneyGraph.Problems(journey).Any(), string.Join("; ", JourneyGraph.Problems(journey).Select(p => p.Message)));
        Assert.IsTrue(machine.Y > gate.Y, "A guarded node sits a row down, left to right layout.");

        (List<JourneyNode> again, _) = JourneyGraph.FromSteps(id, [JourneyStep.Device("Kiosk", needs: "hungry"), JourneyStep.Device("TicketMachine", lacks: "ticket", grants: "ticket"), JourneyStep.Device("Gate"), JourneyStep.ExitStep()], []);
        CollectionAssert.AreEqual(nodes, again, "Conversion is repeatable (ids come from the journey id).");
    }

    [TestMethod]
    public void FromSteps_ForwardJumpIsTheGaveUpOutput_BackwardJumpLeaves()
    {
        var notes = new List<string>();
        var id = Guid.NewGuid();
        (List<JourneyNode> nodes, List<JourneyWire> wires) = JourneyGraph.FromSteps(id,
        [
            JourneyStep.Device("TicketMachine") with { OnGiveUp = JourneyGiveUp.JumpTo, GiveUpStep = 2 },
            JourneyStep.Device("Gate") with { OnGiveUp = JourneyGiveUp.JumpTo, GiveUpStep = 0 },
            JourneyStep.DwellIn("Retail", Distribution.Fixed(10)),
        ], notes);
        var journey = new JourneyTemplate { Id = id, Nodes = nodes, Wires = wires };
        JourneyNode machine = nodes.Single(n => n.TargetTag == "TicketMachine");
        JourneyNode gate = nodes.Single(n => n.TargetTag == "Gate");

        Assert.AreEqual(JourneyGiveUp.Branch, machine.OnGiveUp);
        Assert.AreEqual(JourneyNodeKind.Dwell, JourneyGraph.Next(journey, machine.Id, 1)?.Kind, "Gave up at the machine: on to step 3.");
        Assert.AreEqual(JourneyGiveUp.Leave, gate.OnGiveUp, "A backward jump would loop (D-306), so it leaves.");
        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "step 2");
        Assert.IsFalse(JourneyGraph.HasLoop(journey));
    }

    [TestMethod]
    public void Problems_CatchLoopsBrokenWiresAndBadBranches()
    {
        var journey = new JourneyTemplate { Name = "Test" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid chance = graph.Add(JourneyGraph.ChanceNode(1, 0), 1);
        Guid check = graph.Add(JourneyGraph.TagCheckNode("vip"), 2);
        Guid set = graph.Add(JourneyGraph.SetTagNode("vip"), 3);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 4);
        Guid lonely = graph.Add(JourneyGraph.DeviceNode("Gate"), 4, 2);
        graph.Wire(graph.Start, chance).Wire(chance, 0, check).Wire(chance, 1, exit).Wire(check, 0, exit).Wire(check, 1, set).Wire(set, exit);
        graph.Apply(journey);

        IReadOnlyList<JourneyGraphProblem> problems = JourneyGraph.Problems(journey);
        Assert.IsFalse(problems.Any(p => p.IsError), string.Join("; ", problems.Select(p => p.Message)));
        Assert.IsTrue(problems.Any(p => !p.IsError && p.NodeId == lonely), "A node no one reaches warns.");
        Assert.AreEqual(3, JourneyGraph.Reachable(journey).Count(n => n.Kind is JourneyNodeKind.Chance or JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag), "Chance, check and set are all reached.");

        journey.Wires.Add(JourneyWire.Of(set, 0, chance));
        Assert.IsTrue(JourneyGraph.Problems(journey).Any(p => p.IsError && p.Message.Contains("two wires", StringComparison.Ordinal)), "Two wires from one output.");
        Assert.IsTrue(JourneyGraph.HasLoop(journey), "…and that wire loops back to the chance.");
        Assert.IsTrue(JourneyGraph.Problems(journey).Any(p => p.IsError && p.Message.Contains("loops back", StringComparison.Ordinal)));

        journey.Wires.RemoveAt(journey.Wires.Count - 1);
        journey.Wires.Add(JourneyWire.Of(exit, 0, check));
        Assert.IsTrue(JourneyGraph.Problems(journey).Any(p => p.IsError && p.Message.Contains("broken wire", StringComparison.Ordinal)), "Sinks have no outputs.");
        journey.Wires.RemoveAt(journey.Wires.Count - 1);

        journey.Nodes = [.. journey.Nodes.Select(n => n.Id == chance ? n with { Weights = [0, 0] } : n)];
        Assert.IsTrue(JourneyGraph.Problems(journey).Any(p => p.IsError && p.NodeId == chance), "All-zero weights.");
        journey.Nodes = [.. journey.Nodes.Select(n => n.Id == chance ? n with { Weights = [1] } : n)];
        Assert.IsTrue(JourneyGraph.Problems(journey).Any(p => p.IsError && p.NodeId == chance), "One output isn't a branch.");

        var empty = new JourneyTemplate();
        JourneyGraph.Ensure(empty);
        Assert.AreEqual(2, empty.Nodes.Count, "An empty journey becomes Spawn → Exit.");
        Assert.IsFalse(JourneyGraph.Problems(empty).Any());
        Assert.IsTrue(JourneyGraph.Problems(new JourneyTemplate { Nodes = [JourneyGraph.ExitNode()] }).Any(p => p.IsError && p.Message.Contains("no Spawn", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BuiltIn_NorthOrSouth_IsSound_WithTwoTrainSinks()
    {
        var journey = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.gates-north-south")!;
        Assert.IsFalse(JourneyGraph.Problems(journey).Any(), string.Join("; ", JourneyGraph.Problems(journey).Select(p => p.Message)));
        IReadOnlyList<JourneyNode> sinks = JourneyGraph.Sinks(journey);
        CollectionAssert.AreEquivalent(new[] { "Northbound", "Southbound" }, sinks.Select(s => s.TargetTag).ToArray());
        Assert.IsTrue(sinks.All(s => s.SinkKind == JourneySinkKind.BoardTrain));
        JourneyNode chance = journey.Nodes.Single(n => n.Kind == JourneyNodeKind.Chance);
        CollectionAssert.AreEqual(new[] { 50.0, 50.0 }, JourneyGraph.Percentages(chance).ToArray());
        Assert.AreEqual("50%", JourneyGraph.PortLabel(chance, 0));
        Assert.IsNull(journey.Steps);
        StringAssert.Contains(journey.Summarise(), "2 sinks");

        foreach (JourneyTemplate builtIn in BuiltInLibrary.Instance.Journeys)
        {
            Assert.IsFalse(JourneyGraph.Problems(builtIn).Any(p => p.IsError), builtIn.Key);
        }
    }

    [TestMethod]
    public void MixLine_ProfileDefault_FollowsTheProfilesJourney()
    {
        var study = new Study();
        var commuter = (AgentProfileTemplate)BuiltInLibrary.Instance.Find(Commuter)!;
        Assert.AreEqual(TemplateIds.FromKey("journey.through-gates"), commuter.DefaultJourneyId, "Built-in profiles have defaults (D-304).");

        MixLine explicitLine = MixLine.Of(Commuter, TemplateIds.FromKey("journey.ticket-gates"));
        MixLine defaultLine = explicitLine with { UseProfileJourney = true };
        Assert.AreEqual("journey.ticket-gates", StudyTemplates.JourneyFor(study, explicitLine)?.Key);
        Assert.AreEqual("journey.through-gates", StudyTemplates.JourneyFor(study, defaultLine)?.Key);
        Assert.IsNull(StudyTemplates.JourneyFor(study, MixLine.Of(Commuter)), "No journey: straight to a sink.");

        var own = (AgentProfileTemplate)TemplateCloner.Clone(commuter);
        own.Id = Guid.NewGuid();
        own.DefaultJourneyId = null;
        study.Library.Add(own);
        Assert.IsNull(StudyTemplates.JourneyFor(study, MixLine.Of(own.Id) with { UseProfileJourney = true }), "A profile without a default: straight to a sink.");
    }

    [TestMethod]
    public void Validation_ZoneSinksNeedASinkZone_AndProfileDefaultsAreChecked()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        var spawn = new Region { LevelId = level.Id, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 10)) };
        var sink = new Region { LevelId = level.Id, Name = "Out", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Sink, Outline = ShapeFactory.Rectangle(new(18, 0), new(20, 10)) };
        study.Regions.AddRange([spawn, sink]);

        var journey = new JourneyTemplate { Key = "journey.user.zone", Name = "Zone exit" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid exit = graph.Add(JourneyGraph.ZoneSinkNode("North"), 1);
        graph.Wire(graph.Start, exit).Apply(journey);
        study.Library.Add(journey);
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.Of(Commuter, journey.Id)], DestinationMode = DestinationMode.ChosenSinks, Destinations = [new SinkWeight { SinkId = sink.Id }] });

        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("sink zone tagged 'North'", StringComparison.Ordinal)));
        sink.Tag = "north";
        Assert.IsFalse(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error), "Tags match case-insensitively.");

        // The commuter's default (through gates) needs a gate the study doesn't have.
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(Commuter) with { UseProfileJourney = true }] };
        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("'Gate' device", StringComparison.Ordinal)));
    }
}
