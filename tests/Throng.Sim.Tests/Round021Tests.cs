using Throng.Analysis;
using Throng.Core.Model;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 021: seeded batch runs in parallel (D-354–D-360, D-364).</summary>
[TestClass]
public sealed class Round021Tests
{
    [TestMethod]
    public void BatchRun_IsPlayOnItsSeed()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 7);
        SimModel model = SimTestStudies.Compile(study);
        BatchResults batch = new BatchRunner(model, new BatchOptions(7, 3, 120, Parallelism: 3)).Run();

        Assert.AreEqual(3, batch.Count);
        for (int k = 0; k < batch.Count; k++)
        {
            Assert.AreEqual(7 + k, batch.Runs[k].Seed, "Seeds run base, base + 1, …");

            // PLAY on that seed: the study saved with the seed, compiled on its own, stepped for the same time.
            Study single = SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 7 + k);
            RunResults expected = Round009bTests.Run(single, 120).CopyResults();
            RunResults actual = batch.Runs[k].Results;
            CollectionAssert.AreEqual(expected.Times, actual.Times, $"Run {k}: sample times.");
            CollectionAssert.AreEqual(expected.InModel, actual.InModel, $"Run {k}: people in the model at each sample.");
            CollectionAssert.AreEqual(expected.ExitsPerMin, actual.ExitsPerMin, $"Run {k}: exits per minute.");
            Assert.AreEqual(expected.Journeys.Count, actual.Journeys.Count, $"Run {k}: journey rows.");
            for (int j = 0; j < expected.Journeys.Count; j++)
            {
                Assert.AreEqual(expected.Journeys[j], actual.Journeys[j], $"Run {k}: journey row {j}.");
            }
        }

        Assert.IsFalse(batch.Runs[0].Results.InModel.SequenceEqual(batch.Runs[1].Results.InModel), "Different seeds give different crowds.");
    }

    [TestMethod]
    public void SameBaseSeed_SameSummary_WhateverTheThreads()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 11));
        BatchResults one = new BatchRunner(model, new BatchOptions(11, 4, 90, Parallelism: 1)).Run();
        BatchResults four = new BatchRunner(model, new BatchOptions(11, 4, 90, Parallelism: 4)).Run();

        Assert.AreEqual(1, one.Parallelism);
        CollectionAssert.AreEqual(one.InModel.Mean, four.InModel.Mean, "Mean series.");
        CollectionAssert.AreEqual(one.InModel.Min, four.InModel.Min, "Min series.");
        CollectionAssert.AreEqual(one.InModel.Max, four.InModel.Max, "Max series.");
        Assert.AreEqual(one.Journeys.Count, four.Journeys.Count);
        for (int j = 0; j < one.Journeys.Count; j++)
        {
            Assert.AreEqual(one.Journeys[j].Finished, four.Journeys[j].Finished, $"Journey row {j} finished.");
            Assert.AreEqual(one.Journeys[j].MeanS, four.Journeys[j].MeanS, $"Journey row {j} mean time.");
        }

        Assert.IsNotNull(one.Density);
        Assert.IsNotNull(four.Density);
        foreach (DensityMeasure measure in new[] { DensityMeasure.Max, DensityMeasure.Mean, DensityMeasure.LosEOrWorseS, DensityMeasure.Footfall })
        {
            CollectionAssert.AreEqual(one.Density!.Values(measure).ToArray(), four.Density!.Values(measure).ToArray(), $"{measure} map.");
        }

        Assert.AreEqual(0, one.Density!.Values(DensityMeasure.Live).Length, "A batch has no live map.");
        Assert.IsTrue(one.Density.Values(DensityMeasure.Footfall).ToArray().Any(v => v > 0), "People walked the corridor.");
    }

    [TestMethod]
    public void PeakMap_IsTheHighestRun_MeanMapTheAverage()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 3));
        var options = new BatchOptions(3, 2, 60, Parallelism: 2);
        BatchResults batch = new BatchRunner(model, options).Run();

        // The same two runs by hand.
        float[][] max = new float[2][], mean = new float[2][];
        for (int k = 0; k < 2; k++)
        {
            var engine = new SimEngine(model, options.SeedOf(k), options.DurationS);
            while (!engine.IsFinished)
            {
                engine.Step();
            }

            max[k] = engine.Density.Values(DensityMeasure.Max).ToArray();
            mean[k] = engine.Density.Values(DensityMeasure.Mean).ToArray();
        }

        float[] batchMax = batch.Density!.Values(DensityMeasure.Max).ToArray();
        float[] batchMean = batch.Density.Values(DensityMeasure.Mean).ToArray();
        for (int c = 0; c < batchMax.Length; c++)
        {
            Assert.AreEqual(Math.Max(max[0][c], max[1][c]), batchMax[c], 1e-6f, $"Peak at cell {c}.");
            Assert.AreEqual((mean[0][c] + mean[1][c]) / 2.0, batchMean[c], 1e-5, $"Mean at cell {c}.");
        }
    }

    [TestMethod]
    public void Averager_CountsCoverEveryRun_TimesOnlyRunsWithSomeone()
    {
        RunResults a = Results([0, 5], [2, 4], [new JourneyResult("Commuter", "Ticket", false, 10, 50, 80, 300, 90, 10, 1, 0, 2)]);
        RunResults b = Results([0, 5, 10], [6, 8, 9],
        [
            new JourneyResult("Commuter", "Ticket", false, 0, 0, 0, 300, 0, 0, 0, 1, 4),
            new JourneyResult("Visitor", "Ticket", false, 4, 30, 40, 300, 100, 0, 0, 0, 0),
            new JourneyResult("Commuter", "Ticket ▸ North", false, 3, 45, 60, 300, 100, 0, 0, 0, 0, "North"),
        ]);
        BatchResults batch = BatchAverager.Combine([new BatchRun(0, 1, a), new BatchRun(1, 2, b)], new BatchOptions(1, 2, 10));

        JourneyBatchResult ticket = batch.Journeys[0];
        Assert.AreEqual(new BatchStat(5, 0, 10, 2), ticket.Finished, "Counts average over both runs.");
        Assert.AreEqual(new BatchStat(50, 50, 50, 1), ticket.MeanS, "Times only count the run where someone finished.");
        Assert.AreEqual(new BatchStat(3, 2, 4, 2), ticket.InProgress, "In progress: 2 and 4.");

        // A row only the second run had: zero in the first, placed after its profile's rows.
        CollectionAssert.AreEqual(new[] { "Ticket", "Ticket ▸ North", "Ticket" }, batch.Journeys.Select(j => j.Journey).ToArray());
        Assert.AreEqual("Visitor", batch.Journeys[2].Profile);
        Assert.AreEqual(new BatchStat(1.5, 0, 3, 2), batch.Journeys[1].Finished);

        // A sample counts the runs that reached it.
        CollectionAssert.AreEqual(new[] { 0.0, 5, 10 }, batch.Times);
        CollectionAssert.AreEqual(new[] { 4.0, 6, 9 }, batch.InModel.Mean);
        CollectionAssert.AreEqual(new[] { 2.0, 4, 9 }, batch.InModel.Min);
        CollectionAssert.AreEqual(new[] { 6.0, 8, 9 }, batch.InModel.Max);

        // The rounded means for the overlay and other single-run readers (D-364).
        CollectionAssert.AreEqual(new[] { 4, 6, 9 }, batch.Mean.InModel);
        Assert.AreEqual(5, batch.Mean.Journeys[0].Finished);
    }

    [TestMethod]
    public void Cancelled_ThrowsAndStops()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        bool threw = false;
        try
        {
            new BatchRunner(model, new BatchOptions(1, 3, 600)).Run(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A cancelled batch throws OperationCanceledException.");
    }

    [TestMethod]
    public void Options_ParallelismIsCoresLessOne_CappedAtRuns()
    {
        Assert.AreEqual(Math.Clamp(Environment.ProcessorCount - 1, 1, 5), new BatchOptions(1, 5, 60).EffectiveParallelism);
        Assert.AreEqual(2, new BatchOptions(1, 2, 60, Parallelism: 8).EffectiveParallelism, "Never more than the runs.");
        Assert.AreEqual(int.MinValue, new BatchOptions(int.MaxValue, 2, 60).SeedOf(1), "Seeds wrap like the engine's cast.");
    }

    [TestMethod]
    public void EngineSeedOverride_MatchesModelSeed()
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 21);
        SimModel model = SimTestStudies.Compile(study);
        var a = new SimEngine(model);
        var b = new SimEngine(model, 21, null);
        Assert.AreEqual(21, b.Seed);
        Assert.IsNull(b.DurationS);
        for (int i = 0; i < 600; i++)
        {
            a.Step();
            b.Step();
        }

        Assert.AreEqual(a.Stats, b.Stats, "Same seed through either constructor, same run.");
        var finite = new SimEngine(model, 21, 10);
        while (!finite.IsFinished)
        {
            finite.Step();
        }

        Assert.AreEqual(10, finite.TimeS, 1e-6, "A duration given to the engine ends the run there.");
    }

    [TestMethod]
    public void BatchCsv_SummaryHasMeanMinMax_AndAFolderPerRun()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 40, pattern: EmissionPattern.Poisson, seed: 5));
        BatchResults batch = new BatchRunner(model, new BatchOptions(5, 2, 60, Parallelism: 2)).Run();
        StringAssert.StartsWith(BatchCsv.JourneysCsv(batch), "Profile,Journey,Sink,AcceptableS,LongWaitS,Finished,FinishedMin,FinishedMax,MeanS,");

        string folder = Path.Combine(Path.GetTempPath(), "throng-r021-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.IsTrue(BatchCsv.Export(batch, folder, model).IsSuccess);
            foreach (string file in new[] { "devices.csv", "zones.csv", "journeys.csv", "lifts.csv", "timeseries.csv", "runs.csv" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(folder, BatchCsv.SummaryFolderName, file)), $"summary/{file}");
            }

            Assert.IsTrue(File.Exists(Path.Combine(folder, "run-01-seed-5", ResultsCsv.JourneysFileName)), "Run 1's own files.");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "run-02-seed-6", ResultsCsv.NodesFileName)), "Run 2's own files.");
            Assert.AreEqual(3, File.ReadAllLines(Path.Combine(folder, BatchCsv.SummaryFolderName, BatchCsv.RunsFileName)).Length, "Header and one line per run.");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>A results copy with only a time series and journey rows.</summary>
    private static RunResults Results(double[] times, int[] inModel, JourneyResult[] journeys) => new()
    {
        Devices = [],
        DeviceNames = [],
        Times = times,
        InModel = inModel,
        ArrivalsPerMin = new float[times.Length],
        ExitsPerMin = new float[times.Length],
        PeakDensity = new float[times.Length],
        Queues = [],
        Journeys = journeys,
    };
}
