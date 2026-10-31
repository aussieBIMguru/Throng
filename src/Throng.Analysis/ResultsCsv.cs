using System.Globalization;
using System.Text;
using Throng.Analysis.Checks;
using Throng.Core.Common;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.Analysis;

/// <summary>
/// CSV export of a run's results (round 011c, D-222; round 012, D-233): <c>devices.csv</c> (one row per device, after
/// the warm-up), <c>zones.csv</c> (one row per measurement zone), <c>journeys.csv</c> (profile × journey rows) and
/// <c>timeseries.csv</c> (one row per sample from the start, with a queue column per device and an occupancy column
/// per zone), <c>lifts.csv</c> (one row per lift bank, round 015, D-260) and <c>nodes.csv</c> (one row per journey node
/// and wire, round 019, D-337). Invariant culture, comma separated, RFC 4180 quoting, UTF-8 without a byte order mark.
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

    /// <summary>File name of the lift bank table (D-260).</summary>
    public const string LiftsFileName = "lifts.csv";

    /// <summary>File name of the journey node and wire table (D-337).</summary>
    public const string NodesFileName = "nodes.csv";

    /// <summary>The header of <see cref="NodesFileName"/>.</summary>
    public const string NodesHeader = "Journey,Profile,Node,Kind,Port,To,Count,SharePct,PredictedPct,MeanWaitS,P95WaitS,GaveUp,Balked,MeanDwellS";

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

    /// <summary>
    /// The journey table as CSV text (D-229, D-233, D-312); <c>LongWaitS</c> repeats the setting on each row; <c>Sink</c>
    /// is empty on journey and ALL rows.
    /// </summary>
    public static string JourneysCsv(RunResults results)
    {
        var text = new StringBuilder();
        text.Append("Profile,Journey,Sink,Finished,MeanS,P95S,AcceptableS,WithinAcceptablePct,LongWaitS,LongWaitPct,GaveUp,Stuck,InProgress\r\n");
        string longWait = results.LongWaitS.ToString("0", C);
        foreach (JourneyResult j in results.Journeys)
        {
            // Per-sink rows (D-312) carry the journey's own name and the sink in its own column.
            string journey = j.IsSink && j.Journey.EndsWith(" ▸ " + j.Sink, StringComparison.Ordinal) ? j.Journey[..^(" ▸ ".Length + j.Sink.Length)] : j.Journey;
            text.Append(Quote(j.Profile)).Append(',').Append(Quote(journey)).Append(',').Append(Quote(j.Sink)).Append(',')
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

    /// <summary>The lift bank table as CSV text (D-260).</summary>
    public static string LiftsCsv(RunResults results)
    {
        var text = new StringBuilder();
        text.Append("Lift,Cars,Boarded,MeanWaitS,P95WaitS,MaxWaitS,Rides,MeanRideS,GaveUp\r\n");
        foreach (LiftResult l in results.Lifts)
        {
            text.Append(Quote(l.Name)).Append(',')
                .Append(l.Cars.ToString(C)).Append(',')
                .Append(l.Boarded.ToString(C)).Append(',')
                .Append(l.MeanWaitS.ToString("0.0", C)).Append(',')
                .Append(l.P95WaitS.ToString("0.0", C)).Append(',')
                .Append(l.MaxWaitS.ToString("0.0", C)).Append(',')
                .Append(l.Rides.ToString(C)).Append(',')
                .Append(l.MeanRideS.ToString("0.0", C)).Append(',')
                .Append(l.GaveUp.ToString(C)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// The journey node and wire table as CSV text (D-337): per journey, the ALL rows then each profile's; per node a
    /// row (<c>Port</c> empty: <c>Count</c> = reached, shares of the people who started) followed by a row per output
    /// (<c>Count</c> = people who took it). <c>PredictedPct</c> comes from the capacity model when
    /// <paramref name="model"/> (the run's) is given, else empty. The first row of each block is Spawn's output.
    /// </summary>
    public static string NodesCsv(RunResults results, SimModel? model = null)
    {
        var text = new StringBuilder();
        text.Append(NodesHeader).Append("\r\n");
        foreach (JourneyFlowResult flow in results.Flows)
        {
            JourneyShares? predicted = model is not null && flow.JourneyIndex >= 0 && flow.JourneyIndex < model.Journeys.Count
                && model.Journeys[flow.JourneyIndex].JourneyId == flow.JourneyId
                    ? CapacityCheck.PredictedShares(model, flow.JourneyIndex, flow.ProfileIndex)
                    : null;
            string journey = Quote(flow.JourneyName), profile = Quote(flow.Profile);
            double started = flow.Started;
            string Pct(double count) => started > 0 ? (100.0 * count / started).ToString("0.0", C) : "0.0";
            string Pred(double share) => predicted is null ? string.Empty : (100.0 * share).ToString("0.0", C);

            // Names: node labels, numbered when two share one ("Gate", "Gate 2").
            var names = new Dictionary<Guid, string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JourneyNodeStats node in flow.Nodes)
            {
                int n = seen.GetValueOrDefault(node.Label) + 1;
                seen[node.Label] = n;
                names[node.NodeId] = n > 1 ? $"{node.Label} {n.ToString(C)}" : node.Label;
            }

            string NameOf(Guid id) => id == Guid.Empty ? SimEngine.RuleSinksName : names.GetValueOrDefault(id, "?");
            int w = 0;
            if (flow.Wires.Count > 0)
            {
                JourneyFlowWire spawn = flow.Wires[0];
                WireRow(text, journey, profile, "Spawn", "Spawn", spawn, NameOf(spawn.To), Pct(spawn.Count), Pred(predicted is null ? 0 : 1));
                w = 1;
            }

            for (int n = 0; n < flow.Nodes.Count; n++)
            {
                JourneyNodeStats node = flow.Nodes[n];
                bool device = node.Kind == "Device", dwell = node.Kind == "Dwell";
                text.Append(journey).Append(',').Append(profile).Append(',').Append(Quote(names[node.NodeId])).Append(',').Append(Quote(node.Kind)).Append(",,,")
                    .Append(node.Reached.ToString(C)).Append(',')
                    .Append(Pct(node.Reached)).Append(',')
                    .Append(Pred(predicted is not null && n < predicted.Nodes.Length ? predicted.Nodes[n] : 0)).Append(',')
                    .Append(device ? node.MeanWaitS.ToString("0.0", C) : string.Empty).Append(',')
                    .Append(device ? node.P95WaitS.ToString("0.0", C) : string.Empty).Append(',')
                    .Append(device ? node.GaveUp.ToString(C) : string.Empty).Append(',')
                    .Append(device ? node.Balked.ToString(C) : string.Empty).Append(',')
                    .Append(dwell ? node.MeanDwellS.ToString("0.0", C) : string.Empty).Append("\r\n");

                // This node's outputs follow it in the wire list (compiled order).
                for (; w < flow.Wires.Count && flow.Wires[w].From == node.NodeId; w++)
                {
                    JourneyFlowWire wire = flow.Wires[w];
                    WireRow(text, journey, profile, names[node.NodeId], node.Kind, wire, NameOf(wire.To), Pct(wire.Count), Pred(predicted?.WireAt(n, wire.Port) ?? 0));
                }
            }
        }

        return text.ToString();
    }

    private static void WireRow(StringBuilder text, string journey, string profile, string node, string kind, JourneyFlowWire wire, string to, string pct, string predicted) =>
        text.Append(journey).Append(',').Append(profile).Append(',').Append(Quote(node)).Append(',').Append(Quote(kind)).Append(',')
            .Append(wire.Port.ToString(C)).Append(',').Append(Quote(to)).Append(',')
            .Append(wire.Count.ToString(C)).Append(',').Append(pct).Append(',').Append(predicted).Append(",,,,,\r\n");

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

    /// <summary>
    /// Writes all six files into <paramref name="folder"/>, replacing any already there; <paramref name="model"/> (the
    /// run's) adds the predicted shares to nodes.csv.
    /// </summary>
    public static Result Export(RunResults results, string folder, SimModel? model = null)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(folder, DevicesFileName), DevicesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, TimeSeriesFileName), TimeSeriesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, ZonesFileName), ZonesCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, JourneysFileName), JourneysCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, LiftsFileName), LiftsCsv(results), utf8);
            File.WriteAllText(Path.Combine(folder, NodesFileName), NodesCsv(results, model), utf8);
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
