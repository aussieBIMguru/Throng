using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Tests;

/// <summary>
/// Round 024b: group templates as definitions (D-388): DUPLICATE / NEW / use counts, your library (SAVE TO MY LIBRARY
/// brings member profiles, export, import, embedding) and member names in summaries.
/// </summary>
[TestClass]
public sealed class Round024bTests
{
    private static readonly Guid Family = TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey);
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    [TestMethod]
    public void Duplicate_New_AndUseCounts()
    {
        Study study = StudyWithSpawn(MixLine.OfGroup(Family));
        var family = (GroupTemplate)BuiltInLibrary.Instance.Find(Family)!;
        GroupTemplate copy = StudyDefinitions.CreateGroupCopy(study, family);
        Assert.AreNotEqual(family.Id, copy.Id);
        Assert.AreEqual("Family (copy)", copy.Name);
        Assert.AreEqual(TemplateSource.User, copy.Source);
        CollectionAssert.AreEqual(family.Members, copy.Members, "Same member rows.");
        Assert.AreEqual(family.DefaultJourneyId, copy.DefaultJourneyId);

        GroupTemplate created = StudyDefinitions.CreateNewGroup(study);
        Assert.AreEqual("New group", created.Name);
        Assert.AreEqual(2, created.MinSize, "Two commuters to start with.");

        Assert.AreEqual(1, StudyDefinitions.GroupUseCount(study, Family));
        Assert.AreEqual(0, StudyDefinitions.GroupUseCount(study, copy.Id));
        Assert.AreEqual(1, StudyDefinitions.ProfileUseCount(study, family.Members[0].ProfileId), "A group line uses its members' profiles.");

        study.Library.Groups.Add(created);
        Assert.AreEqual(1, StudyDefinitions.GroupsWithMember(study, Commuter).Count, "The study's group lists Commuter.");
    }

    [TestMethod]
    public void SaveToMine_BringsOwnMemberProfiles_NotPristineBuiltIns()
    {
        var study = new Study();
        AgentProfileTemplate kid = OwnProfile("Small kid");
        study.Library.AgentProfiles.Add(kid);
        GroupTemplate group = StudyDefinitions.CreateNewGroup(study);
        group.Members = [GroupMember.Of(Commuter, 1, 1), GroupMember.Of(kid.Id, 1, 2)];
        study.Library.Groups.Add(group);

        var mine = new TemplateLibrary();
        LibrarySavePlan plan = MyLibrary.PlanSave(study, mine, group);
        CollectionAssert.AreEquivalent(new[] { group.Id, kid.Id }, plan.Items.Select(t => t.Id).ToArray(), "The group and its own member; the built-in commuter stays out.");
        MyLibrary.ApplySave(mine, plan);
        Assert.AreEqual(1, mine.Groups.Count);
        Assert.IsTrue(MyLibrary.Holds(mine.Groups[0]));
        Assert.IsTrue(MyLibrary.PlanSave(study, mine, group).IsUpToDate, "Saving again changes nothing.");
    }

    [TestMethod]
    public void Export_GroupTakesItsMembers_AndImportKeepsBothFollowsCopies()
    {
        var mine = new TemplateLibrary();
        AgentProfileTemplate kid = OwnProfile("Small kid");
        mine.AgentProfiles.Add(kid);
        var group = new GroupTemplate { Id = Guid.NewGuid(), Key = "group.mine.test", Name = "Little ones", Members = [GroupMember.Of(kid.Id, 2, 3)] };
        mine.Groups.Add(group);

        TemplateLibrary export = MyLibrary.BuildExport(mine, [group.Id]);
        Assert.AreEqual(1, export.Groups.Count);
        Assert.AreEqual(1, export.AgentProfiles.Count, "The member profile goes with the group.");

        // The same items with different values: keep both makes copies, and the copied group follows the copied profile.
        TemplateLibrary incoming = Clone(export);
        incoming.AgentProfiles[0].RadiusM = 0.3;
        incoming.Groups[0].Description = "Changed.";
        List<ReconcileItem> clashes = MyLibrary.FindImportClashes(mine, incoming);
        Assert.AreEqual(2, clashes.Count, "Profile and group clash.");
        LibraryImportResult result = MyLibrary.ApplyImport(mine, incoming, [ReconcileChoice.Keep, ReconcileChoice.Keep]);
        Assert.AreEqual(2, result.Added);
        GroupTemplate imported = mine.Groups.Single(g => g.Id != group.Id);
        AgentProfileTemplate importedKid = mine.AgentProfiles.Single(p => p.Id != kid.Id);
        Assert.AreEqual(importedKid.Id, imported.Members[0].ProfileId, "The imported group uses the imported copy of its member.");
        StringAssert.EndsWith(imported.Name, "(imported)");
    }

    [TestMethod]
    public void EmbedReferenced_AGroupLineFromMineBringsTheGroupAndItsMembers()
    {
        var mine = new TemplateLibrary();
        AgentProfileTemplate kid = OwnProfile("Small kid");
        mine.AgentProfiles.Add(kid);
        var group = new GroupTemplate { Id = Guid.NewGuid(), Key = "group.mine.test", Name = "Little ones", Members = [GroupMember.Of(Commuter, 1, 1), GroupMember.Of(kid.Id, 1, 1)] };
        mine.Groups.Add(group);

        Study study = StudyWithSpawn(MixLine.OfGroup(group.Id));
        IReadOnlyList<TemplateBase> added = MyLibrary.EmbedReferenced(study, mine);
        CollectionAssert.AreEquivalent(new[] { group.Id, kid.Id }, added.Select(t => t.Id).ToArray(), "Group and its member from your library; built-ins are left.");
        Assert.IsNotNull(StudyTemplates.FindGroup(study, group.Id), "The study no longer needs your library.");
        Assert.AreEqual(0, MyLibrary.EmbedReferenced(study, mine).Count, "Nothing more the second time.");
    }

    [TestMethod]
    public void Summary_NamesStudyProfiles()
    {
        AgentProfileTemplate kid = OwnProfile("Small kid");
        var group = new GroupTemplate { Members = [GroupMember.Of(Commuter, 1, 1), GroupMember.Of(kid.Id, 1, 2)] };
        StringAssert.Contains(group.Summarise(), "profile", "Built-in names only: the study's profile shows as 'profile'.");
        string named = group.Summarise(id => id == kid.Id ? kid.Name : BuiltInLibrary.Instance.Find(id)?.Name);
        StringAssert.Contains(named, "small kid");
        StringAssert.StartsWith(named, "2–3 people");
    }

    private static Study StudyWithSpawn(MixLine line)
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        var spawn = new Region { LevelId = level.Id, Name = "West", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn };
        spawn.EmissionRules.Add(new EmissionRule { Mix = [line], RatePerMinute = 10 });
        study.Regions.Add(spawn);
        return study;
    }

    private static AgentProfileTemplate OwnProfile(string name)
    {
        Guid id = Guid.NewGuid();
        return new AgentProfileTemplate { Id = id, Key = $"agent.user.{id:N}", Name = name, Source = TemplateSource.User, Speed = Distribution.Fixed(1.1) };
    }

    private static TemplateLibrary Clone(TemplateLibrary library)
    {
        var copy = new TemplateLibrary();
        foreach (TemplateBase item in library.All())
        {
            copy.Add(TemplateCloner.Clone(item));
        }

        return copy;
    }
}
