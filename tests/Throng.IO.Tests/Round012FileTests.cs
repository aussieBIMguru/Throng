using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 012: the Long wait setting is additive within format v8 (D-231).</summary>
[TestClass]
public sealed class Round012FileTests
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
    public void LongWait_RoundTrips_AndDefaultsTo120()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.AreEqual(120, demo.Settings.LongWaitS, 1e-9, "Default 120 s (D-231).");
        demo.Settings.LongWaitS = 300;
        string path = Path.Combine(_folder, "wait.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.AreEqual(300, ThrongFile.Load(path).Value!.Settings.LongWaitS, 1e-9);
    }

    [TestMethod]
    public void LongWait_MissingFromAnOlderFile_ReadsAs120()
    {
        Study demo = DemoStudyBuilder.Build();
        demo.Settings.LongWaitS = 300;
        string path = Path.Combine(_folder, "older.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        RemoveLongWait(path);

        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(120, loaded.Settings.LongWaitS, 1e-9, "A v8 file from before round 012 has no LongWaitS.");
    }

    /// <summary>Strips the LongWaitS property from every JSON entry, to stand in for a file saved before round 012.</summary>
    private static void RemoveLongWait(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        bool removed = false;
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            // LongWaitS is the last settings property, so the comma before it goes too.
            string changed = Regex.Replace(json, ",\\s*\"longWaitS\"\\s*:\\s*[-0-9.eE+]+", string.Empty, RegexOptions.IgnoreCase);
            if (changed == json)
            {
                continue;
            }

            removed = true;
            string name = entry.FullName;
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(changed);
        }

        Assert.IsTrue(removed, "The study JSON has a LongWaitS.");
    }
}
