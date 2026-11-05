using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 020: your library on disk (D-025, D-348) and .thronglib exchange (D-347). Never the real %AppData%.</summary>
[TestClass]
public sealed class Round020FileTests
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
    public void Store_RoundTrips_AndKeepsTheLastFileAsBackup()
    {
        string folder = Path.Combine(_folder, "Library");
        var store = new UserLibraryStore(folder);
        Assert.IsTrue(store.Load().IsSuccess, "No file yet: an empty library.");
        Assert.AreEqual(0, store.Library.Count);

        Assert.IsTrue(store.Update(l => MyLibrary.Put(l, Profile("Runner"))).IsSuccess);
        Assert.IsTrue(File.Exists(store.FilePath));
        Assert.IsFalse(File.Exists(store.BackupPath), "Nothing to back up the first time.");
        string first = File.ReadAllText(store.FilePath);

        Assert.IsTrue(store.Update(l => MyLibrary.Put(l, Profile("Walker"))).IsSuccess);
        Assert.AreEqual(first, File.ReadAllText(store.BackupPath), "The previous library.json is kept as library.bak.json (D-348).");

        var reopened = new UserLibraryStore(folder);
        Assert.IsTrue(reopened.Load().IsSuccess);
        CollectionAssert.AreEqual(new[] { "Runner", "Walker" }, reopened.Library.AgentProfiles.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void Store_UnreadableFile_StartsEmpty_AndKeepsTheFileAside()
    {
        Directory.CreateDirectory(_folder);
        var store = new UserLibraryStore(_folder);
        File.WriteAllText(store.FilePath, "{ not json");

        Result loaded = store.Load();
        Assert.IsFalse(loaded.IsSuccess, "Reported, so the app logs it.");
        Assert.AreEqual(0, store.Library.Count);
        Assert.IsFalse(store.IsReadOnly);
        Assert.IsFalse(File.Exists(store.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(_folder, "library.unreadable-*.json").Length, "The bad file is kept.");

        Assert.IsTrue(store.Update(l => MyLibrary.Put(l, Profile("Runner"))).IsSuccess, "A fresh library can be saved.");
    }

    [TestMethod]
    public void Store_NewerFormat_IsLeftAlone_AndReadOnly()
    {
        var store = new UserLibraryStore(_folder);
        File.WriteAllText(store.FilePath, "{ \"formatVersion\": 99, \"agentProfiles\": [] }");
        string before = File.ReadAllText(store.FilePath);

        Assert.IsFalse(store.Load().IsSuccess);
        Assert.IsTrue(store.IsReadOnly);
        Assert.IsFalse(store.Update(l => MyLibrary.Put(l, Profile("Runner"))).IsSuccess, "Never saved over.");
        Assert.AreEqual(0, store.Library.Count, "A failed update leaves the library as it was.");
        Assert.AreEqual(before, File.ReadAllText(store.FilePath));
    }

    [TestMethod]
    public void LibraryFile_RoundTrips_AndImportsWithReconcileChoices()
    {
        var mine = new TemplateLibrary();
        AgentProfileTemplate runner = Profile("Runner");
        MyLibrary.Put(mine, runner);
        string path = Path.Combine(_folder, "shared" + LibraryFile.Extension);
        Assert.IsTrue(LibraryFile.Save(MyLibrary.BuildExport(mine, [runner.Id]), path).IsSuccess);

        Result<TemplateLibrary> loaded = LibraryFile.Load(path);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        TemplateLibrary incoming = loaded.Value!;
        Assert.AreEqual(runner.Id, incoming.AgentProfiles.Single().Id, "Ids survive (D-346, D-351).");

        // A colleague's library already has a different Runner under the same id.
        var theirs = new TemplateLibrary();
        AgentProfileTemplate other = Profile("Runner");
        other.Id = runner.Id;
        other.Speed = Distribution.Fixed(0.9);
        MyLibrary.Put(theirs, other);
        List<ReconcileItem> clashes = MyLibrary.FindImportClashes(theirs, incoming);
        Assert.AreEqual(1, clashes.Count);
        LibraryImportResult result = MyLibrary.ApplyImport(theirs, incoming, [ReconcileChoice.Keep]);
        Assert.AreEqual(1, result.Added, "Keep both.");
        Assert.AreEqual(2, theirs.AgentProfiles.Count);
        Assert.AreEqual(1, theirs.AgentProfiles.Count(p => p.Id == runner.Id), "Theirs keeps its id; the copy gets a new one.");
    }

    [TestMethod]
    public void LibraryFile_RefusesStudiesAndJunk()
    {
        string study = Path.Combine(_folder, "study" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(new Study(), study).IsSuccess);
        Assert.IsFalse(LibraryFile.Load(study).IsSuccess, "A study isn't a library.");

        string junk = Path.Combine(_folder, "junk" + LibraryFile.Extension);
        File.WriteAllText(junk, "not a zip");
        Assert.IsFalse(LibraryFile.Load(junk).IsSuccess);
        Assert.IsFalse(LibraryFile.Load(Path.Combine(_folder, "missing" + LibraryFile.Extension)).IsSuccess);
    }

    private static AgentProfileTemplate Profile(string name)
    {
        Guid id = Guid.NewGuid();
        return new AgentProfileTemplate { Id = id, Key = $"agent.mine.{id:N}", Name = name, Speed = Distribution.Fixed(1.5) };
    }
}
