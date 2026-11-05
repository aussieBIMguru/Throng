using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>
/// Round 011a: the colour-blind heatmap setting is additive within format v7 (D-206, D-208). Round 011b: format v8
/// carries the accessible flags (D-213, D-223).
/// </summary>
[TestClass]
public sealed class Round011FileTests
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
    public void ColourBlindHeatmap_RoundTrips_AndDefaultsOff()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.IsFalse(demo.Settings.ColourBlindHeatmap, "Classic colours by default (D-206).");
        demo.Settings.ColourBlindHeatmap = true;
        string path = Path.Combine(_folder, "heat.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        Assert.IsTrue(loaded.Settings.ColourBlindHeatmap);
    }

    [TestMethod]
    public void AccessibleFlags_RoundTrip_InFormatV8()
    {
        Study demo = DemoStudyBuilder.Build();
        Device gate = demo.Devices.First(d => d.PresetKey == "device.gate-line");
        gate.AccessibleOverride = true;
        string path = Path.Combine(_folder, "access.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        Assert.IsTrue(loaded.Devices.Single(d => d.Id == gate.Id).AccessibleOverride);
        Assert.IsTrue(loaded.Devices.Where(d => d.Id != gate.Id).All(d => d.AccessibleOverride is null));
        Assert.IsTrue(StudyTemplates.IsAccessible(loaded, loaded.Devices.Single(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey)));
        Assert.IsTrue(((AgentProfileTemplate)loaded.Library.FindByKey(BuiltInLibrary.MobilityProfileKey)!).NeedsAccessibleDevices);
    }

    [TestMethod]
    public void Load_FormatV7_UpgradesTheBuiltInAccessibleGateAndWheelchairProfile()
    {
        Study demo = DemoStudyBuilder.Build();
        ((DeviceTemplate)demo.Library.FindByKey(BuiltInLibrary.AccessibleGateKey)!).Accessible = false;
        ((AgentProfileTemplate)demo.Library.FindByKey(BuiltInLibrary.MobilityProfileKey)!).NeedsAccessibleDevices = false;
        string path = Path.Combine(_folder, "v7.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        SetFormatVersion(path, 7);
        Assert.AreEqual(7, ThrongFile.ReadManifest(path).Value!.FormatVersion);

        Study loaded = ThrongFile.Load(path).Value!;
        Assert.IsTrue(((DeviceTemplate)loaded.Library.FindByKey(BuiltInLibrary.AccessibleGateKey)!).Accessible, "v7 → v8 (D-213).");
        Assert.IsTrue(((AgentProfileTemplate)loaded.Library.FindByKey(BuiltInLibrary.MobilityProfileKey)!).NeedsAccessibleDevices);
        Assert.IsFalse(((DeviceTemplate)loaded.Library.FindByKey("device.gate-line") ?? new DeviceTemplate()).Accessible, "Other presets stay as they were.");
    }

    /// <summary>Rewrites the manifest's format version, to stand in for a file saved by an older build.</summary>
    private static void SetFormatVersion(string path, int version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry manifest = zip.GetEntry("manifest.json")!;
        string json;
        using (var reader = new StreamReader(manifest.Open(), Encoding.UTF8))
        {
            json = reader.ReadToEnd();
        }

        string changed = Regex.Replace(json, "\"formatVersion\"\\s*:\\s*\\d+", "\"formatVersion\": " + version);
        Assert.AreNotEqual(json, changed, "The manifest has a formatVersion.");
        manifest.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
        writer.Write(changed);
    }
}
