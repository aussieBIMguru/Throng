using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 025: a service's kind saves and loads (additive in format v12, D-398); the stadium demo saves.</summary>
[TestClass]
public sealed class Round025FileTests
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
    public void ServiceKind_RoundTrips_AndDemoServicesStayTrains()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.IsTrue(demo.TrainServices.Count > 0);
        Assert.IsTrue(demo.TrainServices.All(s => s.Kind == ServiceKind.Train));
        demo.TrainServices[0] = demo.TrainServices[0].WithKind(ServiceKind.Plane);

        string path = Path.Combine(_folder, "kinds.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(ServiceKind.Plane, loaded.TrainServices[0].Kind);
        Assert.AreEqual(ServiceKind.Train, loaded.TrainServices[1].Kind);
    }

    [TestMethod]
    public void StadiumDemo_SavesAndReopens()
    {
        Study stadium = StadiumBuilder.BuildSector();
        string path = Path.Combine(_folder, "stadium.throng");
        Assert.IsTrue(ThrongFile.Save(stadium, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(stadium.Regions.Count, loaded.Regions.Count);
        Assert.AreEqual(stadium.Connectors.Count, loaded.Connectors.Count);
        Assert.IsTrue(loaded.Library.Journeys.Any(j => j.Key == StadiumBuilder.IngressJourneyKey));
    }
}
