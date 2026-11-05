using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 009b: journey copies, give-up pathways, profile device preferences and shared snakes (D-161–D-166).</summary>
[TestClass]
public sealed class Round009bTests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    [TestMethod]
    public void DuplicateJourney_HasANewIdKeyAndName_AndOnlyTheCopyIsEditable()
    {
        var study = new Study();
        var builtIn = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.ticket-gates")!;
        JourneyTemplate copy = StudyTemplates.CreateJourneyCopy(study, builtIn);

        Assert.AreNotEqual(builtIn.Id, copy.Id);
        Assert.AreNotEqual(builtIn.Key, copy.Key);
        Assert.AreEqual("Ticket then gates (copy)", copy.Name);
        CollectionAssert.AreEqual(builtIn.Nodes, copy.Nodes, "Same nodes (records compare by value, D-302).");
        CollectionAssert.AreEqual(builtIn.Wires, copy.Wires, "Same wires.");
        Assert.IsFalse(StudyTemplates.IsEditableJourney(study, copy), "Not editable until it's in the study library.");

        study.Library.Add(copy);
        Assert.IsTrue(StudyTemplates.IsEditableJourney(study, copy));
        Assert.AreEqual("Ticket then gates (copy 2)", StudyTemplates.CreateJourneyCopy(study, builtIn).Name, "Names don't repeat.");

        // A built-in embedded by a mix line keeps the built-in's id and stays read-only (D-161).
        study.Library.AddCopy(builtIn, TemplateSource.Embedded);
        Assert.IsFalse(StudyTemplates.IsEditableJourney(study, study.Library.Journeys.Single(j => j.Id == builtIn.Id)));
    }

    [TestMethod]
    public void GiveUpLoop_IsAnError_AndAForwardGaveUpWireIsFine()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        var spawn = new Region { LevelId = level.Id, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 10)) };
        var sink = new Region { LevelId = level.Id, Name = "Out", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Sink, Outline = ShapeFactory.Rectangle(new(18, 0), new(20, 10)) };
        study.Regions.AddRange([spawn, sink]);
        study.Devices.Add(new Device { LevelId = level.Id, TypeTag = "TicketMachine", Position = new(10, 5) });
        var journey = new JourneyTemplate { Key = "journey.t", Name = "Jumpy", Category = "Journeys" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid machine = graph.Add(JourneyGraph.DeviceNode("TicketMachine", onGiveUp: JourneyGiveUp.Branch), 1);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 2);
        graph.Wire(graph.Start, machine).Wire(machine, exit).Wire(machine, 1, machine);
        graph.Apply(journey);
        study.Library.Add(journey);
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.Of(Commuter, journey.Id)], DestinationMode = DestinationMode.ChosenSinks, Destinations = [new SinkWeight { SinkId = sink.Id }] });

        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("loops back", StringComparison.Ordinal)), "Gave up back into itself is a loop (D-306).");

        journey.Wires = [.. journey.Wires.Where(w => w.Port != 1), JourneyWire.Of(machine, 1, exit)];
        Assert.IsFalse(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error), "Gave up into the exit is fine.");
    }

    [TestMethod]
    public void BuiltInProfiles_WheelchairAndLuggage_FavourTheAccessibleGate()
    {
        var mobility = (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey("agent.mobility")!;
        var luggage = (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey("agent.luggage")!;
        var commuter = (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey("agent.commuter")!;

        Assert.IsTrue(mobility.DeviceBonusS(BuiltInLibrary.AccessibleGateKey, "Gate") > luggage.DeviceBonusS(BuiltInLibrary.AccessibleGateKey, "Gate"));
        Assert.IsTrue(luggage.DeviceBonusS(BuiltInLibrary.AccessibleGateKey, "Gate") > 0);
        Assert.AreEqual(0, mobility.DeviceBonusS("device.gate-line", "Gate"), "Only the accessible gate.");
        Assert.AreEqual(0, commuter.DeviceBonusS(BuiltInLibrary.AccessibleGateKey, "Gate"));
        StringAssert.Contains(mobility.Summarise(), "prefers accessible wide gate");

        var custom = new AgentProfileTemplate { DevicePreferences = [new DevicePreference { Target = "gate", BonusS = 5 }, new DevicePreference { Target = "Gate", BonusS = 2 }] };
        Assert.AreEqual(7, custom.DeviceBonusS("device.gate-line", "Gate"), "Type tags match case-insensitively and add up.");
    }

    [TestMethod]
    public void DrawnQueue_MovesAndTurnsWithItsDevice()
    {
        var device = new Device { Position = new(0, 0), QueuePath = [new(0, 1), new(0, 4)] };
        Device moved = Throng.Core.Editing.DeviceFactory.Translated(device, new Point2(2, 0));
        CollectionAssert.AreEqual(new List<Point2> { new(2, 1), new(2, 4) }, moved.QueuePath);

        Device turned = Throng.Core.Editing.DeviceFactory.Rotated(device, device.Position, 90);
        Assert.AreEqual(-1, turned.QueuePath[0].X, 1e-9);
        Assert.AreEqual(0, turned.QueuePath[0].Y, 1e-9);
        Assert.AreEqual(2, device.QueuePath.Count, "The original is untouched.");
        Assert.AreEqual(1, device.QueuePath[0].Y);
    }

    [TestMethod]
    public void QueueLines_DrawnPath_IsOneSnake_AndLanesKeepOnlyTheirServicePoints()
    {
        var device = new Device { Position = new(0, 0), AngleDeg = 90, WidthM = 3, DepthM = 1, Lanes = 3, MaxQueue = 10 };
        Point2 start = QueueLines.QueueStart(device);
        Assert.AreEqual(0.9, start.Y, 1e-9, "Half the depth plus the service offset.");
        device.QueuePath = [start, start + new Point2(0, 3.6)];

        QueueLineSet set = QueueLines.Build(device, _ => true);
        Assert.AreEqual(3, set.Lines.Length);
        Assert.IsTrue(set.Lines.All(l => l.Length == 1), "Each lane line is just its service point.");
        Assert.IsNotNull(set.Snake);
        Assert.AreEqual(7, set.Snake!.Length, "3.6 m at 0.6 m spacing, both ends included.");
        Assert.AreEqual(0.6, Point2.Distance(set.Snake[0], set.Snake[1]), 1e-9);

        device.QueuePath = [];
        QueueLineSet auto = QueueLines.Build(device, _ => true);
        Assert.IsNull(auto.Snake);
        Assert.IsTrue(auto.Lines.All(l => l.Length == 10), "Automatic lines run to the max queue on open floor.");
    }
}
