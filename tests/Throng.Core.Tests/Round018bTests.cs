using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 018b: pick lists (D-318), graph edits (D-325–D-327) and built-in journey overrides (D-328).</summary>
[TestClass]
public sealed class Round018bTests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    [TestMethod]
    public void Edits_ConnectReplacesAndRefusesLoopsAndSpawn()
    {
        var journey = new JourneyTemplate { Name = "Edit" };
        JourneyGraph.Ensure(journey);
        JourneyNode spawn = JourneyGraph.Spawn(journey)!;
        JourneyNode exit = JourneyGraph.Sinks(journey).Single();
        Guid gate = JourneyEdits.Add(journey, JourneyEdits.Create(JourneyNodeKind.Device, 200, 0))!.Value;
        Guid chance = JourneyEdits.Add(journey, JourneyEdits.Create(JourneyNodeKind.Chance, 400, 0))!.Value;

        Assert.IsNull(JourneyEdits.Add(journey, JourneyEdits.Create(JourneyNodeKind.Spawn, 0, 0)), "One Spawn only.");
        Assert.IsNull(JourneyEdits.Connect(journey, spawn.Id, 0, gate), "Spawn → gate replaces Spawn → exit.");
        Assert.AreEqual(gate, JourneyGraph.Next(journey, spawn.Id, 0)?.Id);
        Assert.AreEqual(1, journey.Wires.Count(w => w.From == spawn.Id), "One wire per output.");
        Assert.IsNull(JourneyEdits.Connect(journey, gate, 0, chance));
        Assert.IsNull(JourneyEdits.Connect(journey, chance, 0, exit.Id));
        Assert.IsNotNull(JourneyEdits.Connect(journey, chance, 1, gate), "Back into the gate would loop (D-306).");
        Assert.IsNotNull(JourneyEdits.Connect(journey, gate, 0, spawn.Id), "Nothing wires into Spawn.");
        Assert.IsNotNull(JourneyEdits.Connect(journey, gate, 5, exit.Id), "No such port.");
        Assert.IsFalse(JourneyGraph.HasLoop(journey));

        // Fewer chance outputs drop the wires from the outputs that went.
        Assert.IsNull(JourneyEdits.Connect(journey, chance, 1, exit.Id));
        JourneyNode node = JourneyGraph.Find(journey, chance)!;
        Assert.IsTrue(JourneyEdits.Replace(journey, node with { Weights = [1] }));
        Assert.IsFalse(journey.Wires.Any(w => w.From == chance && w.Port == 1));

        Assert.IsFalse(JourneyEdits.Remove(journey, spawn.Id), "Spawn stays.");
        Assert.IsTrue(JourneyEdits.Remove(journey, gate));
        Assert.IsFalse(journey.Wires.Any(w => w.From == gate || w.To == gate), "Its wires go with it.");
        Assert.IsTrue(JourneyEdits.Move(journey, chance, 123.46, 67.0), "123.45 is 123.4499… in binary, so it would round down.");
        Assert.AreEqual(123.5, JourneyGraph.Find(journey, chance)!.X, 1e-9);
        Assert.IsTrue(JourneyEdits.Disconnect(journey, chance, 0));
        Assert.IsFalse(JourneyEdits.Disconnect(journey, chance, 0));
    }

    [TestMethod]
    public void Parsing_WeightsAndRanges()
    {
        Assert.IsTrue(JourneyEdits.TryParseWeights("3, 1, 1", out List<double> weights));
        CollectionAssert.AreEqual(new[] { 3.0, 1.0, 1.0 }, weights.ToArray());
        Assert.IsFalse(JourneyEdits.TryParseWeights("5", out _), "Two outputs at least.");
        Assert.IsFalse(JourneyEdits.TryParseWeights("0, 0", out _), "One above zero.");
        Assert.IsFalse(JourneyEdits.TryParseWeights("1,1,1,1,1,1,1", out _), "Six at most.");
        Assert.IsTrue(JourneyEdits.TryParseRange("30-180", out Distribution? range));
        Assert.AreEqual(30, range!.Min);
        Assert.AreEqual(180, range.Max);
        Assert.IsTrue(JourneyEdits.TryParseRange("60", out Distribution? fixedRange));
        Assert.AreEqual(60, fixedRange!.Mean);
        Assert.IsFalse(JourneyEdits.TryParseRange("soon", out _));
    }

    [TestMethod]
    public void Picks_OverrideTagsForDevicesAndZones_WithWeights()
    {
        Study study = Hall(out Region shopA, out Region shopB, out Device left, out Device right);
        JourneyNode device = JourneyGraph.DeviceNode("TicketMachine");
        Assert.AreEqual(2, JourneyTargets.Devices(study, device).Count, "By type: both machines.");
        device = JourneyEdits.TogglePick(device, right.Id);
        CollectionAssert.AreEqual(new[] { right.Id }, JourneyTargets.Devices(study, device).Select(d => d.Id).ToArray(), "Picked: just that one (D-318).");
        Assert.IsTrue(JourneyEdits.TogglePick(device, right.Id).Picks is null, "Toggling it again empties the list.");

        JourneyNode dwell = JourneyGraph.DwellNode("Retail", Distribution.Fixed(10)) with { Picks = [JourneyPick.Of(shopB.Id, 3), JourneyPick.Of(Guid.NewGuid())] };
        CollectionAssert.AreEqual(new[] { shopB.Id }, JourneyZones.Matching(study, dwell).Select(z => z.Id).ToArray(), "Only picked zones that exist.");
        Assert.AreEqual(3, JourneyTargets.WeightOf(dwell, shopB.Id));
        Assert.AreEqual(1, JourneyTargets.MissingPicks(study, dwell).Count);
        Assert.AreEqual(1, JourneyTargets.WeightOf(JourneyGraph.DwellNode("Retail", Distribution.Fixed(10)), shopA.Id), "By tag: weight 1.");
        StringAssert.Contains(dwell.Describe(), "2 picked");
    }

    [TestMethod]
    public void Validation_PicksThatAreGone_AndAllZeroWeights()
    {
        Study study = Hall(out _, out Region shopB, out _, out _);
        var journey = new JourneyTemplate { Key = "journey.user.picks", Name = "Picks" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid shop = graph.Add(JourneyGraph.DwellNode("Retail", Distribution.Fixed(10)) with { Picks = [JourneyPick.Of(Guid.NewGuid())] }, 1);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 2);
        graph.Wire(graph.Start, shop).Wire(shop, exit).Apply(journey);
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(Commuter, journey.Id)] };

        // Round 020 (D-349): picks that match nothing here fall back to the tag, and CHECK notes it (was an error in 018b).
        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Info && m.Rule == ValidationRules.PicksFallback));
        journey.Nodes = [.. journey.Nodes.Select(n => n.Id == shop ? n with { Picks = [JourneyPick.Of(shopB.Id, 0)], ChooseBy = JourneyChooseBy.RandomWeighted } : n)];
        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("every weight is zero", StringComparison.Ordinal)));
        journey.Nodes = [.. journey.Nodes.Select(n => n.Id == shop ? n with { Picks = [JourneyPick.Of(shopB.Id, 2)] } : n)];
        Assert.IsFalse(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error), string.Join("; ", StudyValidator.Validate(study).Select(m => m.Message)));
    }

    [TestMethod]
    public void BuiltInJourneys_AreEditedAsAnOverride_AndReset()
    {
        var study = new Study();
        var builtIn = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.through-gates")!;
        Assert.IsTrue(StudyTemplates.CanEditJourney(study, builtIn), "Built-ins can be edited (D-328).");
        Assert.IsFalse(StudyTemplates.IsEditableJourney(study, builtIn), "…but they aren't the study's own (no DELETE).");

        JourneyTemplate copy = StudyDefinitions.EnsureStudyCopy(study, builtIn);
        Assert.AreEqual(builtIn.Id, copy.Id, "The override is the built-in's id.");
        Assert.IsFalse(StudyDefinitions.IsModified(study, copy));
        JourneyNode gate = copy.Nodes.Single(n => n.Kind == JourneyNodeKind.Device);
        Assert.IsTrue(JourneyEdits.Replace(copy, gate with { ChooseBy = JourneyChooseBy.Nearest }));
        Assert.IsTrue(StudyDefinitions.IsModified(study, copy), "MODIFIED.");
        Assert.AreEqual(JourneyChooseBy.Quickest, builtIn.Nodes.Single(n => n.Kind == JourneyNodeKind.Device).ChooseBy, "The built-in itself never changes.");

        StudyDefinitions.CopyValues(copy, builtIn);
        Assert.IsFalse(StudyDefinitions.IsModified(study, copy), "RESET TO BUILT-IN.");
    }

    /// <summary>A 20 × 10 m hall with two Retail dwell zones, two ticket machines, a spawn and a sink.</summary>
    private static Study Hall(out Region shopA, out Region shopB, out Device left, out Device right)
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        var spawn = new Region { LevelId = level.Id, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 10)) };
        var sink = new Region { LevelId = level.Id, Name = "Out", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Sink, Outline = ShapeFactory.Rectangle(new(18, 0), new(20, 10)) };
        shopA = new Region { LevelId = level.Id, Name = "Shop A", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Dwell, Tag = "Retail", Outline = ShapeFactory.Rectangle(new(6, 0), new(9, 3)) };
        shopB = new Region { LevelId = level.Id, Name = "Shop B", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Dwell, Tag = "Retail", Outline = ShapeFactory.Rectangle(new(11, 7), new(14, 10)) };
        study.Regions.AddRange([spawn, sink, shopA, shopB]);
        left = new Device { LevelId = level.Id, Name = "Left machine", TypeTag = "TicketMachine", Position = new(5, 5) };
        right = new Device { LevelId = level.Id, Name = "Right machine", TypeTag = "TicketMachine", Position = new(15, 5) };
        study.Devices.AddRange([left, right]);
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.Of(Commuter)], DestinationMode = DestinationMode.ChosenSinks, Destinations = [new SinkWeight { SinkId = sink.Id }] });
        return study;
    }
}
