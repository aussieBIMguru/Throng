using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 017: .throngdevice files (D-290) and overrides saved with the study (D-287).</summary>
[TestClass]
public sealed class Round017FileTests
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
    public void DeviceFile_RoundTrips_AndRefusesNewerFormats()
    {
        var device = StudyDefinitions.CreateNewDevice(new Study());
        device.Name = "Turnstile";
        device.TypeTag = "Turnstile";
        device.ServiceTime = Distribution.Triangular(2, 3, 6);
        device.PassThrough = true;
        string path = Path.Combine(_folder, "turnstile" + DeviceFile.Extension);

        Assert.IsTrue(DeviceFile.Save(DeviceReconciler.BuildBundle(device), path).IsSuccess);
        var loaded = DeviceFile.Load(path);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        DeviceTemplate back = loaded.Value!.Device;
        Assert.AreEqual(device.Id, back.Id);
        Assert.AreEqual("Turnstile", back.TypeTag);
        Assert.AreEqual(DistributionKind.Triangular, back.ServiceTime.Kind);
        Assert.AreEqual(3, back.ServiceTime.Mode, 1e-9);
        Assert.IsTrue(back.PassThrough);

        File.WriteAllText(path, File.ReadAllText(path).Replace("\"formatVersion\": 1", "\"formatVersion\": 99", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(DeviceFile.Load(path).IsSuccess, "A newer format is refused with a message.");
        File.WriteAllText(path, "{ not json");
        Assert.IsFalse(DeviceFile.Load(path).IsSuccess);
    }

    [TestMethod]
    public void BuiltInOverride_SurvivesSaveAndReopen()
    {
        Study demo = DemoStudyBuilder.Build();
        Device machine = demo.Devices.First(d => d.TypeTag == "TicketMachine");
        DeviceTemplate copy = StudyTemplates.FindDeviceTemplate(demo, machine)!;
        copy.ServiceTime = Distribution.Fixed(12);
        string path = Path.Combine(_folder, "override.throng");

        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        var reopened = ThrongFile.Load(path);
        Assert.IsTrue(reopened.IsSuccess, reopened.Error);
        Study back = reopened.Value!;
        DeviceTemplate again = StudyTemplates.FindDeviceTemplate(back, back.Devices.First(d => d.Id == machine.Id))!;
        Assert.AreEqual(12, again.ServiceTime.Value, 1e-9);
        Assert.IsTrue(StudyDefinitions.IsModified(back, again), "Still shown as modified after reopening.");
    }
}
