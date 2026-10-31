using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 020: a journey whose picks match nothing in the study uses its device type in the run (D-349).</summary>
[TestClass]
public sealed class Round020Tests
{
    [TestMethod]
    public void PicksNotInTheStudy_CompileToTheTypesDevices()
    {
        Study study = Round018bTests.Hall(out Device near, out Device far);
        Round018bTests.UseDeviceJourney(study, JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(Guid.NewGuid())] });
        SimEngine engine = Round009bTests.Run(study, 8);

        Assert.AreEqual(2, engine.Model.Devices.Count(d => d.DeviceId == near.Id || d.DeviceId == far.Id), "Both gates of the type are built.");
        (int toNear, int toFar) = Round018bTests.Heading(engine, near, far);
        Assert.IsTrue(toNear + toFar > 5, $"{toNear} + {toFar} people head for gates of the node's type, as if it had no picks.");
    }

    [TestMethod]
    public void PicksInTheStudy_StillRule()
    {
        Study study = Round018bTests.Hall(out Device near, out Device far);
        Round018bTests.UseDeviceJourney(study, JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(far.Id), JourneyPick.Of(Guid.NewGuid())] });
        (int toNear, _) = Round018bTests.Heading(Round009bTests.Run(study, 8), near, far);
        Assert.AreEqual(0, toNear, "One pick still here: only it is used (D-318 unchanged).");
    }
}
