using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// Round 010a, crowd-aware choice (D-178–D-183): what each person can see (D-179), stair and ramp waits measured
/// like belts (D-181), exit crowding (D-182), re-choosing while approaching (D-180), and the current waits for the
/// run-time hover label (D-183).
/// </summary>
public sealed partial class SimEngine
{
    /// <summary>Distance around an exit zone whose people count towards its crowding, m (D-182).</summary>
    public const double ExitCrowdRadiusM = 3.0;

    /// <summary>People per m² around an exit that still walk freely and add no wait (D-182).</summary>
    public const double ExitFreeDensity = 0.3;

    /// <summary>Flow an exit zone passes per metre of its width, people per second (about Fruin LoS C–D) (D-182).</summary>
    public const double ExitFlowPerMetre = 1.3;

    // Per connector: people on the flight (D-181).
    private int[] _onFlight = [];

    // Per route target: exit crowd counting box (expanded by ExitCrowdRadiusM), zone box for the sight test, the
    // box area and zone width for the delay, the people counted this step and the resulting wait (D-182).
    private double[] _exitMinX = [], _exitMinY = [], _exitMaxX = [], _exitMaxY = [];
    private double[] _zoneMinX = [], _zoneMinY = [], _zoneMaxX = [], _zoneMaxY = [];
    private double[] _exitArea = [], _exitWidth = [];
    private int[] _exitCount = [];
    private double[] _exitWait = [];

    // Exit targets per floor surface, so counting only looks at the exits on the person's floor.
    private int[][] _exitsByFloor = [];

    /// <summary>
    /// The current crowd wait at a connector in seconds (D-111, D-113, D-181): what route choice adds for someone who
    /// can see its entry. 0 for an unknown index.
    /// </summary>
    /// <param name="connector">Index into the world's connectors.</param>
    public double ConnectorWaitS(int connector) =>
        connector >= 0 && connector < _wait.Length ? _wait[connector] : 0;

    /// <summary>The current crowd wait at an exit in seconds (D-182); 0 for other targets or with the rule off.</summary>
    /// <param name="sink">Index into the world's route targets.</param>
    public double ExitCrowdS(int sink) =>
        sink >= 0 && sink < _exitWait.Length ? _exitWait[sink] : 0;

    /// <summary>Sets up the crowd arrays once the world is known (called from the constructor).</summary>
    private void InitCrowd()
    {
        _onFlight = new int[_world.Connectors.Count];

        int sinks = _world.Sinks.Count;
        _exitMinX = new double[sinks];
        _exitMinY = new double[sinks];
        _exitMaxX = new double[sinks];
        _exitMaxY = new double[sinks];
        _zoneMinX = new double[sinks];
        _zoneMinY = new double[sinks];
        _zoneMaxX = new double[sinks];
        _zoneMaxY = new double[sinks];
        _exitArea = new double[sinks];
        _exitWidth = new double[sinks];
        _exitCount = new int[sinks];
        _exitWait = new double[sinks];
        var byFloor = new List<int>[_world.Surfaces.Count];
        for (int s = 0; s < sinks; s++)
        {
            NavSink sink = _world.Sinks[s];
            if (sink.Role != NavTargetRole.Exit || sink.Cells.Length == 0)
            {
                continue;
            }

            NavSurface floor = _world.Surfaces[sink.Floor];
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (int cell in sink.Cells)
            {
                Point2 c = floor.CellCentre(cell);
                minX = Math.Min(minX, c.X);
                minY = Math.Min(minY, c.Y);
                maxX = Math.Max(maxX, c.X);
                maxY = Math.Max(maxY, c.Y);
            }

            _zoneMinX[s] = minX;
            _zoneMinY[s] = minY;
            _zoneMaxX[s] = maxX;
            _zoneMaxY[s] = maxY;
            _exitMinX[s] = minX - ExitCrowdRadiusM;
            _exitMinY[s] = minY - ExitCrowdRadiusM;
            _exitMaxX[s] = maxX + ExitCrowdRadiusM;
            _exitMaxY[s] = maxY + ExitCrowdRadiusM;
            _exitArea[s] = (_exitMaxX[s] - _exitMinX[s]) * (_exitMaxY[s] - _exitMinY[s]);
            _exitWidth[s] = Math.Max(1.0, Math.Max(maxX - minX, maxY - minY));
            (byFloor[sink.Floor] ??= []).Add(s);
        }

        _exitsByFloor = new int[_world.Surfaces.Count][];
        for (int f = 0; f < byFloor.Length; f++)
        {
            _exitsByFloor[f] = byFloor[f]?.ToArray() ?? [];
        }
    }

    /// <summary>Counts agent <paramref name="i"/> towards the crowding of every exit on its floor it stands near (D-182).</summary>
    private void CountExitCrowd(int i, double x, double y)
    {
        int[] exits = _exitsByFloor[_agents.Surface[i]];
        for (int e = 0; e < exits.Length; e++)
        {
            int s = exits[e];
            if (x >= _exitMinX[s] && x <= _exitMaxX[s] && y >= _exitMinY[s] && y <= _exitMaxY[s])
            {
                _exitCount[s]++;
            }
        }
    }

    /// <summary>
    /// Exit crowd waits from this step's counts (D-182): people over <see cref="ExitFreeDensity"/> in the box around
    /// the exit ÷ (<see cref="ExitFlowPerMetre"/> × the zone's width).
    /// </summary>
    private void UpdateExitWaits()
    {
        for (int s = 0; s < _exitWait.Length; s++)
        {
            double excess = _exitCount[s] - (ExitFreeDensity * _exitArea[s]);
            _exitWait[s] = _tuning.ExitCrowd && excess > 0 && _exitWidth[s] > 0 ? excess / (ExitFlowPerMetre * _exitWidth[s]) : 0;
        }
    }

    /// <summary>
    /// Stair or ramp wait measured like a belt (D-181): people stopped near the entry ÷ the measured flow onto it,
    /// everyone else heading for it or on the flight ÷ its capacity. With the flow clamped to at most the capacity
    /// this is never less than D-111's load ÷ capacity, and grows when a held queue boards slowly (r010a-2).
    /// </summary>
    private double MeasuredStairWait(int k)
    {
        NavConnector connector = _world.Connectors[k];
        double cap = Math.Max(1e-3, connector.CapacityPerSecond);
        if (_slowNear[k] >= 2)
        {
            // Only measured while people are actually held at the entry, so free flow doesn't read as slow.
            _rate[k] += ((_boarded[k] / _dt) - _rate[k]) * Math.Min(1.0, _dt / RateTauS);
        }

        _rate[k] = Math.Clamp(_rate[k], 0.1 * cap, cap);
        double others = Math.Max(0, _near[k] - _slowNear[k]) + _far[k] + _onFlight[k];
        return (_slowNear[k] / _rate[k]) + (others / cap);
    }

    /// <summary>How far agent <paramref name="i"/> can see crowding (D-179); +∞ with <see cref="SimTuning.SightLimited"/> off.</summary>
    private double Sight(int i)
    {
        if (!_tuning.SightLimited)
        {
            return double.PositiveInfinity;
        }

        double radius = _model.Profiles[_agents.Profile[i]].Template.SightRadiusM;
        return double.IsFinite(radius) ? Math.Clamp(radius, 0, 1000) : AgentProfileTemplate.DefaultSightRadiusM;
    }

    /// <summary>True when the exit zone is on agent <paramref name="i"/>'s floor within its sight (D-179).</summary>
    private bool ExitInSight(int i, int sink)
    {
        if (sink < 0 || sink >= _exitWait.Length || _world.Sinks[sink].Floor != _agents.Surface[i])
        {
            return false;
        }

        double sight = Sight(i);
        if (double.IsPositiveInfinity(sight))
        {
            return true;
        }

        double dx = Math.Max(0, Math.Max(_zoneMinX[sink] - _agents.X[i], _agents.X[i] - _zoneMaxX[sink]));
        double dy = Math.Max(0, Math.Max(_zoneMinY[sink] - _agents.Y[i], _agents.Y[i] - _zoneMaxY[sink]));
        return (dx * dx) + (dy * dy) <= sight * sight;
    }

    /// <summary>The crowd wait agent <paramref name="i"/> adds for an exit in nearest-sink choice: 0 when out of sight (D-182).</summary>
    private double ExitCrowdFor(int i, int sink) =>
        _tuning.ExitCrowd && ExitInSight(i, sink) ? _exitWait[sink] : 0;

    /// <summary>
    /// Re-choosing while approaching (D-180): on this agent's review step, look again at its exit (nearest-sink
    /// rules) and then its connector, while each is in sight, without noise and with a bonus for staying.
    /// </summary>
    private void ReviewApproach(int i)
    {
        if (!IsReviewTick(i) || _world.Surfaces[_agents.Surface[i]].Kind != NavSurfaceKind.Floor)
        {
            return;
        }

        if (ReviewExit(i))
        {
            return;
        }

        AgentStore a = _agents;
        int target = a.Target[i];
        if (a.Phase[i] != AgentPhase.Walk || a.Mode[i] != AgentMode.ToPortal || target < 0)
        {
            return;
        }

        NavPortal portal = _world.Portals[target];
        double sight = Sight(i);
        if (!RoutePlanner.InSight(portal, a.X[i], a.Y[i], sight))
        {
            return;
        }

        SimProfile profile = _model.Profiles[a.Profile[i]];
        double cost = RoutePlanner.Choose(
            _routes, a.Surface[i], a.X[i], a.Y[i], a.Sink[i], profile.Mobility, profile.Template, null, out bool direct, out RouteOption option, _wait,
            BeltWalkSpeed(i), portal.Connector, StayBonusS, _tuning.StairEffort, _tuning.StandingRest, sight);
        if (!double.IsFinite(cost))
        {
            return;
        }

        if (direct)
        {
            SetMode(i, AgentMode.Direct, -1);
        }
        else if (option.Connector != portal.Connector)
        {
            SetMode(i, AgentMode.ToPortal, option.Entry);
            a.PlannedExit[i] = option.Exit;
        }
    }

    /// <summary>
    /// Looks again at a nearest-sink exit while it's in sight (D-180, D-182). Returns true when the agent switched
    /// exit (its route has then been chosen afresh).
    /// </summary>
    private bool ReviewExit(int i)
    {
        AgentStore a = _agents;
        if (!_tuning.ExitCrowd || a.Phase[i] != AgentPhase.Walk || a.QDevice[i] >= 0)
        {
            return false;
        }

        int current = a.Sink[i];
        if (a.Emitter[i] < 0)
        {
            return false;
        }

        SimEmitter emitter = _model.Emitters[a.Emitter[i]];
        if (!emitter.Nearest || emitter.Destinations.Length < 2 || current < 0 || _world.Sinks[current].Role != NavTargetRole.Exit
            || !ExitInSight(i, current))
        {
            return false;
        }

        SimProfile profile = _model.Profiles[a.Profile[i]];
        double sight = Sight(i);
        int best = current;
        double bestCost = double.PositiveInfinity;
        foreach ((int sink, double _) in emitter.Destinations)
        {
            double c = RoutePlanner.Choose(
                _routes, a.Surface[i], a.X[i], a.Y[i], sink, profile.Mobility, profile.Template, null, out _, out _, _wait,
                BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: sight);
            c += ExitCrowdFor(i, sink) - (sink == current ? StayBonusS : 0);
            if (c < bestCost)
            {
                bestCost = c;
                best = sink;
            }
        }

        if (best == current || !double.IsFinite(bestCost))
        {
            return false;
        }

        a.Sink[i] = best;
        Decide(i, _random);
        return true;
    }
}
