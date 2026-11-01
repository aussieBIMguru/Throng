using System.Globalization;
using System.Text;
using Throng.Core.Common;
using Throng.Sim;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.Analysis;

/// <summary>
/// CSV export of a batch (round 021, D-362): a <c>summary</c> folder with the RESULTS tables and time series as mean,
/// min and max across the runs (each stat X becomes X, XMin, XMax) plus <c>runs.csv</c> (one line per run), and one
/// folder per run (<c>run-01-seed-42</c>) holding the usual single-run files (<see cref="ResultsCsv"/>). Same
/// conventions as <see cref="ResultsCsv"/>: invariant culture, RFC 4180 quoting, UTF-8 without a byte order mark.
/// </summary>
public static class BatchCsv
{
    /// <summary>Folder of the across-runs tables.</summary>
    public const string SummaryFolderName = "summary";

    /// <summary>File listing every run with its seed.</summary>
    public const string RunsFileName = "runs.csv";

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly char[] QuoteChars = [',', '"', '\r', '\n'];

    /// <summary>The folder name of run <paramref name="run"/>, e.g. "run-01-seed-42".</summary>
    public static string RunFolderName(BatchRun run) => string.Create(C, $"run-{run.Index + 1:00}-seed-{run.Seed}");

    /// <summary>The device table across runs.</summary>
    public static string DevicesCsv(BatchResults batch)
    {
        var text = new StringBuilder("Device,Type");
        Headers(text, "Served", "MeanWaitS", "P95WaitS", "MaxWaitS", "MaxQueue", "UtilisationPct", "Balked", "GaveUp");
        foreach (DeviceBatchResult d in batch.Devices)
        {
            text.Append(Quote(d.Name)).Append(',').Append(Quote(d.TypeTag));
            Stat(text, d.Served, "0.0").Stat(text, d.MeanWaitS, "0.0").Stat(text, d.P95WaitS, "0.0").Stat(text, d.MaxWaitS, "0.0").Stat(text, d.MaxQueue, "0.0")
                .Stat(text, Scale(d.Utilisation, 100), "0.0").Stat(text, d.Balked, "0.0").Stat(text, d.GaveUp, "0.0");
            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The zone table across runs.</summary>
    public static string ZonesCsv(BatchResults batch)
    {
        var text = new StringBuilder("Zone,AreaM2,WidthM");
        Headers(text, "PeakOccupancy", "MeanOccupancy", "PeakDensityPm2", "Entries", "EntriesPerMin", "EntriesPerMinPerM");
        foreach (ZoneBatchResult z in batch.Zones)
        {
            text.Append(Quote(z.Name)).Append(',').Append(z.AreaM2.ToString("0.0", C)).Append(',').Append(z.WidthM.ToString("0.00", C));
            Stat(text, z.PeakOccupancy, "0.0").Stat(text, z.MeanOccupancy, "0.0").Stat(text, z.PeakDensity, "0.00").Stat(text, z.Entries, "0.0")
                .Stat(text, z.EntriesPerMin, "0.0").Stat(text, z.EntriesPerMinPerM, "0.0");
            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The journey table across runs (<c>Sink</c> empty on journey and ALL rows).</summary>
    public static string JourneysCsv(BatchResults batch)
    {
        var text = new StringBuilder("Profile,Journey,Sink,AcceptableS,LongWaitS");
        Headers(text, "Finished", "MeanS", "P95S", "WithinAcceptablePct", "LongWaitPct", "GaveUp", "Stuck", "InProgress");
        string longWait = batch.LongWaitS.ToString("0", C);
        foreach (JourneyBatchResult j in batch.Journeys)
        {
            string journey = j.IsSink && j.Journey.EndsWith(" ▸ " + j.Sink, StringComparison.Ordinal) ? j.Journey[..^(" ▸ ".Length + j.Sink.Length)] : j.Journey;
            text.Append(Quote(j.Profile)).Append(',').Append(Quote(journey)).Append(',').Append(Quote(j.Sink)).Append(',')
                .Append(j.AcceptableS.ToString("0", C)).Append(',').Append(longWait);
            Stat(text, j.Finished, "0.0").Stat(text, j.MeanS, "0.0").Stat(text, j.P95S, "0.0").Stat(text, j.WithinAcceptablePct, "0.0")
                .Stat(text, j.LongWaitPct, "0.0").Stat(text, j.GaveUp, "0.0").Stat(text, j.Stuck, "0.0").Stat(text, j.InProgress, "0.0");
            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The lift bank table across runs.</summary>
    public static string LiftsCsv(BatchResults batch)
    {
        var text = new StringBuilder("Lift,Cars");
        Headers(text, "Boarded", "MeanWaitS", "P95WaitS", "MaxWaitS", "Rides", "MeanRideS", "GaveUp");
        foreach (LiftBatchResult l in batch.Lifts)
        {
            text.Append(Quote(l.Name)).Append(',').Append(l.Cars.ToString(C));
            Stat(text, l.Boarded, "0.0").Stat(text, l.MeanWaitS, "0.0").Stat(text, l.P95WaitS, "0.0").Stat(text, l.MaxWaitS, "0.0")
                .Stat(text, l.Rides, "0.0").Stat(text, l.MeanRideS, "0.0").Stat(text, l.GaveUp, "0.0");
            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The time series across runs: mean, min and max per column; a sample counts the runs that reached it.</summary>
    public static string TimeSeriesCsv(BatchResults batch)
    {
        var text = new StringBuilder("TimeS,WarmUp");
        var names = new List<string> { "InModel", "ArrivalsPerMin", "ExitsPerMin", "PeakDensityPm2" };
        var series = new List<BatchSeries> { batch.InModel, batch.ArrivalsPerMin, batch.ExitsPerMin, batch.PeakDensity };
        for (int d = 0; d < batch.Queues.Count && d < batch.Devices.Count; d++)
        {
            names.Add("Queue " + batch.Devices[d].Name);
            series.Add(batch.Queues[d]);
        }

        for (int z = 0; z < batch.ZoneOccupancy.Count && z < batch.Zones.Count; z++)
        {
            names.Add("Zone " + batch.Zones[z].Name);
            series.Add(batch.ZoneOccupancy[z]);
        }

        foreach (string name in names)
        {
            text.Append(',').Append(Quote(name)).Append(',').Append(Quote(name + " Min")).Append(',').Append(Quote(name + " Max"));
        }

        text.Append("\r\n");
        for (int s = 0; s < batch.SampleCount; s++)
        {
            double t = batch.Times[s];
            text.Append(t.ToString("0.0", C)).Append(',').Append(t < batch.WarmUpS - 1e-9 ? '1' : '0');
            foreach (BatchSeries line in series)
            {
                bool has = s < line.Count;
                text.Append(',').Append(has ? line.Mean[s].ToString("0.00", C) : string.Empty)
                    .Append(',').Append(has ? line.Min[s].ToString("0.00", C) : string.Empty)
                    .Append(',').Append(has ? line.Max[s].ToString("0.00", C) : string.Empty);
            }

            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>One line per run: its seed and headline totals (after the warm-up), and the folder with its files.</summary>
    public static string RunsCsv(BatchResults batch)
    {
        var text = new StringBuilder("Run,Seed,Folder,MeasuredS,Served,Finished,GaveUp,Stuck\r\n");
        foreach (BatchRun run in batch.Runs)
        {
            RunResults r = run.Results;
            long served = 0, finished = 0, gaveUp = 0, stuck = 0;
            foreach (DeviceResult d in r.Devices)
            {
                served += d.Served;
            }

            foreach (JourneyResult j in r.Journeys)
            {
                if (!j.IsAll && !j.IsSink)
                {
                    finished += j.Finished;
                    gaveUp += j.GaveUp;
                    stuck += j.Stuck;
                }
            }

            text.Append((run.Index + 1).ToString(C)).Append(',').Append(run.Seed.ToString(C)).Append(',').Append(Quote(RunFolderName(run))).Append(',')
                .Append(r.MeasuredS.ToString("0.0", C)).Append(',').Append(served.ToString(C)).Append(',').Append(finished.ToString(C)).Append(',')
                .Append(gaveUp.ToString(C)).Append(',').Append(stuck.ToString(C)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes the summary folder and a folder per run into <paramref name="folder"/>, replacing files already there;
    /// <paramref name="model"/> (the batch's) adds predicted shares to each run's nodes.csv.
    /// </summary>
    public static Result Export(BatchResults batch, string folder, SimModel? model = null)
    {
        try
        {
            string summary = Path.Combine(folder, SummaryFolderName);
            Directory.CreateDirectory(summary);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(summary, ResultsCsv.DevicesFileName), DevicesCsv(batch), utf8);
            File.WriteAllText(Path.Combine(summary, ResultsCsv.ZonesFileName), ZonesCsv(batch), utf8);
            File.WriteAllText(Path.Combine(summary, ResultsCsv.JourneysFileName), JourneysCsv(batch), utf8);
            File.WriteAllText(Path.Combine(summary, ResultsCsv.LiftsFileName), LiftsCsv(batch), utf8);
            File.WriteAllText(Path.Combine(summary, ResultsCsv.TimeSeriesFileName), TimeSeriesCsv(batch), utf8);
            File.WriteAllText(Path.Combine(summary, RunsFileName), RunsCsv(batch), utf8);
            foreach (BatchRun run in batch.Runs)
            {
                Result written = ResultsCsv.Export(run.Results, Path.Combine(folder, RunFolderName(run)), model);
                if (!written.IsSuccess)
                {
                    return written;
                }
            }

            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail($"The batch results couldn't be written to '{folder}': {ex.Message}");
        }
    }

    private static void Headers(StringBuilder text, params string[] stats)
    {
        foreach (string stat in stats)
        {
            text.Append(',').Append(stat).Append(',').Append(stat).Append("Min,").Append(stat).Append("Max");
        }

        text.Append("\r\n");
    }

    /// <summary>Appends ",mean,min,max"; returns a helper so calls chain.</summary>
    private static StatWriter Stat(StringBuilder text, BatchStat stat, string format) => new StatWriter().Stat(text, stat, format);

    private static BatchStat Scale(BatchStat stat, double factor) => stat with { Mean = stat.Mean * factor, Min = stat.Min * factor, Max = stat.Max * factor };

    private static string Quote(string? value)
    {
        string v = value ?? string.Empty;
        return v.IndexOfAny(QuoteChars) >= 0 ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;
    }

    /// <summary>Chains stat columns onto one line.</summary>
    private readonly struct StatWriter
    {
        public StatWriter Stat(StringBuilder text, BatchStat stat, string format)
        {
            text.Append(',').Append(stat.Mean.ToString(format, C)).Append(',').Append(stat.Min.ToString(format, C)).Append(',').Append(stat.Max.ToString(format, C));
            return this;
        }
    }
}
