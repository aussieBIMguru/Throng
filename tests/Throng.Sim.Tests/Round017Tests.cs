using Throng.Analysis.Checks;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;

namespace Throng.Sim.Tests;

/// <summary>Round 017: placed devices follow their definition into the run (D-288); capacity notes trimmed per level (D-296).</summary>
[TestClass]
public sealed class Round017Tests
{
    [TestMethod]
    public void EditedDefinition_ReachesTheRun()
    {
        Study demo = DemoStudyBuilder.Build();
        Device machine = demo.Devices.First(d => d.TypeTag == "TicketMachine");
        DeviceTemplate copy = StudyTemplates.FindDeviceTemplate(demo, machine)!;
        Assert.IsTrue(StudyDefinitions.IsBuiltIn(copy), "The demo places built-ins (embedded copies).");

        copy.ServiceTime = Distribution.Fixed(42);
        copy.ChoiceWeight = 7;
        SimModel model = SimTestStudies.Compile(demo);
        Assert.IsTrue(model.Devices.Any(d => d.TypeTag == "TicketMachine"), "The demo's journeys use its ticket machines.");
        foreach (SimDevice device in model.Devices.Where(d => d.TypeTag == "TicketMachine"))
        {
            Assert.AreEqual(42, device.MeanServiceS, 1e-9, "Service time comes from the definition.");
            Assert.AreEqual(-7, device.ChoicePenaltyS, 1e-9, "So does the choice weight.");
        }

        Assert.IsTrue(StudyDefinitions.IsModified(demo, copy));
    }

    [TestMethod]
    public void CapacityNotes_AreTrimmedToTheBusiestFivePerLevel()
    {
        Study demo = DemoStudyBuilder.Build();
        IReadOnlyList<ValidationMessage> findings = StudyCheck.Run(demo);
        var levelOf = new Dictionary<Guid, Guid>();
        foreach (Device d in demo.Devices)
        {
            levelOf[d.Id] = d.LevelId;
        }

        foreach (Connector c in demo.Connectors)
        {
            levelOf[c.Id] = c.BottomLevelId;
        }

        foreach (Shaft s in demo.Shafts)
        {
            levelOf[s.Id] = s.BaseLevelId;
        }

        IEnumerable<IGrouping<Guid, ValidationMessage>> perLevel = findings
            .Where(f => f.Rule == ValidationRules.Capacity && f.Severity == ValidationSeverity.Info)
            .GroupBy(f => f.LevelId ?? (f.ObjectId is { } id && levelOf.TryGetValue(id, out Guid level) ? level : Guid.Empty));
        foreach (IGrouping<Guid, ValidationMessage> level in perLevel)
        {
            Assert.IsTrue(level.Count() <= 5, $"Level {level.Key}: {level.Count()} capacity notes.");
        }
    }
}
