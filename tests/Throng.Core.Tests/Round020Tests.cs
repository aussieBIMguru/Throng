using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 020: your library across studies (D-340–D-351) and picks that match nothing (D-349).</summary>
[TestClass]
public sealed class Round020Tests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");
    private static readonly Guid ThroughGates = TemplateIds.FromKey("journey.through-gates");

    [TestMethod]
    public void Lookups_GoStudyThenMineThenBuiltIn_OneRowPerId()
    {
        var study = new Study();
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out JourneyTemplate route, out DeviceTemplate kiosk);

        Assert.AreSame(runner, StudyTemplates.FindProfile(study, runner.Id, mine), "Yours when the study lacks it.");
        Assert.IsNull(StudyTemplates.FindProfile(study, runner.Id), "The simulation never looks in your library.");
        Assert.AreSame(route, StudyTemplates.FindJourney(study, route.Id, mine));
        Assert.AreSame(kiosk, StudyDefinitions.FindDevice(study, kiosk.Id, mine));

        IReadOnlyList<AgentProfileTemplate> profiles = StudyTemplates.AvailableProfiles(study, mine);
        Assert.AreSame(runner, profiles[0], "No study profiles: MINE first, then built-ins (D-343).");
        Assert.AreEqual(1 + BuiltInLibrary.Instance.AgentProfiles.Count, profiles.Count);

        // The study's copy wins over yours (same id), and is listed once.
        MyLibrary.EmbedForUse(study, mine, runner.Id);
        var copy = (AgentProfileTemplate)study.Library.Find(runner.Id)!;
        Assert.AreNotSame(runner, copy);
        Assert.AreSame(copy, StudyTemplates.FindProfile(study, runner.Id, mine));
        Assert.AreEqual(1, StudyTemplates.AvailableProfiles(study, mine).Count(p => p.Id == runner.Id));

        // A MINE item saved under a built-in's id shadows the built-in in lists and lookups.
        var myCommuter = (AgentProfileTemplate)TemplateCloner.Clone(BuiltInLibrary.Instance.Find(Commuter)!);
        myCommuter.Name = "My commuter";
        MyLibrary.Put(mine, myCommuter);
        Assert.AreEqual("My commuter", StudyTemplates.FindProfile(new Study(), Commuter, mine)!.Name);
        Assert.AreEqual(1, StudyTemplates.AvailableProfiles(new Study(), mine).Count(p => p.Id == Commuter));
    }

    [TestMethod]
    public void EmbedOnFirstUse_KeepsTheLibraryId_AndBringsTheDefaultJourney()
    {
        var study = new Study();
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out JourneyTemplate route, out _);

        IReadOnlyList<TemplateBase> added = MyLibrary.EmbedForUse(study, mine, runner.Id);
        CollectionAssert.AreEquivalent(new[] { runner.Id, route.Id }, added.Select(t => t.Id).ToArray(), "The profile and its default journey (D-346, D-350).");
        Assert.IsTrue(added.All(t => t.Source == TemplateSource.Embedded));
        Assert.AreEqual(0, MyLibrary.EmbedForUse(study, mine, runner.Id).Count, "Only on first use.");
        Assert.IsFalse(MyLibrary.Differs(study, mine, runner.Id), "A fresh copy matches yours.");

        // Built-ins still embed the way they always did (no default journey brought along).
        var other = new Study();
        Assert.AreEqual(1, MyLibrary.EmbedForUse(other, mine, Commuter).Count);
        Assert.IsNull(other.Library.Find(ThroughGates));
    }

    [TestMethod]
    public void EmbedReferenced_CatchesMixLinesAndPlacedDevices()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out JourneyTemplate route, out DeviceTemplate kiosk);
        var spawn = new Region { LevelId = level.Id, Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 2)) };
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.Of(runner.Id)] });
        study.Regions.Add(spawn);
        study.Devices.Add(new Device { LevelId = level.Id, Name = "Kiosk", TypeTag = kiosk.TypeTag, TemplateId = kiosk.Id });

        IReadOnlyList<TemplateBase> added = MyLibrary.EmbedReferenced(study, mine);
        CollectionAssert.AreEquivalent(new[] { runner.Id, route.Id, kiosk.Id }, added.Select(t => t.Id).ToArray());
        Assert.AreEqual(0, MyLibrary.EmbedReferenced(study, mine).Count, "Nothing twice.");
        Assert.AreEqual(0, MyLibrary.EmbedReferenced(new Study(), mine).Count, "Nothing the study doesn't use.");
    }

    [TestMethod]
    public void Differs_ThenUpdate_IsOneUndoStep_AndUndoRestores()
    {
        var study = new Study();
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out _, out _);
        MyLibrary.EmbedForUse(study, mine, runner.Id);
        var copy = (AgentProfileTemplate)study.Library.Find(runner.Id)!;

        runner.Speed = Distribution.Fixed(2.2);
        Assert.IsTrue(MyLibrary.Differs(study, mine, runner.Id), "Same id, different values (D-351).");

        // UPDATE FROM MY LIBRARY goes through the definition edit path: one ReplaceStateAction (D-344).
        var next = (AgentProfileTemplate)TemplateCloner.Clone(copy);
        StudyDefinitions.CopyValues(next, runner);
        var undo = new UndoStack();
        undo.Execute(new ReplaceStateAction<AgentProfileTemplate>("Update from my library", [(copy, next)], t => (AgentProfileTemplate)TemplateCloner.Clone(t), (into, from) => StudyDefinitions.CopyValues(into, from), null));
        Assert.IsFalse(MyLibrary.Differs(study, mine, runner.Id));
        Assert.AreSame(copy, study.Library.Find(runner.Id), "Updated in place, id kept.");
        Assert.AreEqual(TemplateSource.Embedded, copy.Source, "Source kept.");

        Assert.IsTrue(undo.Undo());
        Assert.IsTrue(MyLibrary.Differs(study, mine, runner.Id), "Undo brings the study's values back.");
    }

    [TestMethod]
    public void SaveToMine_ProfileBringsItsChangedDefaultJourney_AndAsksBeforeReplacing()
    {
        var study = new Study();
        var mine = new TemplateLibrary();

        // A built-in profile whose default journey is an unchanged built-in: just the profile.
        AgentProfileTemplate commuter = StudyDefinitions.EnsureStudyCopy(study, (AgentProfileTemplate)BuiltInLibrary.Instance.Find(Commuter)!);
        LibrarySavePlan plan = MyLibrary.PlanSave(study, mine, commuter);
        Assert.AreEqual(1, plan.Items.Count, "Unchanged built-in journeys aren't copied (they're everywhere).");
        Assert.AreEqual(0, plan.Replaces.Count);

        // Change the study's copy of its journey: now it comes along (D-350).
        JourneyTemplate gates = StudyDefinitions.EnsureStudyCopy(study, (JourneyTemplate)BuiltInLibrary.Instance.Find(ThroughGates)!);
        gates.Description = "Our gates.";
        plan = MyLibrary.PlanSave(study, mine, commuter);
        CollectionAssert.AreEqual(new[] { Commuter, ThroughGates }, plan.Items.Select(t => t.Id).ToArray());
        Assert.IsTrue(plan.Items.All(t => t.Source == TemplateSource.User));
        MyLibrary.ApplySave(mine, plan);
        Assert.AreEqual(1, mine.AgentProfiles.Count);
        Assert.AreEqual(1, mine.Journeys.Count);

        Assert.IsTrue(MyLibrary.PlanSave(study, mine, commuter).IsUpToDate, "Same values: nothing to write.");

        commuter.Name = "Hurried commuter";
        plan = MyLibrary.PlanSave(study, mine, commuter);
        Assert.AreEqual(1, plan.Replaces.Count, "Re-saving asks before replacing (D-345).");
        Assert.AreEqual(1, plan.UnchangedCount, "The journey is already there.");
        MyLibrary.ApplySave(mine, plan);
        Assert.AreEqual("Hurried commuter", mine.AgentProfiles.Single().Name, "Replaced in place.");
    }

    [TestMethod]
    public void SaveToMine_FlagsJourneysWithPicks()
    {
        var study = new Study();
        var journey = new JourneyTemplate { Key = "journey.user.picked", Name = "Picked gate" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid gate = graph.Add(JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(Guid.NewGuid())] }, 1);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 2);
        graph.Wire(graph.Start, gate).Wire(gate, exit).Apply(journey);
        study.Library.Add(journey);

        LibrarySavePlan plan = MyLibrary.PlanSave(study, new TemplateLibrary(), journey);
        Assert.AreEqual(1, plan.JourneysWithPicks.Count, "The save asks first (D-349).");
        Assert.IsTrue(((JourneyTemplate)plan.Items[0]).Nodes.Any(n => n.HasPicks), "Picks are kept.");
    }

    [TestMethod]
    public void RenameAndDelete_ChangeOnlyYourLibrary()
    {
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out _, out DeviceTemplate kiosk);
        Assert.IsFalse(MyLibrary.Rename(mine, runner.Id, "   "), "Blank names are refused.");
        Assert.IsTrue(MyLibrary.Rename(mine, runner.Id, " Jogger "));
        Assert.AreEqual("Jogger", runner.Name);
        Assert.IsTrue(MyLibrary.Remove(mine, kiosk.Id));
        Assert.IsFalse(MyLibrary.Remove(mine, kiosk.Id));
        Assert.AreEqual(0, mine.Devices.Count);
    }

    [TestMethod]
    public void Import_KeepBothSkipAndReplace()
    {
        TemplateLibrary mine = MineWith(out AgentProfileTemplate runner, out JourneyTemplate route, out DeviceTemplate kiosk);
        TemplateLibrary incoming = MyLibrary.BuildExport(mine, [runner.Id, kiosk.Id]);
        Assert.AreEqual(1, incoming.Journeys.Count, "A ticked profile takes its default journey along.");

        Assert.AreEqual(0, MyLibrary.FindImportClashes(mine, incoming).Count, "Same values: nothing to ask.");
        LibraryImportResult same = MyLibrary.ApplyImport(mine, incoming, []);
        Assert.AreEqual(3, same.Unchanged);
        Assert.IsFalse(same.Changed);

        // Someone else changed all three.
        incoming.AgentProfiles[0].Name = "Their runner";
        incoming.Devices[0].Name = "Their kiosk";
        incoming.Journeys[0].Description = "Their route.";
        List<ReconcileItem> clashes = MyLibrary.FindImportClashes(mine, incoming);
        Assert.AreEqual(3, clashes.Count);
        Assert.IsTrue(clashes.All(c => c.Kind == ReconcileKind.LibraryItem));
        Assert.AreEqual("Their runner", clashes[0].Value, "Profiles, then devices, then journeys.");

        // Profile: replace. Device: skip. Journey: keep both (the profile is replaced, so it follows the copy).
        LibraryImportResult result = MyLibrary.ApplyImport(mine, incoming,
            [new ReconcileChoice(ReconcileAction.Replace), new ReconcileChoice(ReconcileAction.Drop), ReconcileChoice.Keep]);
        Assert.AreEqual(1, result.Replaced);
        Assert.AreEqual(1, result.Skipped);
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual("Their runner", mine.AgentProfiles.Single().Name);
        Assert.AreEqual("Kiosk", mine.Devices.Single().Name, "Skipped.");
        Assert.AreEqual(2, mine.Journeys.Count, "Both journeys kept.");
        JourneyTemplate theirs = mine.Journeys.Single(j => j.Id != route.Id);
        StringAssert.Contains(theirs.Name, "(imported)");
        Assert.AreEqual(theirs.Id, mine.AgentProfiles.Single().DefaultJourneyId, "The replaced profile follows its journey's copy.");
    }

    [TestMethod]
    public void PicksThatMatchNothing_UseTheTypeOrTag_AndCheckNotesIt()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        var a = new Device { LevelId = level.Id, Name = "A", TypeTag = "Gate" };
        var b = new Device { LevelId = level.Id, Name = "B", TypeTag = "Gate" };
        study.Devices.AddRange([a, b]);
        var retail = new Region { LevelId = level.Id, Kind = RegionKind.Zone, ZoneKind = ZoneKind.Dwell, Tag = "Retail", Outline = ShapeFactory.Rectangle(new(4, 4), new(6, 6)) };
        study.Regions.Add(retail);

        JourneyNode gone = JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(Guid.NewGuid())] };
        Assert.IsFalse(JourneyTargets.UsesPicks(study, gone));
        Assert.IsTrue(JourneyTargets.PicksFallBack(study, gone));
        CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, JourneyTargets.Devices(study, gone).Select(d => d.Id).ToArray(), "By type instead (D-349).");

        JourneyNode partly = JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(b.Id), JourneyPick.Of(Guid.NewGuid())] };
        Assert.IsTrue(JourneyTargets.UsesPicks(study, partly));
        CollectionAssert.AreEqual(new[] { b.Id }, JourneyTargets.Devices(study, partly).Select(d => d.Id).ToArray(), "One pick still here: picks rule.");

        JourneyNode shop = JourneyGraph.DwellNode("Retail", Distribution.Fixed(10)) with { Picks = [JourneyPick.Of(Guid.NewGuid())] };
        CollectionAssert.AreEqual(new[] { retail.Id }, JourneyZones.Matching(study, shop).Select(z => z.Id).ToArray(), "Zones fall back to the tag.");
        Assert.AreEqual(1, JourneyTargets.MissingPicks(study, shop).Count);

        Assert.IsTrue(ValidationRules.All.Any(r => r.Id == ValidationRules.PicksFallback), "A new CHECK rule id.");
    }

    /// <summary>Your library with a profile, its default journey and a device definition.</summary>
    private static TemplateLibrary MineWith(out AgentProfileTemplate runner, out JourneyTemplate route, out DeviceTemplate kiosk)
    {
        route = new JourneyTemplate { Key = "journey.mine.route", Name = "Route" };
        var graph = new JourneyGraph.Builder(route.Id);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 1);
        graph.Wire(graph.Start, exit).Apply(route);
        runner = new AgentProfileTemplate { Key = "agent.mine.runner", Name = "Runner", Speed = Distribution.Fixed(1.8), DefaultJourneyId = route.Id };
        kiosk = new DeviceTemplate { Key = "device.mine.kiosk", Name = "Kiosk", Kind = DeviceKind.Point, TypeTag = "Kiosk", Servers = 1, ServiceTime = Distribution.Fixed(8) };
        var mine = new TemplateLibrary();
        mine.Add(runner);
        mine.Add(route);
        mine.Add(kiosk);
        return mine;
    }
}
