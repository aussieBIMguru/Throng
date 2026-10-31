using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 016: rule ids on findings and ignore keys (D-272), the inverse explode (D-277), profile reconcile (D-275).</summary>
[TestClass]
public sealed class Round016Tests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    [TestMethod]
    public void Findings_CarryTheirRule_AndAStableIgnoreKey()
    {
        var study = new Study();
        var level = new Level { Name = "Flat", HeightM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Sliver", Kind = RegionKind.Walkable, Outline = [new(0, 0), new(1, 0), new(2, 0)] });

        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        ValidationMessage height = findings.Single(f => f.Message.Contains("height of zero", StringComparison.Ordinal));
        ValidationMessage area = findings.Single(f => f.Message.Contains("no area", StringComparison.Ordinal));
        Assert.AreEqual(ValidationRules.Levels, height.Rule);
        Assert.AreEqual(ValidationRules.Regions, area.Rule);
        Assert.IsTrue(findings.All(f => f.Rule != ValidationRules.General), "Every static finding has a rule.");
        Assert.AreEqual($"{ValidationRules.Levels}|{level.Id:D}", height.IgnoreKey);
        Assert.AreEqual(height.IgnoreKey, (height with { Message = "changed words" }).IgnoreKey, "An object's finding keeps its key when the wording changes.");
        Assert.AreEqual(ValidationRules.All.Count, ValidationRules.All.Select(r => r.Id).Distinct().Count(), "Rule ids are unique.");
    }

    [TestMethod]
    public void Explode_UnwarpUndoesWarp()
    {
        double[] levels = [0, 5, 12];
        foreach (double z in new[] { -2.0, 0, 1.3, 5, 7.7, 12, 15 })
        {
            Assert.AreEqual(z, ExplodeMap.Unwarp(ExplodeMap.Warp(z, levels, 4), levels, 4), 1e-9, $"z = {z}");
        }
    }

    [TestMethod]
    public void Reconcile_FindsWhatTheStudyLacks_AndAppliesKeepMapDrop()
    {
        Study study = StudyWithAGate(out Guid levelId);
        var profile = new AgentProfileTemplate
        {
            Id = Guid.NewGuid(),
            Key = "agent.user.test",
            Name = "Tester",
            DevicePreferences = [new DevicePreference { Target = "Turnstile", BonusS = 5 }, new DevicePreference { Target = "Gate", BonusS = 2 }],
        };
        var journey = new JourneyTemplate
        {
            Id = Guid.NewGuid(),
            Key = "journey.user.test",
            Name = "Test journey",
            Steps =
            [
                JourneyStep.Device("Turnstile"),
                JourneyStep.Device("Kiosk", needs: "Hungry"),
                JourneyStep.DwellIn("Retail", Distribution.Uniform(10, 20)) with { ZoneId = Guid.NewGuid() },
                JourneyStep.ExitStep(),
            ],
        };
        var bundle = new ProfileBundle { Profile = profile, Journeys = [journey] };

        List<ReconcileItem> items = ProfileReconciler.Find(study, bundle);
        CollectionAssert.AreEquivalent(new[] { "Turnstile", "Kiosk", "Hungry", "Retail" }, items.Select(i => i.Value).ToArray());
        Assert.AreEqual(1, items.Count(i => i.Value == "Turnstile"), "One item per value, wherever it's used.");
        CollectionAssert.AreEqual(new[] { "Gate" }, items.Single(i => i.Value == "Turnstile").MapOptions.ToArray());

        ReconcileChoice[] choices = [.. items.Select(i => i.Value switch
        {
            "Turnstile" => new ReconcileChoice(ReconcileAction.Map, "Gate"),
            "Kiosk" => new ReconcileChoice(ReconcileAction.Drop),
            "Hungry" => new ReconcileChoice(ReconcileAction.Drop),
            _ => ReconcileChoice.Keep,
        })];
        (AgentProfileTemplate imported, List<JourneyTemplate> journeys) = ProfileReconciler.Apply(study, bundle, items, choices);

        Assert.AreEqual(TemplateSource.Imported, imported.Source);
        CollectionAssert.AreEqual(new[] { "Gate", "Gate" }, imported.DevicePreferences.Select(p => p.Target).ToArray(), "Turnstile mapped to Gate.");
        JourneyTemplate copy = journeys.Single();
        // The old step list arrives as a graph (D-302): dropping Kiosk and the Hungry check leaves Gate → Retail → Exit.
        JourneyNode[] chain = [.. JourneyGraph.Reachable(copy).Where(n => n.Kind != JourneyNodeKind.Spawn)];
        Assert.AreEqual(3, chain.Length, "The Kiosk node and its tag check were dropped: " + copy.Summarise());
        Assert.AreEqual("Gate", chain[0].TargetTag);
        Assert.AreEqual("Retail", chain[1].TargetTag, "Kept as is.");
        Assert.IsNull(chain[1].ZoneId, "Pinned zones belong to the old study.");
        Assert.AreEqual(JourneyNodeKind.Sink, chain[2].Kind);
        Assert.IsFalse(JourneyGraph.Problems(copy).Any(p => p.IsError), "No broken wires left behind.");
        Assert.AreEqual(journey.Id, copy.Id, "No clash, so the id is kept.");

        // Importing again: the ids are taken now, so the copies get new ones.
        study.Library.AgentProfiles.Add(imported);
        study.Library.Journeys.Add(copy);
        (AgentProfileTemplate again, List<JourneyTemplate> againJourneys) = ProfileReconciler.Apply(study, bundle, items, choices);
        Assert.AreNotEqual(imported.Id, again.Id);
        Assert.AreNotEqual(copy.Id, againJourneys[0].Id);
        StringAssert.EndsWith(again.Name, "(imported)");
    }

    [TestMethod]
    public void Bundle_TakesTheJourneysTheProfileIsUsedWith()
    {
        Study study = StudyWithAGate(out Guid levelId);
        var builtIn = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.ticket-gates")!;
        var other = (JourneyTemplate)BuiltInLibrary.Instance.Journeys.First(j => j.Id != builtIn.Id);
        var spawn = new Region { LevelId = levelId, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 2)) };
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.Of(Commuter, builtIn.Id), MixLine.Of(TemplateIds.FromKey("agent.luggage"), other.Id)] });
        study.Regions.Add(spawn);

        var commuter = (AgentProfileTemplate)BuiltInLibrary.Instance.Find(Commuter)!;
        ProfileBundle bundle = ProfileReconciler.BuildBundle(study, commuter);
        Assert.AreEqual(commuter.Id, bundle.Profile.Id);
        Assert.AreNotSame(commuter, bundle.Profile, "A copy, not the built-in itself.");
        CollectionAssert.AreEqual(new[] { commuter.DefaultJourneyId!.Value, builtIn.Id }, bundle.Journeys.Select(j => j.Id).ToArray(),
            "Its default journey (D-304), then only the journeys used with this profile.");
    }

    private static Study StudyWithAGate(out Guid levelId)
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Devices.Add(new Device { LevelId = level.Id, TypeTag = "Gate", Position = new(5, 5) });
        levelId = level.Id;
        return study;
    }
}
