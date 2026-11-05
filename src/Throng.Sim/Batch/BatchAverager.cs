using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>
/// Combines a batch's runs into <see cref="BatchResults"/> (round 021, D-358–D-360, D-364). Pure and deterministic:
/// it reads the runs in seed order and nothing else, so the same runs always give the same summary whatever the
/// thread count. Counts average over every run (a row a run didn't have is zero there); times and shares average
/// over the runs that had someone to time.
/// </summary>
public static class BatchAverager
{
    /// <summary>Combines <paramref name="runs"/> (in seed order; at least one).</summary>
    /// <param name="runs">The finished runs, in seed order.</param>
    /// <param name="options">How the batch ran.</param>
    /// <param name="density">The averaged heatmaps, or null.</param>
    /// <param name="parallelism">Runs that ran at once.</param>
    /// <param name="elapsed">Wall-clock time taken.</param>
    public static BatchResults Combine(IReadOnlyList<BatchRun> runs, BatchOptions options, BatchDensity? density = null, int parallelism = 1, TimeSpan elapsed = default)
    {
        if (runs.Count == 0)
        {
            throw new ArgumentException("A batch needs at least one run.", nameof(runs));
        }

        RunResults first = runs[0].Results;
        List<DeviceBatchResult> devices = Devices(runs);
        List<ZoneBatchResult> zones = Zones(runs);
        List<JourneyBatchResult> journeys = Journeys(runs);
        List<LiftBatchResult> lifts = Lifts(runs);
        List<GroupJourneyBatchResult> groups = Groups(runs);

        int samples = 0;
        foreach (BatchRun run in runs)
        {
            samples = Math.Max(samples, run.Results.SampleCount);
        }

        double[] times = runs.Select(r => r.Results.Times).First(t => t.Length == samples);
        BatchSeries inModel = Series(runs, samples, r => r.InModel.Length, (r, s) => r.InModel[s]);
        BatchSeries arrivals = Series(runs, samples, r => r.ArrivalsPerMin.Length, (r, s) => r.ArrivalsPerMin[s]);
        BatchSeries exits = Series(runs, samples, r => r.ExitsPerMin.Length, (r, s) => r.ExitsPerMin[s]);
        BatchSeries peak = Series(runs, samples, r => r.PeakDensity.Length, (r, s) => r.PeakDensity[s]);
        var queues = new BatchSeries[devices.Count];
        for (int d = 0; d < queues.Length; d++)
        {
            int device = d;
            queues[d] = Series(runs, samples, r => r.Devices.Count == devices.Count ? r.SampleCount : 0, (r, s) => r.QueueAt(s, device));
        }

        var occupancy = new BatchSeries[zones.Count];
        for (int z = 0; z < occupancy.Length; z++)
        {
            int zone = z;
            occupancy[z] = Series(runs, samples, r => r.Zones.Count == zones.Count && r.ZoneOccupancy.Length >= r.SampleCount * r.Zones.Count ? r.SampleCount : 0, (r, s) => r.ZoneAt(s, zone));
        }

        double measured = Average(runs, r => r.MeasuredS);
        RunResults mean = MeanResults(runs, devices, zones, journeys, lifts, groups, times, inModel, arrivals, exits, peak, queues, occupancy, measured);
        return new BatchResults
        {
            Options = options,
            Parallelism = parallelism,
            Elapsed = elapsed,
            Runs = runs,
            WarmUpS = first.WarmUpS,
            MeasuredS = measured,
            LongWaitS = first.LongWaitS,
            Devices = devices,
            Zones = zones,
            Journeys = journeys,
            Lifts = lifts,
            Groups = groups,
            Times = times,
            InModel = inModel,
            ArrivalsPerMin = arrivals,
            ExitsPerMin = exits,
            PeakDensity = peak,
            Queues = queues,
            ZoneOccupancy = occupancy,
            Mean = mean,
            Density = density,
        };
    }

    /// <summary>The stat of a value every run has (or zero where a run lacks it).</summary>
    private static BatchStat Count(IReadOnlyList<BatchRun> runs, Func<RunResults, double> pick)
    {
        var values = new double[runs.Count];
        for (int r = 0; r < runs.Count; r++)
        {
            values[r] = pick(runs[r].Results);
        }

        return BatchStat.Of(values);
    }

    /// <summary>The stat over the runs where <paramref name="pick"/> has a value (null skips the run).</summary>
    private static BatchStat Timed(IReadOnlyList<BatchRun> runs, Func<RunResults, double?> pick)
    {
        var values = new List<double>(runs.Count);
        foreach (BatchRun run in runs)
        {
            if (pick(run.Results) is { } v)
            {
                values.Add(v);
            }
        }

        return BatchStat.Of(values);
    }

    private static double Average(IReadOnlyList<BatchRun> runs, Func<RunResults, double> pick) => Count(runs, pick).Mean;

    private static List<DeviceBatchResult> Devices(IReadOnlyList<BatchRun> runs)
    {
        IReadOnlyList<DeviceResult> template = runs[0].Results.Devices;
        var rows = new List<DeviceBatchResult>(template.Count);
        for (int d = 0; d < template.Count; d++)
        {
            int i = d;
            DeviceResult? At(RunResults r) => i < r.Devices.Count ? r.Devices[i] : null;
            double? Wait(RunResults r, Func<DeviceResult, double> pick) => At(r) is { Served: > 0 } x ? pick(x) : null;
            rows.Add(new DeviceBatchResult(
                template[d].Device, template[d].Name, template[d].TypeTag,
                Count(runs, r => At(r)?.Served ?? 0),
                Timed(runs, r => Wait(r, x => x.MeanWaitS)),
                Timed(runs, r => Wait(r, x => x.P95WaitS)),
                Timed(runs, r => Wait(r, x => x.MaxWaitS)),
                Count(runs, r => At(r)?.MaxQueue ?? 0),
                Count(runs, r => At(r)?.Utilisation ?? 0),
                Count(runs, r => At(r)?.Balked ?? 0),
                Count(runs, r => At(r)?.GaveUp ?? 0)));
        }

        return rows;
    }

    private static List<ZoneBatchResult> Zones(IReadOnlyList<BatchRun> runs)
    {
        IReadOnlyList<ZoneResult> template = runs[0].Results.Zones;
        var rows = new List<ZoneBatchResult>(template.Count);
        for (int z = 0; z < template.Count; z++)
        {
            int i = z;
            ZoneResult? At(RunResults r) => i < r.Zones.Count ? r.Zones[i] : null;
            rows.Add(new ZoneBatchResult(
                template[z].Zone, template[z].Name, template[z].AreaM2, template[z].WidthM,
                Count(runs, r => At(r)?.PeakOccupancy ?? 0),
                Count(runs, r => At(r)?.MeanOccupancy ?? 0),
                Count(runs, r => At(r)?.PeakDensity ?? 0),
                Count(runs, r => At(r)?.Entries ?? 0),
                Count(runs, r => At(r)?.EntriesPerMin ?? 0),
                Count(runs, r => At(r)?.EntriesPerMinPerM ?? 0)));
        }

        return rows;
    }

    private static List<LiftBatchResult> Lifts(IReadOnlyList<BatchRun> runs)
    {
        IReadOnlyList<LiftResult> template = runs[0].Results.Lifts;
        var rows = new List<LiftBatchResult>(template.Count);
        for (int l = 0; l < template.Count; l++)
        {
            int i = l;
            LiftResult? At(RunResults r) => i < r.Lifts.Count ? r.Lifts[i] : null;
            rows.Add(new LiftBatchResult(
                template[l].Lift, template[l].Name, template[l].Cars,
                Count(runs, r => At(r)?.Boarded ?? 0),
                Timed(runs, r => At(r) is { Boarded: > 0 } x ? x.MeanWaitS : null),
                Timed(runs, r => At(r) is { Boarded: > 0 } x ? x.P95WaitS : null),
                Timed(runs, r => At(r) is { Boarded: > 0 } x ? x.MaxWaitS : null),
                Count(runs, r => At(r)?.Rides ?? 0),
                Timed(runs, r => At(r) is { Rides: > 0 } x ? x.MeanRideS : null),
                Count(runs, r => At(r)?.GaveUp ?? 0)));
        }

        return rows;
    }

    /// <summary>
    /// Journey rows matched by profile, journey, ALL and sink. Order: the first run's, then each row only a later run
    /// had, placed after the last row of its profile (else at the end).
    /// </summary>
    private static List<JourneyBatchResult> Journeys(IReadOnlyList<BatchRun> runs)
    {
        var keys = new List<(string Profile, string Journey, bool IsAll, string Sink)>();
        var acceptable = new Dictionary<(string, string, bool, string), double>();
        foreach (BatchRun run in runs)
        {
            foreach (JourneyResult j in run.Results.Journeys)
            {
                var key = (j.Profile, j.Journey, j.IsAll, j.Sink);
                if (acceptable.ContainsKey(key))
                {
                    continue;
                }

                acceptable[key] = j.AcceptableS;
                int at = keys.FindLastIndex(k => string.Equals(k.Profile, j.Profile, StringComparison.Ordinal));
                keys.Insert(at < 0 ? keys.Count : at + 1, key);
            }
        }

        var rows = new List<JourneyBatchResult>(keys.Count);
        foreach (var key in keys)
        {
            JourneyResult? At(RunResults r)
            {
                foreach (JourneyResult j in r.Journeys)
                {
                    if (j.IsAll == key.IsAll && string.Equals(j.Profile, key.Profile, StringComparison.Ordinal)
                        && string.Equals(j.Journey, key.Journey, StringComparison.Ordinal) && string.Equals(j.Sink, key.Sink, StringComparison.Ordinal))
                    {
                        return j;
                    }
                }

                return null;
            }

            double? Timed1(RunResults r, Func<JourneyResult, double> pick) => At(r) is { Finished: > 0 } x ? pick(x) : null;
            rows.Add(new JourneyBatchResult(
                key.Profile, key.Journey, key.IsAll, key.Sink, acceptable[key],
                Count(runs, r => At(r)?.Finished ?? 0),
                Timed(runs, r => Timed1(r, x => x.MeanS)),
                Timed(runs, r => Timed1(r, x => x.P95S)),
                Timed(runs, r => Timed1(r, x => x.WithinAcceptablePct)),
                Timed(runs, r => Timed1(r, x => x.LongWaitPct)),
                Count(runs, r => At(r)?.GaveUp ?? 0),
                Count(runs, r => At(r)?.Stuck ?? 0),
                Count(runs, r => At(r)?.InProgress ?? 0)));
        }

        return rows;
    }

    /// <summary>Group × journey rows (round 024b) matched by group and journey, in the first run's order then later runs' new rows.</summary>
    private static List<GroupJourneyBatchResult> Groups(IReadOnlyList<BatchRun> runs)
    {
        var keys = new List<(string Group, string Journey)>();
        var acceptable = new Dictionary<(string, string), double>();
        foreach (BatchRun run in runs)
        {
            foreach (GroupJourneyResult g in run.Results.Groups)
            {
                if (acceptable.TryAdd((g.Group, g.Journey), g.AcceptableS))
                {
                    keys.Add((g.Group, g.Journey));
                }
            }
        }

        var rows = new List<GroupJourneyBatchResult>(keys.Count);
        foreach (var key in keys)
        {
            GroupJourneyResult? At(RunResults r)
            {
                foreach (GroupJourneyResult g in r.Groups)
                {
                    if (string.Equals(g.Group, key.Group, StringComparison.Ordinal) && string.Equals(g.Journey, key.Journey, StringComparison.Ordinal))
                    {
                        return g;
                    }
                }

                return null;
            }

            double? Timed1(RunResults r, Func<GroupJourneyResult, double> pick) => At(r) is { Finished: > 0 } x ? pick(x) : null;
            rows.Add(new GroupJourneyBatchResult(
                key.Group, key.Journey, acceptable[key],
                Count(runs, r => At(r)?.Finished ?? 0),
                Timed(runs, r => Timed1(r, x => x.MeanS)),
                Timed(runs, r => Timed1(r, x => x.P95S)),
                Timed(runs, r => Timed1(r, x => x.WithinAcceptablePct)),
                Timed(runs, r => Timed1(r, x => x.LongWaitPct)),
                Count(runs, r => At(r)?.GaveUp ?? 0),
                Count(runs, r => At(r)?.Stuck ?? 0),
                Count(runs, r => At(r)?.InProgress ?? 0),
                Timed(runs, r => Timed1(r, x => x.MeanSize)),
                Count(runs, r => At(r)?.People ?? 0),
                Timed(runs, r => Timed1(r, x => x.MeanSpreadS)),
                Timed(runs, r => Timed1(r, x => x.SpreadPct))));
        }

        return rows;
    }

    /// <summary>A series over the runs: sample <c>s</c> counts the runs whose <paramref name="length"/> passes it.</summary>
    private static BatchSeries Series(IReadOnlyList<BatchRun> runs, int samples, Func<RunResults, int> length, Func<RunResults, int, double> value)
    {
        var mean = new double[samples];
        var min = new double[samples];
        var max = new double[samples];
        for (int s = 0; s < samples; s++)
        {
            double sum = 0, lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            int n = 0;
            foreach (BatchRun run in runs)
            {
                if (s >= length(run.Results))
                {
                    continue;
                }

                double v = value(run.Results, s);
                sum += v;
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
                n++;
            }

            mean[s] = n > 0 ? sum / n : 0;
            min[s] = n > 0 ? lo : 0;
            max[s] = n > 0 ? hi : 0;
        }

        return new BatchSeries(mean, min, max);
    }

    /// <summary>The batch means in <see cref="RunResults"/> form, counts rounded to whole people (D-364).</summary>
    private static RunResults MeanResults(
        IReadOnlyList<BatchRun> runs, List<DeviceBatchResult> devices, List<ZoneBatchResult> zones, List<JourneyBatchResult> journeys, List<LiftBatchResult> lifts,
        List<GroupJourneyBatchResult> groups, double[] times, BatchSeries inModel, BatchSeries arrivals, BatchSeries exits, BatchSeries peak, BatchSeries[] queues, BatchSeries[] occupancy, double measured)
    {
        RunResults first = runs[0].Results;
        int samples = times.Length;
        var queueValues = new int[samples * devices.Count];
        for (int s = 0; s < samples; s++)
        {
            for (int d = 0; d < devices.Count; d++)
            {
                queueValues[(s * devices.Count) + d] = Round(queues[d].Mean[s]);
            }
        }

        var zoneValues = new int[samples * zones.Count];
        for (int s = 0; s < samples; s++)
        {
            for (int z = 0; z < zones.Count; z++)
            {
                zoneValues[(s * zones.Count) + z] = Round(occupancy[z].Mean[s]);
            }
        }

        double timeS = 0;
        long version = 0;
        foreach (BatchRun run in runs)
        {
            timeS = Math.Max(timeS, run.Results.TimeS);
            version = unchecked(version + run.Results.Version + 1);
        }

        return new RunResults
        {
            Version = version,
            TimeS = timeS,
            WarmUpS = first.WarmUpS,
            MeasuredS = measured,
            LongWaitS = first.LongWaitS,
            Devices = [.. devices.Select(d => new DeviceResult(d.Device, d.Name, d.TypeTag, d.Served.RoundedMean, d.MeanWaitS.Mean, d.P95WaitS.Mean, d.MaxWaitS.Mean,
                (int)d.MaxQueue.RoundedMean, d.Utilisation.Mean, d.Balked.RoundedMean, d.GaveUp.RoundedMean))],
            DeviceNames = first.DeviceNames,
            Times = times,
            InModel = [.. inModel.Mean.Select(Round)],
            ArrivalsPerMin = [.. arrivals.Mean.Select(v => (float)v)],
            ExitsPerMin = [.. exits.Mean.Select(v => (float)v)],
            PeakDensity = [.. peak.Mean.Select(v => (float)v)],
            Queues = queueValues,
            Zones = [.. zones.Select(z => new ZoneResult(z.Zone, z.Name, z.AreaM2, z.WidthM, (int)z.PeakOccupancy.RoundedMean, z.MeanOccupancy.Mean, z.Entries.RoundedMean,
                z.EntriesPerMin.Mean, z.EntriesPerMinPerM.Mean))],
            ZoneNames = first.ZoneNames,
            ZoneOccupancy = zoneValues,
            Journeys = [.. journeys.Select(j => new JourneyResult(j.Profile, j.Journey, j.IsAll, j.Finished.RoundedMean, j.MeanS.Mean, j.P95S.Mean, j.AcceptableS,
                j.WithinAcceptablePct.Mean, j.LongWaitPct.Mean, j.GaveUp.RoundedMean, j.Stuck.RoundedMean, (int)j.InProgress.RoundedMean, j.Sink))],
            Groups = [.. groups.Select(g => new GroupJourneyResult(g.Group, g.Journey, g.Finished.RoundedMean, g.MeanS.Mean, g.P95S.Mean, g.AcceptableS,
                g.WithinAcceptablePct.Mean, g.LongWaitPct.Mean, g.GaveUp.RoundedMean, g.Stuck.RoundedMean, (int)g.InProgress.RoundedMean, g.MeanSize.Mean,
                g.People.RoundedMean, g.MeanSpreadS.Mean, g.SpreadPct.Mean))],
            Lifts = [.. lifts.Select(l => new LiftResult(l.Lift, l.Name, l.Cars, l.Boarded.RoundedMean, l.MeanWaitS.Mean, l.P95WaitS.Mean, l.MaxWaitS.Mean, l.Rides.RoundedMean,
                l.MeanRideS.Mean, l.GaveUp.RoundedMean))],
            Flows = Flows(runs),
        };
    }

    /// <summary>
    /// Mean overlay rows (D-364), matched by journey and profile; wires by node and port, nodes by id, candidates by
    /// position. Counts are rounded means over every run; waits cover the runs that served someone.
    /// </summary>
    private static List<JourneyFlowResult> Flows(IReadOnlyList<BatchRun> runs)
    {
        var keys = new List<(Guid Journey, int Profile)>();
        var templates = new Dictionary<(Guid, int), JourneyFlowResult>();
        foreach (BatchRun run in runs)
        {
            foreach (JourneyFlowResult flow in run.Results.Flows)
            {
                if (templates.TryAdd((flow.JourneyId, flow.ProfileIndex), flow))
                {
                    keys.Add((flow.JourneyId, flow.ProfileIndex));
                }
            }
        }

        var flows = new List<JourneyFlowResult>(keys.Count);
        foreach (var key in keys)
        {
            JourneyFlowResult template = templates[key];
            var perRun = new JourneyFlowResult?[runs.Count];
            for (int r = 0; r < runs.Count; r++)
            {
                foreach (JourneyFlowResult flow in runs[r].Results.Flows)
                {
                    if (flow.JourneyId == key.Journey && flow.ProfileIndex == key.Profile)
                    {
                        perRun[r] = flow;
                        break;
                    }
                }
            }

            // Wires: every (from, port) any run had, first appearance order.
            var wireKeys = new List<JourneyFlowWire>();
            var seenWires = new HashSet<(Guid, int)>();
            foreach (JourneyFlowResult? flow in perRun)
            {
                foreach (JourneyFlowWire wire in flow?.Wires ?? [])
                {
                    if (seenWires.Add((wire.From, wire.Port)))
                    {
                        wireKeys.Add(wire);
                    }
                }
            }

            var wires = new List<JourneyFlowWire>(wireKeys.Count);
            foreach (JourneyFlowWire wire in wireKeys)
            {
                wires.Add(wire with { Count = MeanCount(perRun, f => f.CountOf(wire.From, wire.Port)) });
            }

            var nodes = new List<JourneyNodeStats>(template.Nodes.Count);
            foreach (JourneyNodeStats node in template.Nodes)
            {
                JourneyNodeStats?[] at = [.. perRun.Select(f => f?.NodeOf(node.NodeId))];
                var candidates = new List<JourneyCandidateStats>(node.Candidates.Count);
                for (int c = 0; c < node.Candidates.Count; c++)
                {
                    int i = c;
                    JourneyCandidateStats? Cand(JourneyNodeStats? n) => n is not null && i < n.Candidates.Count ? n.Candidates[i] : null;
                    candidates.Add(new JourneyCandidateStats(
                        node.Candidates[c].Name,
                        MeanOf(at, n => Cand(n)?.Count ?? 0),
                        MeanOf(at, n => Cand(n)?.Served ?? 0),
                        TimedOf(at, n => Cand(n) is { Served: > 0 } x ? x.MeanWaitS : null)));
                }

                nodes.Add(node with
                {
                    Reached = MeanOf(at, n => n?.Reached ?? 0),
                    Finished = MeanOf(at, n => n?.Finished ?? 0),
                    Waited = MeanOf(at, n => n?.Waited ?? 0),
                    MeanWaitS = TimedOf(at, n => n is { Waited: > 0 } ? n.MeanWaitS : null),
                    P95WaitS = TimedOf(at, n => n is { Waited: > 0 } ? n.P95WaitS : null),
                    GaveUp = MeanOf(at, n => n?.GaveUp ?? 0),
                    Balked = MeanOf(at, n => n?.Balked ?? 0),
                    Dwelled = MeanOf(at, n => n?.Dwelled ?? 0),
                    MeanDwellS = TimedOf(at, n => n is { Dwelled: > 0 } ? n.MeanDwellS : null),
                    Candidates = candidates,
                });
            }

            flows.Add(new JourneyFlowResult
            {
                JourneyIndex = template.JourneyIndex,
                JourneyId = template.JourneyId,
                JourneyName = template.JourneyName,
                ProfileIndex = template.ProfileIndex,
                Profile = template.Profile,
                Wires = wires,
                Nodes = nodes,
                Started = MeanCount(perRun, f => f.Started),
            });
        }

        return flows;
    }

    private static long MeanCount(JourneyFlowResult?[] perRun, Func<JourneyFlowResult, long> pick)
    {
        var values = new double[perRun.Length];
        for (int r = 0; r < perRun.Length; r++)
        {
            values[r] = perRun[r] is { } f ? pick(f) : 0;
        }

        return BatchStat.Of(values).RoundedMean;
    }

    private static long MeanOf(JourneyNodeStats?[] at, Func<JourneyNodeStats?, double> pick) =>
        BatchStat.Of([.. at.Select(pick)]).RoundedMean;

    private static double TimedOf<T>(T[] at, Func<T, double?> pick)
    {
        var values = new List<double>(at.Length);
        foreach (T item in at)
        {
            if (pick(item) is { } v)
            {
                values.Add(v);
            }
        }

        return BatchStat.Of(values).Mean;
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
