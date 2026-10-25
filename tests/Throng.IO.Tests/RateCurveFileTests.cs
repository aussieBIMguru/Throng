using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 007 part 2: file format v5 carries rate curves (D-129).</summary>
[TestClass]
public sealed class RateCurveFileTests
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
    public void V5_RoundTripsRateCurves()
    {
        Study demo = DemoStudyBuilder.Build();
        Region west = demo.Regions.Single(r => r.Name == "West entrance");
        west.EmissionRules[0] = west.EmissionRules[0] with { RateCurve = RateCurves.PointsOf(RateCurvePreset.EventSurge) };
        string path = Path.Combine(_folder, "v5" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);

        Study loaded = ThrongFile.Load(path).Value!;
        Region westLoaded = loaded.FindRegion(west.Id)!;
        CollectionAssert.AreEqual(west.EmissionRules[0].RateCurve, westLoaded.EmissionRules[0].RateCurve);
        Assert.AreEqual(0, westLoaded.EmissionRules[1].RateCurve.Count, "Rules without a curve stay flat.");
    }
}
