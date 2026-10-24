namespace Throng.Sim.Tests;

[TestClass]
public sealed class SimClockTests
{
    [TestMethod]
    public void Format_ShowsTwelveHourTime()
    {
        var clock = new SimClock(new TimeSpan(7, 30, 0));
        Assert.AreEqual("07:30:00 AM", clock.Format());
        Assert.AreEqual("01:15:00 PM", SimClock.FormatTimeOfDay(new TimeSpan(13, 15, 0)));
    }

    [TestMethod]
    public void Advance_UsesIntegerTicksWithoutDrift()
    {
        var clock = new SimClock(new TimeSpan(7, 30, 0), timeStepS: 0.1);
        for (int i = 0; i < 36000; i++)
        {
            clock.Advance();
        }

        Assert.AreEqual(3600.0, clock.ElapsedSeconds, 1e-9);
        Assert.AreEqual("08:30:00 AM", clock.Format());
    }

    [TestMethod]
    public void PastMidnight_ShowsDayCounter()
    {
        var clock = new SimClock(new TimeSpan(23, 30, 0), timeStepS: 1.0);
        clock.Advance(3600);
        Assert.AreEqual(1, clock.DayIndex);
        Assert.AreEqual("Day 2 · 12:30:00 AM", clock.Format());
    }

    [TestMethod]
    public void Reset_ReturnsToStart()
    {
        var clock = new SimClock(new TimeSpan(7, 30, 0));
        clock.Advance(100);
        clock.Reset();
        Assert.AreEqual(0, clock.Tick);
        Assert.AreEqual(new TimeSpan(7, 30, 0), clock.TimeOfDay);
    }
}
