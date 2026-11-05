using Throng.Core.Common;
using Throng.Core.Library;

namespace Throng.IO.Tests;

/// <summary>Round 024b: groups in your library on disk and in .thronglib files (D-388). Never the real %AppData%.</summary>
[TestClass]
public sealed class Round024bFileTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "throng-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }

    [TestMethod]
    public void GroupsRoundTrip_InTheStoreAndInAThronglib()
    {
        Guid commuter = TemplateIds.FromKey("agent.commuter");
        var group = new GroupTemplate
        {
            Id = Guid.NewGuid(),
            Key = "group.mine.pair",
            Name = "Pair",
            Members = [GroupMember.Of(commuter, 2, 2)],
            DefaultJourneyId = null,
        };

        var store = new UserLibraryStore(_folder);
        Assert.IsTrue(store.Load().IsSuccess);
        Assert.IsTrue(store.Update(mine => MyLibrary.Put(mine, group)).IsSuccess);
        var reopened = new UserLibraryStore(_folder);
        Assert.IsTrue(reopened.Load().IsSuccess);
        GroupTemplate saved = reopened.Library.Groups.Single();
        Assert.AreEqual("Pair", saved.Name);
        Assert.AreEqual(GroupMember.Of(commuter, 2, 2), saved.Members.Single());

        string path = Path.Combine(_folder, "groups" + LibraryFile.Extension);
        Assert.IsTrue(LibraryFile.Save(MyLibrary.BuildExport(reopened.Library, [group.Id]), path).IsSuccess);
        Result<TemplateLibrary> loaded = LibraryFile.Load(path);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(1, loaded.Value!.Groups.Count, "The group travels in the .thronglib.");
        Assert.AreEqual(0, loaded.Value.AgentProfiles.Count, "A built-in member isn't copied (everyone has it).");
    }
}
