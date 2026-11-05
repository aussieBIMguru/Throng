using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Tests;

/// <summary>Round 024a: group templates (D-382, D-386–D-389) and the removed Family profile (D-385).</summary>
[TestClass]
public sealed class Round024Tests
{
    [TestMethod]
    public void Roll_StaysWithinEachRow_AndTwoToEightPeople()
    {
        var random = new Random(5);
        (int, int)[] rows = [(1, 2), (1, 3)];
        for (int k = 0; k < 500; k++)
        {
            int[] counts = GroupTemplate.Roll(rows, random.NextDouble);
            Assert.IsTrue(counts[0] is >= 1 and <= 2 && counts[1] is >= 1 and <= 3, string.Join(",", counts));
        }

        int[] big = GroupTemplate.Roll([(3, 3), (6, 6)], () => 0.5);
        Assert.AreEqual(GroupTemplate.MaxPeople, big.Sum(), "Cut to 8…");
        Assert.AreEqual(3, big[0], "…from the last rows.");

        int[] small = GroupTemplate.Roll([(1, 1), (0, 2)], () => 0.0);
        Assert.AreEqual(GroupTemplate.MinPeople, small.Sum(), "Topped up to 2 where a row allows.");

        int[] lone = GroupTemplate.Roll([(1, 1)], () => 0.0);
        Assert.AreEqual(1, lone.Sum(), "A one-person template can't be topped up; it stays one.");
    }

    [TestMethod]
    public void MeanSize_IsTheSumOfRowMidpoints()
    {
        Assert.AreEqual(3.5, GroupTemplate.MeanSize([(1, 2), (1, 3)]), 1e-9);
        Assert.AreEqual(2.0, GroupTemplate.MeanSize([(2, 2)]), 1e-9);
    }

    [TestMethod]
    public void BuiltIns_FamilyGroupReplacesTheWideProfile()
    {
        TemplateLibrary lib = BuiltInLibrary.Instance;
        Assert.IsNull(lib.FindByKey(BuiltInLibrary.RemovedFamilyProfileKey), "The one-wide-agent profile is gone (D-385).");
        var family = (GroupTemplate)lib.FindByKey(BuiltInLibrary.FamilyGroupKey)!;
        Assert.AreEqual(2, family.MinSize);
        Assert.AreEqual(5, family.MaxSize);
        Assert.IsNotNull(family.DefaultJourneyId);
        foreach (GroupTemplate group in lib.Groups)
        {
            Assert.IsTrue(group.Rows().All(m => lib.Find(m.ProfileId) is AgentProfileTemplate), group.Key);
            Assert.IsTrue(group.MaxSize <= GroupTemplate.MaxPeople, group.Key);
        }
    }

    [TestMethod]
    public void LineProfiles_AGroupLineBringsItsMembers()
    {
        var study = new Study();
        Guid family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);
        IReadOnlyList<AgentProfileTemplate> members = StudyTemplates.LineProfiles(study, MixLine.OfGroup(family));
        CollectionAssert.AreEqual(new[] { "agent.infrequent", "agent.child" }, members.Select(p => p.Key).ToArray());
        Assert.AreEqual("agent.commuter", StudyTemplates.LineProfiles(study, MixLine.OfGroup(Guid.NewGuid())).Single().Key, "Unknown group: Commuter.");
        Assert.AreEqual(((GroupTemplate)BuiltInLibrary.Instance.Find(family)!).DefaultJourneyId,
            StudyTemplates.JourneyIdFor(study, MixLine.OfGroup(family) with { UseProfileJourney = true }), "(profile default) = the group's journey.");
    }

    [TestMethod]
    public void ProfileUseCount_CountsGroupMembers()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        var spawn = new Region { LevelId = level.Id, Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Name = "In" };
        spawn.EmissionRules.Add(new EmissionRule { Mix = [MixLine.OfGroup(TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey))] });
        study.Regions.Add(spawn);
        Assert.AreEqual(1, StudyDefinitions.ProfileUseCount(study, TemplateIds.FromKey("agent.child")));
        Assert.AreEqual(0, StudyDefinitions.ProfileUseCount(study, TemplateIds.FromKey("agent.commuter")), "The line's leftover profile id isn't a use.");
    }

    [TestMethod]
    public void EmbedForUse_AGroupBringsItsMemberProfiles()
    {
        var study = new Study();
        Guid family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);
        MyLibrary.EmbedForUse(study, null, family);
        Assert.IsNotNull(study.Library.Find(family) as GroupTemplate);
        Assert.IsNotNull(study.Library.Find(TemplateIds.FromKey("agent.child")) as AgentProfileTemplate);
        Assert.IsNotNull(study.Library.Find(TemplateIds.FromKey("agent.infrequent")) as AgentProfileTemplate);
    }
}
