using Throng.Analysis;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 011c: run results (device stats after the warm-up, time series, D-218–D-222) and CSV export.</summary>
[TestClass]
public sealed class Round011cTests
{
    [TestMethod]
    public void DeviceStats_StartAfterTheWarmUp_AndAddUp()
    {
        Study study = Round009bTests.WideGates(SimTestStudies.Commuter, 30);
        study.Settings.WarmUpS = 60;
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 40);
        RunResults early = engine.CopyResults();
        Assert.IsTrue(early.Devices.All(d => d.Served == 0 && d.MaxQueue == 0), "Nothing counted during the warm-up (D-218).");
        Assert.AreEqual(0, early.MeasuredS, 1e-9);
        Assert.IsTrue(early.SampleCount >= 7, $"The series records from the start ({early.SampleCount} samples by 40 s).");

        Run(engine, 200);
        RunResults results = engine.CopyResults();
        Assert.AreEqual(180, results.MeasuredS, 1.0, "240 s run, 60 s warm-up.");
        long served = results.Devices.Sum(d => d.Served);
        Assert.IsTrue(served > 20, $"{served} served after the warm-up.");
        foreach (DeviceResult d in results.Devices)
        {
            Assert.IsTrue(d.Served <= engine.ServedAt(d.Device), "Only part of the whole run.");
            Assert.IsTrue(d.Utilisation is >= 0 and <= 1, $"{d.Name}: utilisation {d.Utilisation}.");
            if (d.Served > 0)
            {
                Assert.IsTrue(d.MeanWaitS <= d.MaxWaitS + 1e-6 && d.P95WaitS <= d.MaxWaitS + 1e-6, $"{d.Name}: mean {d.MeanWaitS}, 95% {d.P95WaitS}, max {d.MaxWaitS}.");
                Assert.IsTrue(d.Utilisation > 0, $"{d.Name} served people, so it was busy some of the time.");
            }
        }
    }

    [TestMethod]
    public void TimeSeries_EveryFiveSeconds_CountsMatchTheRun()
    {
        var engine = new SimEngine(SimTestStudies.Compile(SimTestStudies.Corridor(60)));
        Run(engine, 120);
        RunResults r = engine.CopyResults();
        Assert.AreEqual(25, r.SampleCount, 1, "One sample every 5 s from the first step.");
        for (int s = 1; s < r.SampleCount; s++)
        {
            Assert.AreEqual(RunResults.SeriesIntervalS, r.Times[s] - r.Times[s - 1], 0.11);
        }

        Assert.AreEqual(engine.Agents.Count, r.InModel[^1], 6, "People in the model at the last sample.");
        double emitted = 0;
        for (int s = 0; s < r.SampleCount; s++)
        {
            double interval = s == 0 ? r.Times[0] : r.Times[s] - r.Times[s - 1];
            emitted += r.ArrivalsPerMin[s] * interval / 60;
        }

        Assert.AreEqual(engine.Stats.Spawned, emitted, 6.0, "Arrival rates add back up to the people emitted.");
        Assert.IsTrue(r.PeakDensity.Max() > 0);

        engine.Reset();
        RunResults cleared = engine.CopyResults();
        Assert.AreEqual(0, cleared.SampleCount, "Reset clears the results.");
        Assert.IsTrue(cleared.Devices.All(d => d.Served == 0));
    }

    [TestMethod]
    public void Csv_DevicesAndTimeSeries()
    {
        var results = new RunResults
        {
            WarmUpS = 10,
            MeasuredS = 10,
            Devices = [new DeviceResult(0, "Gate, east", "Gate", 12, 3.24, 7.5, 9, 4, 0.425, 1, 0), new DeviceResult(1, "Machine \"A\"", "TicketMachine", 0, 0, 0, 0, 0, 0, 0, 2)],
            DeviceNames = ["Gate, east", "Machine \"A\""],
            Times = [5, 10, 15],
            InModel = [3, 6, 4],
            ArrivalsPerMin = [36, 36, 12],
            ExitsPerMin = [0, 0, 36],
            PeakDensity = [0.5f, 1.25f, 0.75f],
            Queues = [0, 0, 2, 1, 1, 0],
        };

        string[] devices = ResultsCsv.DevicesCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(3, devices.Length);
        Assert.AreEqual("Device,Type,Served,MeanWaitS,P95WaitS,MaxWaitS,MaxQueue,UtilisationPct,Balked,GaveUp", devices[0]);
        Assert.AreEqual("\"Gate, east\",Gate,12,3.2,7.5,9.0,4,42.5,1,0", devices[1], "Names with commas are quoted; invariant numbers.");
        StringAssert.StartsWith(devices[2], "\"Machine \"\"A\"\"\",TicketMachine,0,");

        string[] series = ResultsCsv.TimeSeriesCsv(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(4, series.Length);
        Assert.AreEqual("TimeS,WarmUp,InModel,ArrivalsPerMin,ExitsPerMin,PeakDensityPm2,\"Queue Gate, east\",\"Queue Machine \"\"A\"\"\"", series[0]);
        Assert.AreEqual("5.0,1,3,36.0,0.0,0.50,0,0", series[1], "Inside the warm-up.");
        Assert.AreEqual("10.0,0,6,36.0,0.0,1.25,2,1", series[2]);
    }

    [TestMethod]
    public void Fruin_GradeStartDensities_MatchTheGrades()
    {
        double[] starts = FruinLevelOfService.GradeStartDensities(LosAreaType.Walkway);
        Assert.AreEqual(5, starts.Length);
        for (int g = 0; g < starts.Length; g++)
        {
            Assert.AreEqual((LevelOfService)g, FruinLevelOfService.FromDensity(starts[g] * 0.99, LosAreaType.Walkway));
            Assert.AreEqual((LevelOfService)(g + 1), FruinLevelOfService.FromDensity(starts[g] * 1.01, LosAreaType.Walkway));
        }
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
