using Throng.Analysis;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 015: the RESULTS ▸ LIFTS table and lifts.csv (D-260).</summary>
[TestClass]
public sealed class Round015Tests
{
    [TestMethod]
    public void LiftResults_CountBoardingsWaitsAndRides()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 10);
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 600);

        RunResults results = engine.CopyResults();
        Assert.AreEqual(1, results.Lifts.Count, "One row per bank.");
        LiftResult lift = results.Lifts[0];
        Assert.AreEqual(study.Shafts[0].Lift!.Name, lift.Name);
        Assert.AreEqual(1, lift.Cars);
        Assert.IsTrue(lift.Boarded >= 20, $"{lift.Boarded} boarded.");
        Assert.IsTrue(lift.Rides > 0 && lift.Rides <= lift.Boarded, $"{lift.Rides} rides of {lift.Boarded} boarded.");
        Assert.IsTrue(lift.MeanWaitS >= 0 && lift.MaxWaitS >= lift.P95WaitS && lift.MaxWaitS >= lift.MeanWaitS, "Waits are ordered.");
        Assert.IsTrue(lift.MeanRideS > 2 && lift.MeanRideS < 60, $"A 5 m ride takes a few seconds door to door ({lift.MeanRideS:0.0} s).");
        Assert.AreEqual(engine.LiftGaveUp(0), lift.GaveUp, "No warm-up: every give-up counts.");
    }

    [TestMethod]
    public void LiftResults_LeaveTheWarmUpOut()
    {
        Study study = Round013Tests.LiftStudy(SimTestStudies.Commuter, 10);
        study.Settings.WarmUpS = 10_000;
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 300);

        LiftResult lift = engine.CopyResults().Lifts[0];
        Assert.AreEqual(0, lift.Boarded, "Still in the warm-up (D-218).");
        Assert.AreEqual(0, lift.Rides);
        Assert.IsTrue(engine.Stats.Arrived > 0, "People still rode the lift.");
    }

    [TestMethod]
    public void Csv_LiftsHasARowPerBank()
    {
        var results = new RunResults
        {
            Devices = [],
            DeviceNames = [],
            Times = [],
            InModel = [],
            ArrivalsPerMin = [],
            ExitsPerMin = [],
            PeakDensity = [],
            Queues = [],
            Lifts = [new LiftResult(0, "Core, east", 4, 120, 31.25, 64, 88.4, 118, 22.04, 3)],
        };

        string[] rows = ResultsCsv.LiftsCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, rows.Length);
        Assert.AreEqual("Lift,Cars,Boarded,MeanWaitS,P95WaitS,MaxWaitS,Rides,MeanRideS,GaveUp", rows[0]);
        Assert.AreEqual("\"Core, east\",4,120,31.3,64.0,88.4,118,22.0,3", rows[1]);
        Assert.AreEqual(0, RunResults.Empty.Lifts.Count, "An empty result has no lift rows.");
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
        }
    }
}
