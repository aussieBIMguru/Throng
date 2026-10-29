using System.Globalization;
using System.Text;
using Throng.Core.Common;
using Throng.Sim.Engine;

namespace Throng.Analysis;

/// <summary>
/// CSV export of a run's results (round 011c, D-222; round 012, D-233): <c>devices.csv</c> (one row per device, after
/// the warm-up), <c>zones.csv</c> (one row per measurement zone), <c>journeys.csv</c> (profile × journey rows) and
/// <c>timeseries.csv</c> (one row per sample from the start, with a queue column per device and an occupancy column
/// per zone). Invariant culture, comma separated, RFC 4180 quoting, UTF-8 without a byte order mark.
/// </summary>
public static class ResultsCsv
{
    /// <summary>File name of the device table.</summary>
    public const string DevicesFileName = "devices.csv";

    /// <summary>File name of the time series.</summary>
    public const string TimeSeriesFileName = "timeseries.csv";

    /// <summary>File name of the zone table (D-233).</summary>
    public const string ZonesFileName = "zones.csv";

    /// <summary>File name of the journey table (D-233).</summary>
    public const string JourneysFileName = "journeys.csv";

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly char[] QuoteChars = [',', '"', '\r', '\n'];

    /// <summary>The device table as CSV text.</summary>
    public static string DevicesCsv(RunResults results)
    {
        var text = new StringBuilder();
        text.Append("Device,Type,Served,MeanWaitS,P95WaitS,MaxWaitS,MaxQueue,UtilisationPct,Balked,GaveUp\r\n");
        foreach (DeviceResult d in results.Devices)
        {
            text.Append(Quote(d.Name)).Append(',').Append(Quote(d.TypeTag)).Append(',')
                .Append(d.Served.ToString(C)).Append(',')
                .Append(d.MeanWaitS.ToString("0.0", C)).Append(',')
                .Append(d.P95WaitS.ToString("0.0", C)).Append(',')
                .Append(d.MaxWaitS.ToString("0.0", C)).Append(',')
                .Append(d.MaxQueue.ToString(C)).Append(',')
                .Append((d.Utilisation * 100).ToString("0.0", C)).Append(',')
                .Append(d.Balked.ToString(C)).Append(',')
                .Append(d.GaveUp.ToString(C)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The zone table as CSV text (D-228, D-233).</summary>
    public static string ZonesCsv(RunResults results)
    {
        var text = new StringBuilder();
        text.Append("Zone,AreaM2,WidthM,PeakOccupancy,MeanOccupancy,PeakDensityPm2,Entries,EntriesPerMin,EntriesPerMinPerM\r\n");
        foreach (ZoneResult z in results.Zones)
        {
            text.Append(Quote(z.Name)).Append(',')
                .Append(z.AreaM2.ToString("0.0", C)).Append(',')
                .Append(z.WidthM.ToString("0.00", C)).Append(',')
                .Append(z.PeakOccupancy.ToString(C)).Append(',')
                .Append(z.MeanOccupancy.ToString("0.0", C)).Append(',')
                .Append(z.PeakDensity.ToString("0.00", C)).Append(',')
                .Append(z.Entries.ToString(C)).Append(',')
                .Append(z.EntriesPerMin.ToString("0.0", C)).Append(',')
                .Append(z.EntriesPerMinPerM.ToString("0.0", C)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The journey table as CSV text (D-229, D-233); <c>LongWaitS</c> repeats the setting on each row.</summary>
    public static string JourneysCsv(RunResults results)
    {
        var text = new StringBuilder();
        text.Append("Profile,Journey,Finished,MeanS,P95S,AcceptableS,WithinAcceptablePct,LongWaitS,LongWaitPct,GaveUp,Stuck,InProgress\r\n");
        string longWait = results.LongWaitS.ToString("0", C);
        foreach (JourneyResult j in results.Journeys)
        {
            text.Append(Quote(j.Profile)).Append(',').Append(Quote(j.Journey)).Append(',')
                .Append(j.Finished.ToString(C)).Append(',')
                .Append(j.MeanS.ToString("0.0", C)).Append(',')
                .Append(j.P95S.ToString("0.0", C)).Append(',')
                .Append(j.AcceptableS.ToString("0", C)).Append(',')
                .Append(j.WithinAcceptablePct.ToString("0.0", C)).Append(',')
                .Append(longWait).Append(',')
                .Append(j.LongWaitPct.ToString("0.0", C)).Append(',')
                .Append(j.GaveUp.ToString(C)).Append(',')
                .Append(j.Stuck.ToString(C)).Append(',')
                .Append(j.InProgress.ToString(C)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The time series as CSV text; <c>WarmUp</c> is 1 for samples inside the warm-up (D-218).</summary>
    public static string TimeSeriesCsv(RunResults results)
    {
        // Zone columns only when the occupancy series is complete for every listed zone.
        int zones = Math.Min(results.Zones.Count, results.ZoneNames.Count);
        if (results.ZoneOccupancy.Length < results.SampleCount * results.Zones.Count)
        {
            zones = 0;
        }

        var text = new StringBuilder();
        text.Append("TimeS,WarmUp,InModel,ArrivalsPerMin,ExitsPerMin,PeakDensityPm2");
        foreach (string name in results.DeviceNames)
        {
            text.Append(',').Append(Quote("Queue " + name));
        }

        for (int z = 0; z < zones; z++)
        {
            text.Append(',').Append(Quote("Zone " + results.ZoneNames[z]));
        }

        text.Append("\r\n");
        int devices = results.Devices.Count;
        for (int s = 0; s < results.SampleCount; s++)
        {
            double t = results.Times[s];
            text.Append(t.ToString("0.0", C)).Append(',')
                .Append(t < results.WarmUpS - 1e-9 ? '1' : '0').Append(',')
                .Append(results.InModel[s].ToString(C)).Append(',')
                .Append(results.ArrivalsPerMin[s].ToString("0.0", C)).Append(',')
                .Append(results.ExitsPerMin[s].ToString("0.0", C)).Append(',')
                .Append(results.PeakDensity[s].ToString("0.00", C));
            for (int d = 0; d < devices; d++)
            {
                text.Append(',').Append(results.QueueAt(s, d).ToString(C));
            }

            for (int z = 0; z < zones; z++)
            {
                text.Append(',').Append(results.ZoneAt(s, z).ToString(C));
            }

            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>Writes all four files into <paramref name="folder"/>, replacing any already there.</summary>
    public static Result Export(RunResults results, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(folder, DevicesFileName), DevicesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, TimeSeriesFileName), TimeSeriesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, ZonesFileName), ZonesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, JourneysFileName), JourneysCsv(results), utf8);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail($"The results couldn't be written to '{folder}': {ex.Message}");
        }
    }

    /// <summary>Quotes a field when it holds a comma, quote or line break (RFC 4180).</summary>
    private static string Quote(string? value)
    {
        string v = value ?? string.Empty;
        return v.IndexOfAny(QuoteChars) >= 0 ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;
    }
}
