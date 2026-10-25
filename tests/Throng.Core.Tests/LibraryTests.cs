using Throng.Core.Library;

namespace Throng.Core.Tests;

[TestClass]
public sealed class LibraryTests
{
    [TestMethod]
    public void BuiltIns_HaveExpectedCounts()
    {
        TemplateLibrary lib = BuiltInLibrary.Build();
        Assert.AreEqual(8, lib.AgentProfiles.Count, "Agent profiles");
        Assert.AreEqual(12, lib.Devices.Count, "Devices");
        Assert.AreEqual(9, lib.Connectors.Count, "Connectors");
        Assert.AreEqual(4, lib.Lifts.Count, "Lifts");
        Assert.AreEqual(13, lib.Schedules.Count, "Schedules");
        Assert.AreEqual(46, lib.Count);
    }

    [TestMethod]
    public void BuiltIns_HaveUniqueKeysAndDeterministicIds()
    {
        TemplateLibrary lib = BuiltInLibrary.Build();
        List<TemplateBase> all = [.. lib.All()];

        Assert.AreEqual(all.Count, all.Select(t => t.Key).Distinct().Count(), "Keys must be unique.");
        Assert.AreEqual(all.Count, all.Select(t => t.Id).Distinct().Count(), "Ids must be unique.");
        foreach (TemplateBase t in all)
        {
            Assert.AreEqual(TemplateIds.FromKey(t.Key), t.Id, t.Key);
            Assert.AreEqual(TemplateSource.BuiltIn, t.Source, t.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Name), t.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Category), t.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Summarise()), t.Key);
            Assert.IsTrue(t.ColorHex.StartsWith('#'), t.Key);
        }
    }

    [TestMethod]
    public void BuiltIns_AreStableAcrossBuilds()
    {
        Assert.AreEqual(BuiltInLibrary.Build().AgentProfiles[0].Id, BuiltInLibrary.Build().AgentProfiles[0].Id);
    }

    [TestMethod]
    public void TemplateIds_AreDeterministicAndDistinct()
    {
        Assert.AreEqual(TemplateIds.FromKey("agent.commuter"), TemplateIds.FromKey("agent.commuter"));
        Assert.AreNotEqual(TemplateIds.FromKey("agent.commuter"), TemplateIds.FromKey("agent.staff"));
    }

    [TestMethod]
    public void AddCopy_IsDeepAndMarkedWithSource()
    {
        TemplateLibrary builtIns = BuiltInLibrary.Build();
        AgentProfileTemplate original = builtIns.AgentProfiles[0];
        var study = new TemplateLibrary();

        var copy = (AgentProfileTemplate)study.AddCopy(original, TemplateSource.Embedded);
        copy.Speed.Mean = 99;
        copy.Name = "Changed";

        Assert.AreEqual(TemplateSource.Embedded, copy.Source);
        Assert.AreEqual(original.Id, copy.Id, "Copies keep the source id so they can be reset/updated later.");
        Assert.AreNotEqual(99, original.Speed.Mean, "Nested objects must not be shared.");
        Assert.AreNotEqual("Changed", original.Name);
    }

    [TestMethod]
    public void AddCopy_DoesNotDuplicate()
    {
        TemplateLibrary builtIns = BuiltInLibrary.Build();
        var study = new TemplateLibrary();
        study.AddCopy(builtIns.Schedules[0], TemplateSource.Embedded);
        study.AddCopy(builtIns.Schedules[0], TemplateSource.Embedded);
        Assert.AreEqual(1, study.Count);
    }

    [TestMethod]
    public void Distribution_Describe_IsCultureInvariant()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.AreEqual("1.5–2.5", Distribution.Uniform(1.5, 2.5).Describe());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
