using Throng.Core.Model;

namespace Throng.Core.Tests;

/// <summary>Round 025: services of any kind (D-398): kinds change words and starting values, never the engine.</summary>
[TestClass]
public sealed class Round025Tests
{
    [TestMethod]
    public void Create_TakesTheKindsStartingTimetable()
    {
        TrainService plane = TrainService.Create(ServiceKind.Plane, "Flight 12", [Guid.NewGuid()]);
        (double headway, double dwell, int alight, double over, int capacity) = ServiceKinds.Defaults(ServiceKind.Plane);
        Assert.AreEqual(ServiceKind.Plane, plane.Kind);
        Assert.AreEqual(headway, plane.HeadwayS);
        Assert.AreEqual(dwell, plane.DwellS);
        Assert.AreEqual(alight, plane.AlightPerTrain);
        Assert.AreEqual(over, plane.AlightOverS);
        Assert.AreEqual(capacity, plane.Capacity);
        Assert.AreEqual(ServiceKind.Train, new TrainService().Kind, "Older files (no kind) are trains.");
    }

    [TestMethod]
    public void WithKind_KeepsWhatYouChanged_AndSwapsTheRest()
    {
        TrainService train = TrainService.Create(ServiceKind.Train, "Service 1", []) with { Capacity = 420 };
        TrainService ride = train.WithKind(ServiceKind.Ride);
        (double headway, _, _, _, _) = ServiceKinds.Defaults(ServiceKind.Ride);
        Assert.AreEqual(ServiceKind.Ride, ride.Kind);
        Assert.AreEqual(headway, ride.HeadwayS, "Still at the train's starting value, so it takes the ride's.");
        Assert.AreEqual(420, ride.Capacity, "Changed by hand: kept.");
        Assert.AreEqual(train.Name, ride.Name);
        StringAssert.StartsWith(ride.Describe(), "Ride every");
    }

    [TestMethod]
    public void Words_FollowTheKind()
    {
        Assert.AreEqual("plane", ServiceKinds.Vehicle(ServiceKind.Plane));
        Assert.AreEqual("boarding gates", ServiceKinds.Doors(ServiceKind.Plane));
        Assert.AreEqual("RIDE", ServiceKinds.Badge(ServiceKind.Ride));
        Assert.AreEqual("SERVICE", ServiceKinds.Badge(ServiceKind.Generic));
        Assert.AreEqual(ServiceKinds.All.Count, Enum.GetValues<ServiceKind>().Length, "Every kind is in the menu.");
    }
}
