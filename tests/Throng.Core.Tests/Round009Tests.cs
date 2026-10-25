using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Round 009a: journeys, mix lines and their validation (D-149–D-154).</summary>
[TestClass]
public sealed class Round009Tests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    private static (Study Study, Region Spawn, Region Sink) OneLevel()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        var spawn = new Region { LevelId = level.Id, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 10)) };
        var sink = new Region { LevelId = level.Id, Name = "Out", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Sink, Outline = ShapeFactory.Rectangle(new(18, 0), new(20, 10)) };
        study.Regions.AddRange([spawn, sink]);
        return (study, spawn, sink);
    }

    private static EmissionRule RuleTo(Region sink, params MixLine[] mix) => new()
    {
        Mix = [.. mix],
        DestinationMode = DestinationMode.ChosenSinks,
        Destinations = [new SinkWeight { SinkId = sink.Id }],
    };

    /// <summary>The demand findings (they all name the rule).</summary>
    private static List<ValidationMessage> Findings(Study study) =>
        [.. StudyValidator.Validate(study).Where(m => m.Message.Contains("rule", StringComparison.Ordinal))];

    [TestMethod]
    public void JourneyStep_ForAMissingDeviceType_IsAnError()
    {
        var (study, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(RuleTo(sink, MixLine.Of(Commuter, TemplateIds.FromKey("journey.through-gates"))));
        List<ValidationMessage> found = Findings(study);
        Assert.IsTrue(found.Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("needs a 'Gate' device", StringComparison.Ordinal)),
            string.Join("\n", found.Select(m => m.Message)));
    }

    [TestMethod]
    public void JourneyStep_ForAMissingZoneTag_IsAnError_AndATaggedZoneFixesIt()
    {
        var (study, spawn, sink) = OneLevel();
        var journey = new JourneyTemplate { Key = "journey.t", Name = "Shop", Category = "Journeys", Steps = [JourneyStep.DwellIn("Retail", Distribution.Fixed(10))] };
        study.Library.Add(journey);
        spawn.EmissionRules.Add(RuleTo(sink, MixLine.Of(Commuter, journey.Id)));
        Assert.IsTrue(Findings(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("zone tagged 'Retail'", StringComparison.Ordinal)));

        study.Regions.Add(new Region { LevelId = study.Levels[0].Id, Name = "Shop", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Dwell, Tag = "retail", Outline = ShapeFactory.Rectangle(new(8, 0), new(12, 4)) });
        List<ValidationMessage> found = Findings(study);
        Assert.AreEqual(0, found.Count, "Tags match case-insensitively. " + string.Join("\n", found.Select(m => m.Message)));
    }

    [TestMethod]
    public void Mix_WithNoPositiveShare_UnknownJourney_AreWarnings()
    {
        var (study, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(RuleTo(sink, MixLine.Of(Commuter, null, 0)));
        spawn.EmissionRules.Add(RuleTo(sink, MixLine.Of(Commuter, Guid.NewGuid())));
        List<ValidationMessage> found = Findings(study);
        Assert.IsTrue(found.All(m => m.Severity == ValidationSeverity.Warning), string.Join("\n", found.Select(m => m.Message)));
        Assert.IsTrue(found.Any(m => m.Message.Contains("no mix line with a share above zero", StringComparison.Ordinal)));
        Assert.IsTrue(found.Any(m => m.Message.Contains("journey that isn't in the library", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void UnknownTags_AreFine()
    {
        var (study, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(RuleTo(sink, MixLine.Of(Commuter, TemplateIds.FromKey("journey.straight-exit"), 1, SpawnTag.Of("whatever", 30))));
        Assert.AreEqual(0, Findings(study).Count);
    }

    [TestMethod]
    public void CopyWithNewId_CopiesTheMixDeeply()
    {
        EmissionRule rule = EmissionRule.ForProfile(Commuter) with { Mix = [MixLine.Of(Commuter, null, 2, SpawnTag.Of("ticket", 50))] };
        EmissionRule copy = rule.CopyWithNewId();
        Assert.AreNotEqual(rule.Id, copy.Id);
        Assert.AreNotSame(rule.Mix, copy.Mix);
        Assert.AreNotSame(rule.Mix[0].SpawnTags, copy.Mix[0].SpawnTags);
        Assert.AreEqual(rule.Mix[0].ProfileId, copy.Mix[0].ProfileId);
        CollectionAssert.AreEqual(rule.Mix[0].SpawnTags, copy.Mix[0].SpawnTags);
    }

    [TestMethod]
    public void BuiltInJourneys_AskForTypesAndTags()
    {
        TemplateLibrary lib = BuiltInLibrary.Build();
        JourneyTemplate ticket = lib.Journeys.Single(j => j.Key == "journey.ticket-gates");
        Assert.AreEqual(JourneyStepKind.GoToDevice, ticket.Steps[0].Kind);
        Assert.AreEqual("TicketMachine", ticket.Steps[0].TargetTag);
        Assert.AreEqual("ticket", ticket.Steps[0].LacksTag);
        Assert.AreEqual(JourneyStepKind.Exit, ticket.Steps[^1].Kind);
        Assert.IsTrue(ticket.Summarise().Contains("TicketMachine", StringComparison.Ordinal));
    }
}
