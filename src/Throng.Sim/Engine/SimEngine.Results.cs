namespace Throng.Sim.Engine;

/// <summary>
/// Run results (round 011c, D-218–D-222): device stats gathered after the warm-up (served, waits, max queue,
/// utilisation, balks, gave up) and a time series from the start every <see cref="RunResults.SeriesIntervalS"/>.
/// Zone and journey results (round 012) live in <c>SimEngine.ZoneResults.cs</c> and <c>SimEngine.JourneyResults.cs</c>.
/// Written only on the simulation thread; <see cref="CopyResults"/> builds an immutable copy for the UI.
/// </summary>
public sealed partial class SimEngine
{
    // Per device, after the warm-up.
    private long[] _resServed = [];
    private double[] _resWaitSum = [];
    private double[] _resWaitMax = [];
    private List<float>[] _resWaits = [];
    private int[] _resMaxQueue = [];
    private double[] _resBusyS = [];
    private long[] _resBalked = [];
    private long[] _resGaveUp = [];
    private double _resMeasuredS;

    // Time series, from the start.
    private readonly List<double> _seriesTimes = [];
    private readonly List<int> _seriesInModel = [];
    private readonly List<float> _seriesArrivals = [];
    private readonly List<float> _seriesExits = [];
    private readonly List<float> _seriesPeak = [];
    private readonly List<int> _seriesQueues = [];
    private double _nextSeriesS;
    private long _seriesSpawned, _seriesArrived;
    private long _resultsVersion;

    /// <summary>True once the warm-up has passed (device stats are gathering).</summary>
    private bool Measuring => TimeS >= _model.WarmUpS - 1e-9;

    /// <summary>Back to empty results (run reset); sizes the arrays on first use.</summary>
    private void ResetResults()
    {
        int n = _model.Devices.Count;
        if (_resServed.Length != n)
        {
            _resServed = new long[n];
            _resWaitSum = new double[n];
            _resWaitMax = new double[n];
            _resWaits = new List<float>[n];
            _resMaxQueue = new int[n];
            _resBusyS = new double[n];
            _resBalked = new long[n];
            _resGaveUp = new long[n];
            for (int d = 0; d < n; d++)
            {
                _resWaits[d] = [];
            }
        }

        Array.Clear(_resServed);
        Array.Clear(_resWaitSum);
        Array.Clear(_resWaitMax);
        Array.Clear(_resMaxQueue);
        Array.Clear(_resBusyS);
        Array.Clear(_resBalked);
        Array.Clear(_resGaveUp);
        foreach (List<float> waits in _resWaits)
        {
            waits.Clear();
        }

        _resMeasuredS = 0;
        _seriesTimes.Clear();
        _seriesInModel.Clear();
        _seriesArrivals.Clear();
        _seriesExits.Clear();
        _seriesPeak.Clear();
        _seriesQueues.Clear();
        _nextSeriesS = 0;
        _seriesSpawned = _seriesArrived = 0;
        ResetZoneResults();
        ResetJourneyResults();
        _resultsVersion++;
    }

    /// <summary>A service started at a device after <paramref name="waitS"/> in line.</summary>
    private void RecordServed(int device, double waitS)
    {
        if (!Measuring)
        {
            return;
        }

        _resServed[device]++;
        _resWaitSum[device] += waitS;
        _resWaitMax[device] = Math.Max(_resWaitMax[device], waitS);
        _resWaits[device].Add((float)waitS);
        _resultsVersion++;
    }

    /// <summary>Someone left a device's line for another of the same type.</summary>
    private void RecordBalk(int device)
    {
        if (Measuring && device >= 0)
        {
            _resBalked[device]++;
            _resultsVersion++;
        }
    }

    /// <summary>Someone's patience ran out in a device's line.</summary>
    private void RecordGaveUp(int device)
    {
        if (Measuring && device >= 0)
        {
            _resGaveUp[device]++;
            _resultsVersion++;
        }
    }

    /// <summary>End of each step: busy lanes and queue peaks after the warm-up, and a series sample when due.</summary>
    private void RecordResults()
    {
        IReadOnlyList<SimDevice> devices = _model.Devices;
        CountZones();
        if (Measuring)
        {
            _resMeasuredS += _dt;
            for (int d = 0; d < devices.Count; d++)
            {
                SimDevice device = devices[d];
                int busy = 0;
                for (int lane = 0; lane < device.Lanes; lane++)
                {
                    busy += _laneServing[device.LaneBase + lane] ? 1 : 0;
                }

                _resBusyS[d] += busy * _dt;
                int queue = QueueLength(d);
                if (queue > _resMaxQueue[d])
                {
                    _resMaxQueue[d] = queue;
                    _resultsVersion++;
                }
            }
        }

        if (TimeS + 1e-9 < _nextSeriesS)
        {
            return;
        }

        double interval = _seriesTimes.Count == 0 ? TimeS : TimeS - _seriesTimes[^1];
        double perMin = interval > 0 ? 60.0 / interval : 0;
        _seriesTimes.Add(TimeS);
        _seriesInModel.Add(_agents.Count);
        _seriesArrivals.Add((float)((_spawned - _seriesSpawned) * perMin));
        _seriesExits.Add((float)((_arrived - _seriesArrived) * perMin));
        _seriesSpawned = _spawned;
        _seriesArrived = _arrived;
        _seriesPeak.Add(PeakDensityNow());
        for (int d = 0; d < devices.Count; d++)
        {
            _seriesQueues.Add(QueueLength(d));
        }

        SampleZones();

        _nextSeriesS = (Math.Floor((TimeS / RunResults.SeriesIntervalS) + 1e-9) + 1) * RunResults.SeriesIntervalS;
        _resultsVersion++;
    }

    /// <summary>Highest smoothed density on any mapped cell.</summary>
    private float PeakDensityNow()
    {
        DensityGrid grid = Density;
        ReadOnlySpan<float> values = grid.Smoothed;
        ReadOnlySpan<DensityCellKind> kinds = grid.Kinds;
        float peak = 0;
        for (int c = 0; c < values.Length; c++)
        {
            if (kinds[c] != DensityCellKind.None && values[c] > peak)
            {
                peak = values[c];
            }
        }

        return peak;
    }

    /// <summary>Bumped whenever the results change, so readers can skip unchanged copies.</summary>
    public long ResultsVersion => _resultsVersion;

    /// <summary>An immutable copy of the results so far (call on the simulation thread).</summary>
    public RunResults CopyResults()
    {
        IReadOnlyList<SimDevice> devices = _model.Devices;
        var rows = new DeviceResult[devices.Count];
        var names = new string[devices.Count];
        var zoneNames = new string[_model.Zones.Count];
        for (int z = 0; z < zoneNames.Length; z++)
        {
            zoneNames[z] = _model.Zones[z].Name;
        }

        for (int d = 0; d < devices.Count; d++)
        {
            SimDevice device = devices[d];
            long served = _resServed[d];
            double lanesS = _resMeasuredS * Math.Max(1, device.Lanes);
            rows[d] = new DeviceResult(
                d,
                device.Name,
                device.TypeTag,
                served,
                served > 0 ? _resWaitSum[d] / served : 0,
                Percentile(_resWaits[d], 0.95),
                _resWaitMax[d],
                _resMaxQueue[d],
                lanesS > 0 ? Math.Clamp(_resBusyS[d] / lanesS, 0, 1) : 0,
                _resBalked[d],
                _resGaveUp[d]);
            names[d] = device.Name;
        }

        return new RunResults
        {
            Version = _resultsVersion,
            TimeS = TimeS,
            WarmUpS = _model.WarmUpS,
            MeasuredS = _resMeasuredS,
            Devices = rows,
            DeviceNames = names,
            Times = [.. _seriesTimes],
            InModel = [.. _seriesInModel],
            ArrivalsPerMin = [.. _seriesArrivals],
            ExitsPerMin = [.. _seriesExits],
            PeakDensity = [.. _seriesPeak],
            Queues = [.. _seriesQueues],
            LongWaitS = _model.LongWaitS,
            Zones = CopyZoneResults(),
            ZoneNames = zoneNames,
            ZoneOccupancy = [.. _seriesZones],
            Journeys = CopyJourneyResults(),
        };
    }

    /// <summary>Nearest-rank percentile of a list (0 when empty); sorts a copy.</summary>
    private static double Percentile(List<float> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        float[] sorted = [.. values];
        Array.Sort(sorted);
        int rank = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[rank];
    }
}
