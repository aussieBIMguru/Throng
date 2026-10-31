namespace Throng.Sim.Engine;

/// <summary>
/// Journey results (round 012, D-229–D-231): one row per profile × journey the emission rules use, filed when a
/// person spawned after the warm-up leaves the model (finished or stuck); people still in the model are counted as in
/// progress when a copy is taken. Journeys with two or more sinks add a row per sink under the journey's row (D-312).
/// </summary>
public sealed partial class SimEngine
{
    /// <summary>Journey column for people with no journey (straight to an exit).</summary>
    public const string NoJourneyName = "(no journey)";

    /// <summary>Journey column of a profile's row over all its journeys.</summary>
    public const string AllJourneysName = "ALL";

    /// <summary>Sink name for people who left by the rule's sinks without reaching a sink node (D-312).</summary>
    public const string RuleSinksName = "rule's sinks";

    // Row lookup: profile * (journeys + 1) + (journey + 1) → row, or −1 when no rule uses that pair.
    private int[] _journeyRowOf = [];
    private (int Profile, int Journey)[] _journeyRows = [];
    private List<float>[] _jrTimes = [];
    private long[] _jrWithin = [];
    private long[] _jrLongWait = [];
    private long[] _jrGaveUp = [];
    private long[] _jrStuck = [];

    // Per-sink buckets (D-312): per journey, step index → bucket (0 = the rule's sinks, 1.. = its sink steps in
    // order, −1 = not a sink); per row and bucket, the finished.
    private int[][] _sinkBucketOf = [];
    private List<float>[][] _jrSinkTimes = [];
    private long[][] _jrSinkWithin = [];
    private long[][] _jrSinkLongWait = [];
    private long[][] _jrSinkGaveUp = [];

    /// <summary>Back to empty journey results; builds the row table on first use.</summary>
    private void ResetJourneyResults()
    {
        if (_journeyRowOf.Length == 0)
        {
            BuildJourneyRows();
        }

        foreach (List<float> times in _jrTimes)
        {
            times.Clear();
        }

        Array.Clear(_jrWithin);
        Array.Clear(_jrLongWait);
        Array.Clear(_jrGaveUp);
        Array.Clear(_jrStuck);
        for (int r = 0; r < _jrSinkTimes.Length; r++)
        {
            foreach (List<float> times in _jrSinkTimes[r])
            {
                times.Clear();
            }

            Array.Clear(_jrSinkWithin[r]);
            Array.Clear(_jrSinkLongWait[r]);
            Array.Clear(_jrSinkGaveUp[r]);
        }

        ResetJourneyFlow();
    }

    /// <summary>Rows in profile order, then journey order (no journey first), from the emitters' mix lines.</summary>
    private void BuildJourneyRows()
    {
        int profiles = _model.Profiles.Count;
        int stride = _model.Journeys.Count + 1;
        var used = new bool[Math.Max(1, profiles * stride)];
        foreach (SimEmitter emitter in _model.Emitters)
        {
            foreach (SimMixLine line in emitter.Lines)
            {
                if (line.Profile >= 0 && line.Profile < profiles)
                {
                    used[(line.Profile * stride) + line.Journey + 1] = true;
                }
            }
        }

        _journeyRowOf = new int[used.Length];
        var rows = new List<(int, int)>();
        for (int key = 0; key < used.Length; key++)
        {
            _journeyRowOf[key] = used[key] ? rows.Count : -1;
            if (used[key])
            {
                rows.Add((key / stride, (key % stride) - 1));
            }
        }

        _journeyRows = [.. rows];
        _jrTimes = new List<float>[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            _jrTimes[r] = [];
        }

        _jrWithin = new long[rows.Count];
        _jrLongWait = new long[rows.Count];
        _jrGaveUp = new long[rows.Count];
        _jrStuck = new long[rows.Count];

        _sinkBucketOf = new int[_model.Journeys.Count][];
        for (int j = 0; j < _model.Journeys.Count; j++)
        {
            SimStep[] steps = _model.Journeys[j].Steps;
            var map = new int[steps.Length];
            int bucket = 0;
            for (int k = 0; k < steps.Length; k++)
            {
                map[k] = steps[k].IsSink ? ++bucket : -1;
            }

            _sinkBucketOf[j] = map;
        }

        _jrSinkTimes = new List<float>[rows.Count][];
        _jrSinkWithin = new long[rows.Count][];
        _jrSinkLongWait = new long[rows.Count][];
        _jrSinkGaveUp = new long[rows.Count][];
        for (int r = 0; r < rows.Count; r++)
        {
            int journey = rows[r].Item2;
            int buckets = journey >= 0 ? 1 + _model.Journeys[journey].Steps.Count(st => st.IsSink) : 0;
            _jrSinkTimes[r] = new List<float>[buckets];
            for (int b = 0; b < buckets; b++)
            {
                _jrSinkTimes[r][b] = [];
            }

            _jrSinkWithin[r] = new long[buckets];
            _jrSinkLongWait[r] = new long[buckets];
            _jrSinkGaveUp[r] = new long[buckets];
        }
    }

    /// <summary>The sink bucket an agent leaving now counts in (D-312): its sink step's, or 0 for the rule's sinks.</summary>
    private int SinkBucketOf(int i)
    {
        int j = _agents.Journey[i];
        int k = _agents.Step[i];
        return j >= 0 && j < _sinkBucketOf.Length && k >= 0 && k < _sinkBucketOf[j].Length && _sinkBucketOf[j][k] > 0 ? _sinkBucketOf[j][k] : 0;
    }

    /// <summary>The row of an agent's profile and journey, or −1.</summary>
    private int JourneyRowOf(int i)
    {
        int profile = _agents.Profile[i];
        int key = (profile * (_model.Journeys.Count + 1)) + _agents.Journey[i] + 1;
        return profile >= 0 && key >= 0 && key < _journeyRowOf.Length ? _journeyRowOf[key] : -1;
    }

    /// <summary>Keeps an agent's longest single queue wait (D-229).</summary>
    private void NoteWait(int i, double waitS)
    {
        if (waitS > _agents.MaxWaitS[i])
        {
            _agents.MaxWaitS[i] = (float)waitS;
        }
    }

    /// <summary>Files an agent leaving the model (D-230): only those spawned after the warm-up count.</summary>
    /// <param name="i">Agent index (still in the store).</param>
    /// <param name="arrived">True when it finished; false when it was removed as stuck.</param>
    /// <param name="timeS">Time it left, s.</param>
    private void RecordJourneyEnd(int i, bool arrived, double timeS)
    {
        if (arrived && CurrentStep(i) is { IsSink: true })
        {
            // The run overlay (D-335): left by a sink node.
            CountFinished(i);
        }

        int row = JourneyRowOf(i);
        if (row < 0 || _agents.SpawnTime[i] < _model.WarmUpS - 1e-9)
        {
            return;
        }

        if (!arrived)
        {
            _jrStuck[row]++;
        }
        else
        {
            double journeyS = timeS - _agents.SpawnTime[i];
            bool within = journeyS <= _model.Profiles[_agents.Profile[i]].Template.AcceptableJourneyS;
            bool longWait = _agents.MaxWaitS[i] > _model.LongWaitS;
            bool gaveUp = _agents.LeftEarly[i];
            _jrTimes[row].Add((float)journeyS);
            _jrWithin[row] += within ? 1 : 0;
            _jrLongWait[row] += longWait ? 1 : 0;
            _jrGaveUp[row] += gaveUp ? 1 : 0;

            int bucket = SinkBucketOf(i);
            if (row < _jrSinkTimes.Length && bucket < _jrSinkTimes[row].Length)
            {
                _jrSinkTimes[row][bucket].Add((float)journeyS);
                _jrSinkWithin[row][bucket] += within ? 1 : 0;
                _jrSinkLongWait[row][bucket] += longWait ? 1 : 0;
                _jrSinkGaveUp[row][bucket] += gaveUp ? 1 : 0;
            }
        }

        _resultsVersion++;
    }

    /// <summary>Journey rows for a results copy: each pair, then an ALL row for profiles with two or more.</summary>
    private JourneyResult[] CopyJourneyResults()
    {
        int rows = _journeyRows.Length;
        var inProgress = new int[rows];
        for (int i = 0; i < _agents.Count; i++)
        {
            int row = JourneyRowOf(i);
            if (row >= 0 && _agents.SpawnTime[i] >= _model.WarmUpS - 1e-9)
            {
                inProgress[row]++;
            }
        }

        var results = new List<JourneyResult>(rows + _model.Profiles.Count);
        int r = 0;
        while (r < rows)
        {
            int profile = _journeyRows[r].Profile;
            int first = r;
            while (r < rows && _journeyRows[r].Profile == profile)
            {
                int journey = _journeyRows[r].Journey;
                string name = journey >= 0 ? _model.Journeys[journey].Name : NoJourneyName;
                results.Add(JourneyRow(profile, name, false, _jrTimes[r], _jrWithin[r], _jrLongWait[r], _jrGaveUp[r], _jrStuck[r], inProgress[r]));
                AddSinkRows(results, r, profile, journey, name);
                r++;
            }

            if (r - first >= 2)
            {
                var times = new List<float>();
                long within = 0, longWait = 0, gaveUp = 0, stuck = 0;
                int progress = 0;
                for (int k = first; k < r; k++)
                {
                    times.AddRange(_jrTimes[k]);
                    within += _jrWithin[k];
                    longWait += _jrLongWait[k];
                    gaveUp += _jrGaveUp[k];
                    stuck += _jrStuck[k];
                    progress += inProgress[k];
                }

                results.Add(JourneyRow(profile, AllJourneysName, true, times, within, longWait, gaveUp, stuck, progress));
            }
        }

        return [.. results];
    }

    /// <summary>
    /// Rows per sink under a journey with two or more sinks (D-312): each sink in order, then "rule's sinks" when
    /// anyone left without reaching a sink node.
    /// </summary>
    private void AddSinkRows(List<JourneyResult> results, int row, int profile, int journey, string name)
    {
        if (journey < 0 || row >= _jrSinkTimes.Length)
        {
            return;
        }

        SimStep[] steps = _model.Journeys[journey].Steps;
        int[] sinks = [.. Enumerable.Range(0, steps.Length).Where(k => steps[k].IsSink)];
        if (sinks.Length < 2)
        {
            return;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int b = 1; b < _jrSinkTimes[row].Length; b++)
        {
            // Two sinks with the same label ("Exit") get numbered so each row stays its own.
            string sink = b - 1 < sinks.Length ? steps[sinks[b - 1]].Label : "sink";
            int n = seen.GetValueOrDefault(sink) + 1;
            seen[sink] = n;
            sink = n > 1 ? $"{sink} {n.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : sink;
            results.Add(JourneyRow(profile, $"{name} ▸ {sink}", false, _jrSinkTimes[row][b], _jrSinkWithin[row][b], _jrSinkLongWait[row][b], _jrSinkGaveUp[row][b], 0, 0, sink));
        }

        if (_jrSinkTimes[row].Length > 0 && _jrSinkTimes[row][0].Count > 0)
        {
            results.Add(JourneyRow(profile, $"{name} ▸ {RuleSinksName}", false, _jrSinkTimes[row][0], _jrSinkWithin[row][0], _jrSinkLongWait[row][0], _jrSinkGaveUp[row][0], 0, 0, RuleSinksName));
        }
    }

    private JourneyResult JourneyRow(int profile, string journey, bool isAll, List<float> times, long within, long longWait, long gaveUp, long stuck, int inProgress, string sink = "")
    {
        int finished = times.Count;
        double sum = 0;
        foreach (float t in times)
        {
            sum += t;
        }

        double acceptable = _model.Profiles[profile].Template.AcceptableJourneyS;
        return new JourneyResult(
            _model.Profiles[profile].Template.Name,
            journey,
            isAll,
            finished,
            finished > 0 ? sum / finished : 0,
            Percentile(times, 0.95),
            acceptable,
            finished > 0 ? 100.0 * within / finished : 0,
            finished > 0 ? 100.0 * longWait / finished : 0,
            gaveUp,
            stuck,
            inProgress,
            sink);
    }
}
