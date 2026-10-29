namespace Throng.Sim.Engine;

/// <summary>
/// Journey results (round 012, D-229–D-231): one row per profile × journey the emission rules use, filed when a
/// person spawned after the warm-up leaves the model (finished or stuck); people still in the model are counted as in
/// progress when a copy is taken.
/// </summary>
public sealed partial class SimEngine
{
    /// <summary>Journey column for people with no journey (straight to an exit).</summary>
    public const string NoJourneyName = "(no journey)";

    /// <summary>Journey column of a profile's row over all its journeys.</summary>
    public const string AllJourneysName = "ALL";

    // Row lookup: profile * (journeys + 1) + (journey + 1) → row, or −1 when no rule uses that pair.
    private int[] _journeyRowOf = [];
    private (int Profile, int Journey)[] _journeyRows = [];
    private List<float>[] _jrTimes = [];
    private long[] _jrWithin = [];
    private long[] _jrLongWait = [];
    private long[] _jrGaveUp = [];
    private long[] _jrStuck = [];

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
            _jrTimes[row].Add((float)journeyS);
            if (journeyS <= _model.Profiles[_agents.Profile[i]].Template.AcceptableJourneyS)
            {
                _jrWithin[row]++;
            }

            if (_agents.MaxWaitS[i] > _model.LongWaitS)
            {
                _jrLongWait[row]++;
            }

            if (_agents.LeftEarly[i])
            {
                _jrGaveUp[row]++;
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

    private JourneyResult JourneyRow(int profile, string journey, bool isAll, List<float> times, long within, long longWait, long gaveUp, long stuck, int inProgress)
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
            inProgress);
    }
}
