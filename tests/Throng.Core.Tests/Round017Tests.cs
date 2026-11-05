using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Tests;

/// <summary>
/// Round 017: definitions (D-286–D-288: built-in overrides, duplicates, new), distribution edits (D-289) and device
/// definition exchange (D-290).
/// </summary>
[TestClass]
public sealed class Round017Tests
{
    private static DeviceTemplate BuiltInTicketMachine => (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.ticket-machine")!;

    [TestMethod]
    public void Override_IsAStudyCopyUnderTheBuiltInsId_ModifiedUntilReset()
    {
        var study = new Study();
        DeviceTemplate builtIn = BuiltInTicketMachine;
        DeviceTemplate copy = StudyDefinitions.EnsureStudyCopy(study, builtIn);

        Assert.AreNotSame(builtIn, copy);
        Assert.AreEqual(builtIn.Id, copy.Id, "The override keeps the built-in's id (D-287).");
        Assert.AreSame(copy, StudyDefinitions.EnsureStudyCopy(study, builtIn), "One copy per study.");
        Assert.IsFalse(StudyDefinitions.IsModified(study, copy), "A pristine embedded copy isn't modified.");
        Assert.IsTrue(StudyDefinitions.IsBuiltIn(copy));

        copy.ServiceTime = Distribution.Fixed(12);
        Assert.IsTrue(StudyDefinitions.IsModified(study, copy));
        var device = new Device { TemplateId = builtIn.Id, PresetKey = builtIn.Key, TypeTag = builtIn.TypeTag };
        study.Devices.Add(device);
        Assert.AreSame(copy, StudyTemplates.FindDeviceTemplate(study, device), "Placed devices follow the study's copy.");
        Assert.AreEqual(1, StudyDefinitions.DevicesUsing(study, copy).Count);

        StudyDefinitions.CopyValues(copy, builtIn);
        Assert.IsFalse(StudyDefinitions.IsModified(study, copy), "RESET puts the built-in's values back.");
        Assert.AreEqual(builtIn.Id, copy.Id, "Copying values keeps the id.");
        Assert.AreEqual(TemplateSource.Embedded, copy.Source, "Copying values keeps the source.");
        Assert.AreNotSame(builtIn.ServiceTime, copy.ServiceTime, "Values are deep copies.");
        Assert.AreEqual(35, BuiltInTicketMachine.ServiceTime.Mode, 1e-9, "The built-in itself is untouched.");
    }

    [TestMethod]
    public void Duplicate_AndNew_GetTheirOwnIdsKeysAndNames()
    {
        var study = new Study();
        DeviceTemplate first = StudyDefinitions.CreateDeviceCopy(study, BuiltInTicketMachine);
        study.Library.Devices.Add(first);
        DeviceTemplate second = StudyDefinitions.CreateDeviceCopy(study, BuiltInTicketMachine);

        Assert.AreNotEqual(BuiltInTicketMachine.Id, first.Id);
        Assert.AreEqual("Ticket machine (copy)", first.Name);
        Assert.AreEqual("Ticket machine (copy 2)", second.Name);
        Assert.AreEqual(BuiltInTicketMachine.TypeTag, first.TypeTag, "A duplicate keeps the type, so journeys use it too.");
        Assert.AreEqual(TemplateSource.User, first.Source);
        StringAssert.StartsWith(first.Key, "device.user.");
        Assert.IsFalse(StudyDefinitions.IsBuiltIn(first));

        DeviceTemplate made = StudyDefinitions.CreateNewDevice(study);
        study.Library.Devices.Add(made);
        DeviceTemplate another = StudyDefinitions.CreateNewDevice(study);
        Assert.AreEqual("Custom", made.TypeTag);
        Assert.AreEqual("Custom2", another.TypeTag, "A new device gets a type nothing else has.");
        Assert.AreEqual("New device 2", another.Name);

        AgentProfileTemplate profile = StudyDefinitions.CreateNewProfile(study);
        StringAssert.StartsWith(profile.Key, "agent.user.");
        Assert.IsTrue(StudyDefinitions.AvailableDevices(study).Take(3).Contains(first), "The study's own definitions list first.");
    }

    [TestMethod]
    public void ProfileUseCount_CountsMixLines()
    {
        var study = new Study();
        Guid profile = Guid.NewGuid();
        study.Regions.Add(new Region
        {
            Kind = RegionKind.Zone,
            ZoneKind = ZoneKind.Spawn,
            EmissionRules =
            [
                new EmissionRule { Mix = [new MixLine { ProfileId = profile }, new MixLine { ProfileId = Guid.NewGuid() }] },
                new EmissionRule { Mix = [new MixLine { ProfileId = profile }] },
            ],
        });
        Assert.AreEqual(2, StudyDefinitions.ProfileUseCount(study, profile));
    }

    [TestMethod]
    public void Distribution_EditsStayConsistent()
    {
        Distribution swapped = DistributionShape.Normalised(new Distribution { Kind = DistributionKind.Uniform, Min = 9, Max = 3 });
        Assert.AreEqual(3, swapped.Min, 1e-9);
        Assert.AreEqual(9, swapped.Max, 1e-9);
        Assert.AreEqual(6, swapped.ExpectedValue, 1e-9, "The mean the capacity check reads follows the bounds.");

        Distribution triangle = DistributionShape.Normalised(new Distribution { Kind = DistributionKind.Triangular, Min = 10, Mode = 50, Max = 40 });
        Assert.AreEqual(40, triangle.Mode, 1e-9, "The mode stays inside the bounds.");
        Assert.AreEqual(30, triangle.ExpectedValue, 1e-9);
        Assert.AreEqual(1, DistributionShape.Density(triangle, 40), 1e-9);
        Assert.AreEqual(0, DistributionShape.Density(triangle, 41), 1e-9);

        Distribution fixedValue = DistributionShape.Converted(Distribution.Uniform(20, 40), DistributionKind.Fixed);
        Assert.AreEqual(30, fixedValue.ExpectedValue, 1e-9, "A new type keeps the middle.");
        Distribution normal = DistributionShape.Converted(Distribution.Fixed(10), DistributionKind.NormalClamped);
        Assert.AreEqual(10, normal.Mean, 1e-9);
        Assert.IsTrue(normal.Min < 10 && normal.Max > 10 && normal.StdDev > 0);
        Assert.AreEqual(0, DistributionShape.Normalised(Distribution.Fixed(-4)).Value, 1e-9, "Times can't go negative.");

        (double lo, double hi) = DistributionShape.Range(Distribution.Exponential(5));
        Assert.AreEqual(0, lo, 1e-9);
        Assert.AreEqual(20, hi, 1e-9);
    }

    [TestMethod]
    public void DeviceImport_AsksAboutAnUnknownType_AndMapsOrRehomes()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Devices.Add(new Device { LevelId = level.Id, TypeTag = "Gate" });

        DeviceTemplate turnstile = StudyDefinitions.CreateNewDevice(study);
        turnstile.TypeTag = "Turnstile";
        turnstile.Name = "Turnstile";
        DeviceBundle bundle = DeviceReconciler.BuildBundle(turnstile);
        Assert.AreNotSame(turnstile, bundle.Device, "The bundle holds a copy.");

        List<ReconcileItem> items = DeviceReconciler.Find(study, bundle);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(ReconcileKind.ImportedDeviceType, items[0].Kind);
        CollectionAssert.Contains(items[0].MapOptions.ToList(), "Gate");

        DeviceTemplate mapped = DeviceReconciler.Apply(study, bundle, items, [new ReconcileChoice(ReconcileAction.Map, "Gate")]);
        Assert.AreEqual("Gate", mapped.TypeTag);
        Assert.AreEqual(TemplateSource.Imported, mapped.Source);
        Assert.AreEqual(turnstile.Id, mapped.Id, "A free id is kept.");

        DeviceTemplate kept = DeviceReconciler.Apply(study, bundle, items, [new ReconcileChoice(ReconcileAction.Drop)]);
        Assert.AreEqual("Turnstile", kept.TypeTag, "Keep and drop both keep the device's own type.");

        study.Library.Devices.Add(mapped);
        DeviceTemplate again = DeviceReconciler.Apply(study, bundle, [], []);
        Assert.AreNotEqual(mapped.Id, again.Id, "A taken id gets a new one.");
        StringAssert.EndsWith(again.Name, "(imported)");

        bundle.Device.TypeTag = "Gate";
        Assert.AreEqual(0, DeviceReconciler.Find(study, bundle).Count, "A known type needs no answer.");
    }
}
