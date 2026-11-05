namespace Throng.Sim.Engine;

/// <summary>
/// Group journey results (round 024b, D-390): one row per group template × journey the mix lines use. A group spawned
/// after the warm-up is filed when its last member leaves: its time runs from spawning to then, and it counts as stuck
/// if any member was removed as stuck. Spread is the time two members were more than
/// <see cref="GroupJourneyResult.SpreadM"/> apart, measured every step. Nothing here draws a random number, so seeded
/// runs match round 024a.
/// </summary>
public sealed partial class SimEngine
{
    // Rows: (group name, journey) in the emitters' line order; per row the finished groups' stats.
    private (string Group, int Journey, int Lead)[] _groupRows = [];
    private readonly Dictionary<(string, int), int> _groupRowOf = new();
    private List<float>[] _grTimes = [];
    private long[] _grWithin = [];
    private long[] _grLongWait = [];
    private long[] _grGaveUp = [];
    private long[] _grStuck = [];
    private long[] _grPeople = [];
    private double[] _grSpreadS = [];
    private bool _groupRowsBuilt;

    // Per group slot: its row (−1 = not counted: spawned in the warm-up), spawn time, size and what happened so far.
    private int[] _gRow = [];
    private double[] _gSpawnS = [];
    private int[] _gSize = [];
    private bool[] _gStuck = [];
    private bool[] _gGaveUp = [];
    private bool[] _gLongWait = [];
    private double[] _gSpreadS = [];

    /// <summary>Back to empty group results; builds the row table on first use.</summary>
    private void ResetGroupResults()
    {
        if (!_groupRowsBuilt)
        {
            BuildGroupRows();
        }

        foreach (List<float> times in _grTimes)
        {
            times.Clear();
        }

        Array.Clear(_grWithin);
        Array.Clear(_grLongWait);
        Array.Clear(_grGaveUp);
        Array.Clear(_grStuck);
        Array.Clear(_grPeople);
        Array.Clear(_grSpreadS);
        Array.Fill(_gRow, -1);
    }

    /// <summary>Rows from the emitters' group lines: one per group name and journey, in line order.</summary>
    private void BuildGroupRows()
    {
        _groupRowOf.Clear();
        var rows = new List<(string, int, int)>();
        foreach (SimEmitter emitter in _model.Emitters)
        {
            foreach (SimMixLine line in emitter.Lines)
            {
                if (line.IsGroup && _groupRowOf.TryAdd((line.GroupName, line.Journey), rows.Count))
                {
                    rows.Add((line.GroupName, line.Journey, line.Profile));
                }
            }
        }

        _groupRows = [.. rows];
        _grTimes = new List<float>[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            _grTimes[r] = [];
        }

        _grWithin = new long[rows.Count];
        _grLongWait = new long[rows.Count];
        _grGaveUp = new long[rows.Count];
        _grStuck = new long[rows.Count];
        _grPeople = new long[rows.Count];
        _grSpreadS = new double[rows.Count];
        _groupRowsBuilt = true;
    }

    /// <summary>Grows the per-slot result arrays with the group table.</summary>
    private void EnsureGroupResultSlots(int size)
    {
        if (_gRow.Length >= size)
        {
            return;
        }

        int old = _gRow.Length;
        Array.Resize(ref _gRow, size);
        Array.Resize(ref _gSpawnS, size);
        Array.Resize(ref _gSize, size);
        Array.Resize(ref _gStuck, size);
        Array.Resize(ref _gGaveUp, size);
        Array.Resize(ref _gLongWait, size);
        Array.Resize(ref _gSpreadS, size);
        Array.Fill(_gRow, -1, old, size - old);
    }

    /// <summary>A group of <paramref name="line"/> just spawned in slot <paramref name="g"/> with <paramref name="size"/> people.</summary>
    private void StartGroupResults(int g, SimMixLine line, int size)
    {
        if (!_groupRowsBuilt)
        {
            BuildGroupRows();
        }

        EnsureGroupResultSlots(g + 1);
        _gRow[g] = Measuring && _groupRowOf.TryGetValue((line.GroupName, line.Journey), out int row) ? row : -1;
        _gSpawnS[g] = TimeS;
        _gSize[g] = size;
        _gStuck[g] = false;
        _gGaveUp[g] = false;
        _gLongWait[g] = false;
        _gSpreadS[g] = 0;
    }

    /// <summary>
    /// Spread (D-390): every counted group with two members more than <see cref="GroupJourneyResult.SpreadM"/> apart
    /// this step gets another step's worth of spread time. Call when the member index is fresh.
    /// </summary>
    private void MeasureGroupSpread()
    {
        const double limit = GroupJourneyResult.SpreadM * GroupJourneyResult.SpreadM;
        AgentStore a = _agents;
        for (int g = 0; g < _indexedSlots && g < _gRow.Length; g++)
        {
            int first = _gFirst[g], last = _gFirst[g + 1];
            if (_gRow[g] < 0 || last - first < 2)
            {
                continue;
            }

            bool apart = false;
            for (int p = first; p < last && !apart; p++)
            {
                int i = _gList[p];
                for (int q = p + 1; q < last; q++)
                {
                    int k = _gList[q];
                    double dx = a.X[i] - a.X[k], dy = a.Y[i] - a.Y[k], dz = a.Z[i] - a.Z[k];
                    if ((dx * dx) + (dy * dy) + (dz * dz) > limit)
                    {
                        apart = true;
                        break;
                    }
                }
            }

            if (apart)
            {
                _gSpreadS[g] += _dt;
            }
        }
    }

    /// <summary>
    /// A member of a group leaves the model (call before <see cref="LeaveGroup"/>): keeps what happened to them, and
    /// files the group when they're the last.
    /// </summary>
    private void NoteGroupLeave(int i, bool arrived, double timeS)
    {
        int g = _agents.Group[i];
        if (g < 0 || g >= _groupSlots || g >= _gRow.Length || _gRow[g] < 0)
        {
            return;
        }

        _gStuck[g] |= !arrived;
        _gGaveUp[g] |= arrived && _agents.LeftEarly[i];
        _gLongWait[g] |= _agents.MaxWaitS[i] > _model.LongWaitS;
        if (_gAlive[g] > 1)
        {
            return;
        }

        int row = _gRow[g];
        _gRow[g] = -1;
        if (_gStuck[g])
        {
            _grStuck[row]++;
        }
        else
        {
            double groupS = timeS - _gSpawnS[g];
            _grTimes[row].Add((float)groupS);
            _grWithin[row] += groupS <= LeadAcceptableS(row) ? 1 : 0;
            _grLongWait[row] += _gLongWait[g] ? 1 : 0;
            _grGaveUp[row] += _gGaveUp[g] ? 1 : 0;
            _grPeople[row] += _gSize[g];
            _grSpreadS[row] += _gSpreadS[g];
        }

        _resultsVersion++;
    }

    /// <summary>The acceptable journey time of a row's lead profile, seconds.</summary>
    private double LeadAcceptableS(int row)
    {
        int lead = _groupRows[row].Lead;
        return lead >= 0 && lead < _model.Profiles.Count ? _model.Profiles[lead].Template.AcceptableJourneyS : double.PositiveInfinity;
    }

    /// <summary>Group rows for a results copy, in row order (rows no group has finished yet included).</summary>
    private GroupJourneyResult[] CopyGroupResults()
    {
        int rows = _groupRows.Length;
        var inProgress = new int[rows];
        for (int g = 0; g < _groupSlots && g < _gRow.Length; g++)
        {
            if (_gRow[g] >= 0 && _gAlive[g] > 0)
            {
                inProgress[_gRow[g]]++;
            }
        }

        var results = new GroupJourneyResult[rows];
        for (int r = 0; r < rows; r++)
        {
            (string group, int journey, int _) = _groupRows[r];
            List<float> times = _grTimes[r];
            int finished = times.Count;
            double sum = 0;
            foreach (float t in times)
            {
                sum += t;
            }

            results[r] = new GroupJourneyResult(
                group,
                journey >= 0 ? _model.Journeys[journey].Name : NoJourneyName,
                finished,
                finished > 0 ? sum / finished : 0,
                Percentile(times, 0.95),
                LeadAcceptableS(r),
                finished > 0 ? 100.0 * _grWithin[r] / finished : 0,
                finished > 0 ? 100.0 * _grLongWait[r] / finished : 0,
                _grGaveUp[r],
                _grStuck[r],
                inProgress[r],
                finished > 0 ? (double)_grPeople[r] / finished : 0,
                _grPeople[r],
                finished > 0 ? _grSpreadS[r] / finished : 0,
                sum > 0 ? 100.0 * _grSpreadS[r] / sum : 0);
        }

        return results;
    }
}
