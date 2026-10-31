using Throng.Core.Library;

namespace Throng.Sim.Engine;

/// <summary>
/// The run overlay's counters (round 019, D-331–D-338): per journey row (profile × journey, as the journey results),
/// people out of each node output, and per node reached / finished, device waits, gave up, balked, dwell time and the
/// candidates people headed for. Only people spawned after the warm-up count (D-338). Arrays are sized once when the
/// rows are built; nothing here draws a random number, so runs stay identical with the counters on.
/// </summary>
public sealed partial class SimEngine
{
    // Per journey: compiled node → index of its port 0 in the row's wire counts (slot 0 is Spawn's output).
    private int[][] _flowPortBase = [];

    // Per journey row (empty arrays for rows with no journey).
    private long[][] _flowWires = [];
    private long[][] _fnReached = [];
    private long[][] _fnFinished = [];
    private long[][] _fnGaveUp = [];
    private long[][] _fnBalked = [];
    private double[][] _fnWaitSum = [];
    private List<float>[][] _fnWaits = [];
    private double[][] _fnDwellSum = [];
    private long[][] _fnDwelled = [];

    // Per journey row, per compiled node, per step target: headed for, served, wait sum.
    private long[][][] _fcCount = [];
    private long[][][] _fcServed = [];
    private double[][][] _fcWaitSum = [];

    /// <summary>Sizes the counters from the journey rows (called once, after <see cref="BuildJourneyRows"/>).</summary>
    private void BuildJourneyFlow()
    {
        _flowPortBase = new int[_model.Journeys.Count][];
        for (int j = 0; j < _model.Journeys.Count; j++)
        {
            SimNode[] nodes = _model.Journeys[j].Nodes;
            var bases = new int[nodes.Length];
            int next = 1;
            for (int n = 0; n < nodes.Length; n++)
            {
                bases[n] = next;
                next += nodes[n].Next.Length;
            }

            _flowPortBase[j] = bases;
        }

        int rows = _journeyRows.Length;
        _flowWires = new long[rows][];
        _fnReached = new long[rows][];
        _fnFinished = new long[rows][];
        _fnGaveUp = new long[rows][];
        _fnBalked = new long[rows][];
        _fnWaitSum = new double[rows][];
        _fnWaits = new List<float>[rows][];
        _fnDwellSum = new double[rows][];
        _fnDwelled = new long[rows][];
        _fcCount = new long[rows][][];
        _fcServed = new long[rows][][];
        _fcWaitSum = new double[rows][][];
        for (int r = 0; r < rows; r++)
        {
            int journey = _journeyRows[r].Journey;
            SimJourney? sim = journey >= 0 ? _model.Journeys[journey] : null;
            int nodes = sim?.Nodes.Length ?? 0;
            int ports = sim is null ? 0 : nodes == 0 ? 1 : _flowPortBase[journey][nodes - 1] + sim.Nodes[nodes - 1].Next.Length;
            _flowWires[r] = new long[ports];
            _fnReached[r] = new long[nodes];
            _fnFinished[r] = new long[nodes];
            _fnGaveUp[r] = new long[nodes];
            _fnBalked[r] = new long[nodes];
            _fnWaitSum[r] = new double[nodes];
            _fnWaits[r] = new List<float>[nodes];
            _fnDwellSum[r] = new double[nodes];
            _fnDwelled[r] = new long[nodes];
            _fcCount[r] = new long[nodes][];
            _fcServed[r] = new long[nodes][];
            _fcWaitSum[r] = new double[nodes][];
            for (int n = 0; n < nodes; n++)
            {
                SimNode node = sim!.Nodes[n];
                int targets = node.Kind == SimNodeKind.Step ? sim.Steps[node.Step].Targets.Length : 0;
                _fnWaits[r][n] = [];
                _fcCount[r][n] = new long[targets];
                _fcServed[r][n] = new long[targets];
                _fcWaitSum[r][n] = new double[targets];
            }
        }
    }

    /// <summary>Back to zero (run reset).</summary>
    private void ResetJourneyFlow()
    {
        if (_flowWires.Length != _journeyRows.Length)
        {
            BuildJourneyFlow();
        }

        for (int r = 0; r < _flowWires.Length; r++)
        {
            Array.Clear(_flowWires[r]);
            Array.Clear(_fnReached[r]);
            Array.Clear(_fnFinished[r]);
            Array.Clear(_fnGaveUp[r]);
            Array.Clear(_fnBalked[r]);
            Array.Clear(_fnWaitSum[r]);
            Array.Clear(_fnDwellSum[r]);
            Array.Clear(_fnDwelled[r]);
            for (int n = 0; n < _fnWaits[r].Length; n++)
            {
                _fnWaits[r][n].Clear();
                Array.Clear(_fcCount[r][n]);
                Array.Clear(_fcServed[r][n]);
                Array.Clear(_fcWaitSum[r][n]);
            }
        }
    }

    /// <summary>The journey row an agent's counts go to, or −1 (no journey, or spawned in the warm-up, D-338).</summary>
    private int FlowRow(int i)
    {
        if (_agents.Journey[i] < 0 || _agents.SpawnTime[i] < _model.WarmUpS - 1e-9)
        {
            return -1;
        }

        int row = JourneyRowOf(i);
        return row >= 0 && row < _flowWires.Length ? row : -1;
    }

    /// <summary>Counts one person out of Spawn's output.</summary>
    private void CountSpawnWire(int i)
    {
        int row = FlowRow(i);
        if (row >= 0 && _flowWires[row].Length > 0)
        {
            _flowWires[row][0]++;
            _resultsVersion++;
        }
    }

    /// <summary>Counts one person out of compiled node <paramref name="node"/>'s <paramref name="port"/> (row from <see cref="FlowRow"/>).</summary>
    private void CountWire(int row, int journey, int node, int port)
    {
        if (row < 0 || port < 0 || node < 0 || node >= _flowPortBase[journey].Length || port >= _model.Journeys[journey].Nodes[node].Next.Length)
        {
            return;
        }

        _flowWires[row][_flowPortBase[journey][node] + port]++;
        _resultsVersion++;
    }

    /// <summary>Counts one person arriving at compiled node <paramref name="node"/>.</summary>
    private void CountReached(int row, int node)
    {
        if (row >= 0 && node >= 0 && node < _fnReached[row].Length)
        {
            _fnReached[row][node]++;
        }
    }

    /// <summary>The compiled node of the agent's current step, or −1.</summary>
    private int CurrentNode(int i) => CurrentStep(i)?.Node ?? -1;

    /// <summary>Counts the agent completing its current step's node (devices, zones, dwell; sinks on leaving).</summary>
    private void CountFinished(int i)
    {
        int row = FlowRow(i);
        int node = CurrentNode(i);
        if (row >= 0 && node >= 0 && node < _fnFinished[row].Length)
        {
            _fnFinished[row][node]++;
        }
    }

    /// <summary>Counts the agent heading for <paramref name="target"/> (a value of the step's targets) at its node.</summary>
    private void CountCandidate(int i, SimStep step, int target, int delta = 1)
    {
        int row = FlowRow(i);
        int k = TargetSlot(step, target);
        if (row >= 0 && k >= 0 && step.Node >= 0 && step.Node < _fcCount[row].Length && k < _fcCount[row][step.Node].Length)
        {
            _fcCount[row][step.Node][k] += delta;
        }
    }

    /// <summary>Position of <paramref name="target"/> in the step's targets, or −1.</summary>
    private static int TargetSlot(SimStep step, int target)
    {
        int[] targets = step.Targets;
        for (int k = 0; k < targets.Length; k++)
        {
            if (targets[k] == target)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>Counts a device service starting after <paramref name="waitS"/> in line, at the agent's node and device.</summary>
    private void CountServed(int i, int device, double waitS)
    {
        int row = FlowRow(i);
        if (row < 0 || CurrentStep(i) is not { } step || step.Node < 0 || step.Node >= _fnWaits[row].Length)
        {
            return;
        }

        _fnWaitSum[row][step.Node] += waitS;
        _fnWaits[row][step.Node].Add((float)waitS);
        int k = TargetSlot(step, device);
        if (k >= 0 && k < _fcServed[row][step.Node].Length)
        {
            _fcServed[row][step.Node][k]++;
            _fcWaitSum[row][step.Node][k] += waitS;
        }
    }

    /// <summary>Counts a give-up (D-165) at the agent's node.</summary>
    private void CountGaveUp(int i)
    {
        int row = FlowRow(i);
        int node = CurrentNode(i);
        if (row >= 0 && node >= 0 && node < _fnGaveUp[row].Length)
        {
            _fnGaveUp[row][node]++;
        }
    }

    /// <summary>Counts a balk from <paramref name="from"/> to <paramref name="to"/> at the agent's node (the candidate counts move).</summary>
    private void CountBalk(int i, SimStep step, int from, int to)
    {
        int row = FlowRow(i);
        if (row >= 0 && step.Node >= 0 && step.Node < _fnBalked[row].Length)
        {
            _fnBalked[row][step.Node]++;
        }

        CountCandidate(i, step, from, -1);
        CountCandidate(i, step, to);
    }

    /// <summary>Counts a finished dwell of <paramref name="dwellS"/> at the agent's node.</summary>
    private void CountDwell(int i, double dwellS)
    {
        int row = FlowRow(i);
        int node = CurrentNode(i);
        if (row >= 0 && node >= 0 && node < _fnDwelled[row].Length)
        {
            _fnDwelled[row][node]++;
            _fnDwellSum[row][node] += dwellS;
        }
    }

    /// <summary>
    /// The overlay copy (D-331, D-333): per journey, a row for each profile on it and one for everyone (−1), in
    /// journey order. Called on the simulation thread from <see cref="CopyResults"/>.
    /// </summary>
    private JourneyFlowResult[] CopyJourneyFlow()
    {
        var results = new List<JourneyFlowResult>();
        for (int j = 0; j < _model.Journeys.Count; j++)
        {
            var rows = new List<int>();
            for (int r = 0; r < _journeyRows.Length && r < _flowWires.Length; r++)
            {
                if (_journeyRows[r].Journey == j)
                {
                    rows.Add(r);
                }
            }

            if (rows.Count == 0)
            {
                continue;
            }

            results.Add(FlowRowsResult(j, -1, rows));
            foreach (int r in rows)
            {
                results.Add(FlowRowsResult(j, _journeyRows[r].Profile, [r]));
            }
        }

        return [.. results];
    }

    /// <summary>One overlay result over the given journey rows (one profile's, or all of the journey's).</summary>
    private JourneyFlowResult FlowRowsResult(int journey, int profile, List<int> rows)
    {
        SimJourney sim = _model.Journeys[journey];
        int nodes = sim.Nodes.Length;
        long Sum(long[][] by, int index)
        {
            long total = 0;
            foreach (int r in rows)
            {
                total += index < by[r].Length ? by[r][index] : 0;
            }

            return total;
        }

        double SumD(double[][] by, int index)
        {
            double total = 0;
            foreach (int r in rows)
            {
                total += index < by[r].Length ? by[r][index] : 0;
            }

            return total;
        }

        var wires = new List<JourneyFlowWire> { new(sim.SpawnId, 0, sim.NodeIdAt(sim.Start), Sum(_flowWires, 0)) };
        var stats = new JourneyNodeStats[nodes];
        for (int n = 0; n < nodes; n++)
        {
            SimNode node = sim.Nodes[n];
            for (int port = 0; port < node.Next.Length; port++)
            {
                wires.Add(new JourneyFlowWire(sim.NodeIdAt(n), port, sim.NodeIdAt(node.Next[port]), Sum(_flowWires, _flowPortBase[journey][n] + port)));
            }

            var waits = new List<float>();
            foreach (int r in rows)
            {
                waits.AddRange(_fnWaits[r][n]);
            }

            SimStep? step = node.Kind == SimNodeKind.Step ? sim.Steps[node.Step] : null;
            var candidates = new JourneyCandidateStats[step?.Targets.Length ?? 0];
            for (int k = 0; k < candidates.Length; k++)
            {
                long count = 0, served = 0;
                double waitSum = 0;
                foreach (int r in rows)
                {
                    count += _fcCount[r][n][k];
                    served += _fcServed[r][n][k];
                    waitSum += _fcWaitSum[r][n][k];
                }

                candidates[k] = new JourneyCandidateStats(CandidateName(step!, step!.Targets[k]), count, served, served > 0 ? waitSum / served : 0);
            }

            long dwelled = Sum(_fnDwelled, n);
            string kind = KindName(node, step);
            stats[n] = new JourneyNodeStats(
                sim.NodeIdAt(n),
                step?.Label ?? kind,
                kind,
                Sum(_fnReached, n),
                Sum(_fnFinished, n),
                waits.Count,
                waits.Count > 0 ? SumD(_fnWaitSum, n) / waits.Count : 0,
                Percentile(waits, 0.95),
                Sum(_fnGaveUp, n),
                Sum(_fnBalked, n),
                dwelled,
                dwelled > 0 ? SumD(_fnDwellSum, n) / dwelled : 0,
                candidates);
        }

        return new JourneyFlowResult
        {
            JourneyIndex = journey,
            JourneyId = sim.JourneyId,
            JourneyName = sim.Name,
            ProfileIndex = profile,
            Profile = profile >= 0 ? _model.Profiles[profile].Template.Name : AllJourneysName,
            Wires = [.. wires],
            Nodes = stats,
            Started = wires[0].Count,
        };
    }

    /// <summary>A node's kind for the overlay rows and nodes.csv.</summary>
    private static string KindName(SimNode node, SimStep? step) => node.Kind switch
    {
        SimNodeKind.Chance => "Chance",
        SimNodeKind.TagCheck => "Tag check",
        SimNodeKind.SetTag => "Set tag",
        _ => step?.Kind switch
        {
            JourneyStepKind.GoToDevice => "Device",
            JourneyStepKind.PassZone => "Pass zone",
            JourneyStepKind.Dwell => "Dwell",
            JourneyStepKind.BoardTrain => "Board train",
            _ => "Exit",
        },
    };

    /// <summary>A candidate's name: the device's for device steps, else the route target's (zone or door).</summary>
    private string CandidateName(SimStep step, int target)
    {
        if (step.Kind == JourneyStepKind.GoToDevice)
        {
            return target >= 0 && target < _model.Devices.Count ? _model.Devices[target].Name : "device";
        }

        return target >= 0 && target < _world.Sinks.Count ? _world.Sinks[target].Name : "zone";
    }
}
