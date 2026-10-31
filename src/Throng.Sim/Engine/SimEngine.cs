using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// The movement engine (M2, round 006): fixed-step social force over the navigation world. Each step emits new
/// agents, works out every agent's acceleration from its flow field, its neighbours, walls and the keep-side bias,
/// moves it (crossing onto stairs, escalators and floors through portals), then removes arrivals and agents that
/// have been stuck too long (D-106).
/// </summary>
/// <remarks>
/// Round 007 adds the connector-end rules (D-112): a keep-clear run-off in front of every one-way exit, a short
/// walk straight out after stepping off, and a boarding hold while an escalator's own exit landing is full; and
/// deferral (D-113): belts costed per agent, waits from measured boardings and the queue at the entry, and a
/// re-choice for people queueing behind a long wait.
/// </remarks>
/// <remarks>
/// Single-threaded and deterministic: the same model and seed give the same run. The step allocates nothing once
/// the arrays have grown to the peak agent count. Not thread-safe; <see cref="SimRunner"/> owns it on its thread.
/// </remarks>
public sealed partial class SimEngine
{
    /// <summary>Floats per agent in a snapshot: x, y, z, heading, r, g, b, radius, height, stuck, stress (D-145).</summary>
    public const int FloatsPerAgent = 11;

    /// <summary>
    /// Below this share of their own pace a person counts as held up (D-145): of the desired speed before D-191, of
    /// progress along the route since.
    /// </summary>
    public const double StressSlowFraction = 0.5;

    /// <summary>Smoothing time for the progress pace, s (D-191): long enough that jiggling back and forth averages out.</summary>
    public const double PaceTauS = 2.0;

    /// <summary>A change in route cost bigger than this in one step is a new target, not progress, s (D-191).</summary>
    public const double PaceJumpS = 3.0;

    /// <summary>How fast held-up time drains in free flow, relative to real time (D-145).</summary>
    public const double StressDecay = 0.5;

    /// <summary>Seconds without progress before an agent is flagged, and before it is removed (D-106).</summary>
    public const double StuckFlagS = 30, StuckRemoveS = 120;

    // Social force (round 006 design).
    private const double Tau = 0.5, AgentA = 2.1, AgentB = 0.3, Range = 2.5, Lambda = 0.35;
    private const double WallA = 3.0, WallB = 0.2, WallRange = 1.0, WallOffNearPortal = 1.0;
    private const double KeepSideAccel = 0.8, OncomingAhead = 4.0, OncomingLateral = 1.5, KeepSideOffS = 2.0;

    // Lanes at two-way connectors (D-125): constant keep-side push on stairs and ramps, and within this range of a
    // two-way end.
    // r007l (D-134): approach range 6 m (was 4), so people line up on their side before the entry, not at it.
    private const double KeepSideLaneAccel = 1.5, KeepSideLaneRange = 6.0;

    // Landings on stairs and ramps (D-134): a stronger lane push so people turn wide on their own side rather than
    // cutting the corner across the other stream.
    // r007m (D-135): back to 1.0 (1.5 swung people into each other through the turn); people going the same way on a
    // landing push each other at this share of the social force so they can squeeze past rather than lock up.
    private const double LandingLaneFactor = 1.0, LandingSocialFactor = 0.3;

    // Stall escape (D-136): after this long without progress, a person drops the keep-side push, and pairs where
    // either one is stalled push each other at this share, so two people locked face to face can slip past.
    private const double StallS = 4.0, StallSocialFactor = 0.3;

    /// <summary>Two-way stair ends (D-300): lane run-in / walk-out length, the range it applies in, and the walk-out time cap.</summary>
    private const double StairEndLaneM = 2.0, StairEndRangeM = 8.0, StairEndOffS = 4.0;

    // Landing carry (D-137): after stepping from a flight onto a landing, keep going straight this far before turning,
    // so people don't cut the inner corner.
    private const double LandingCarryM = 0.6;

    /// <summary>Radius within which neighbours count towards local density for crowd stress, m (D-194).</summary>
    public const double CrowdRadiusM = 1.0;

    // Counterflow gate on those lanes (D-132): full lanes once the minority direction is this share of a stair's or
    // ramp's users, none with one-way flow; the factor is smoothed over this time so it doesn't flicker.
    private const double CounterflowFull = 0.2, LaneFactorTauS = 2.0;

    // Escalator boarding-rate seed as a share of the cap (D-133): about what D-128's comb spacing reaches in a crowd.
    private const double BoardingRateSeed = 0.8;
    private const double MaxAccel = 6.0, MaxSpeedFactor = 1.3, LevelBand = 0.8, MaxCorrection = 0.1;
    private const double ProgressS = 0.3;
    private const int MaxPendingPerRule = 500, SpawnTries = 6;

    // Connector ends (D-112, D-119): run-off depth and push, walk-clear time, landing depth, density of slow people
    // that counts as a jam, and the speed below which someone on the landing counts as slow.
    private const double RunOffDepth = 2.5, RunOffAccel = 4.0, WalkClearS = 1.5, LandingDepth = 1.2, JamDensity = 1.5;
    private const double JamSlowSpeed = 0.3;

    // Deferral (D-113): queue radius at an entry, boarding-rate smoothing, review interval, wait that prompts a
    // re-choice, the bonus for staying put, and walkers' speed on a belt as a share of their own.
    private const double QueueRadius = 5.0, RateTauS = 30, ReviewS = 3.0, BalkWaitS = 20, StayBonusS = 8;
    private const double WalkOnBeltFactor = 0.6;

    // Boarding spacing (D-128): people on the same metered belt, or within this range of its entry and heading on,
    // push each other this much less, so a crowd can close up two abreast at step spacing and board near the cap.
    // r007j (D-131): 0.3 and 1.5 m took the saturated pair from 21% to 56% of the cap; pushed further to 0.2 and 2 m.
    private const double CombRange = 2.0, CombSocialFactor = 0.2;

    private readonly SimModel _model;
    private readonly NavRoutes _routes;
    private readonly NavWorld _world;
    private readonly AgentStore _agents = new();
    private readonly SpatialHash _hash;
    private readonly double _dt;
    private readonly double[] _nextArrival;
    private readonly int[] _pending;
    private readonly DeterministicRandom[] _ruleRandom;
    private readonly double[] _tokens;
    private readonly double[] _wait;
    private readonly int[] _load;

    // Per connector (D-132, D-133): users going up and down, slow queuers near a stair entry, and the lane factor.
    private readonly int[] _up, _down, _slowNear;
    private readonly double[] _laneFactor;
    private readonly int[] _near, _far, _boarded;
    private readonly long[] _boardedTotal;
    private readonly bool[] _metered;
    private readonly double[][] _curveMinutes, _curveMultipliers;
    private readonly double[] _emitClock;
    private readonly double[] _rate;
    private readonly bool[] _jammed;
    private readonly int[][] _runOffs;
    private readonly int[] _runOffCount, _jamCount;
    private readonly bool[] _end0Open, _end1Open;
    private readonly int _reviewTicks;
    private readonly SimTuning _tuning;
    private readonly List<StuckRecord> _stuckLog = [];
    private readonly List<int> _justSpawned = [];
    private readonly List<(int Index, bool Arrived)> _removals = [];
    private DeterministicRandom _random;
    private double[] _dx = new double[256], _dy = new double[256];
    private int[] _group = new int[256];
    private long _tick, _spawned, _arrived, _stuckRemoved;
    private double _journeySum;
    private int _stuckNow;

    /// <summary>Creates an engine at time zero.</summary>
    /// <param name="model">The compiled model.</param>
    /// <param name="tuning">Rule switches; null for the defaults (everything on).</param>
    public SimEngine(SimModel model, SimTuning? tuning = null)
    {
        _tuning = tuning ?? SimTuning.Default;
        _model = model;
        _routes = model.Routes;
        _world = model.World;
        _dt = model.TimeStepS;
        _hash = new SpatialHash(_world.Bounds, Range);
        _nextArrival = new double[model.Emitters.Count];
        _pending = new int[model.Emitters.Count];
        _ruleRandom = new DeterministicRandom[model.Emitters.Count];
        _tokens = new double[_world.Connectors.Count];
        _wait = new double[_world.Connectors.Count];
        _load = new int[_world.Connectors.Count];
        _near = new int[_world.Connectors.Count];
        _far = new int[_world.Connectors.Count];
        _up = new int[_world.Connectors.Count];
        _down = new int[_world.Connectors.Count];
        _slowNear = new int[_world.Connectors.Count];
        _laneFactor = new double[_world.Connectors.Count];
        _boarded = new int[_world.Connectors.Count];
        _boardedTotal = new long[_world.Connectors.Count];
        _metered = new bool[_world.Connectors.Count];
        for (int k = 0; k < _metered.Length; k++)
        {
            _metered[k] = _world.Connectors[k].ThroughputPerMin > 0;
        }

        _emitClock = new double[model.Emitters.Count];
        _curveMinutes = new double[model.Emitters.Count][];
        _curveMultipliers = new double[model.Emitters.Count][];
        for (int e = 0; e < model.Emitters.Count; e++)
        {
            (_curveMinutes[e], _curveMultipliers[e]) = RateCurves.Compile(model.Emitters[e].Rule.RateCurve);
        }
        _rate = new double[_world.Connectors.Count];
        _jammed = new bool[_world.Connectors.Count];
        _runOffCount = new int[_world.Portals.Count];
        _jamCount = new int[_world.Portals.Count];
        _runOffs = BuildRunOffs(_world, _jamCount);
        _end0Open = new bool[_world.Surfaces.Count];
        _end1Open = new bool[_world.Surfaces.Count];
        MarkOpenEnds();
        InitCrowd();
        InitTrains();
        InitLifts();
        _reviewTicks = Math.Max(1, (int)Math.Round(ReviewS / _dt));
        _random = new DeterministicRandom(0);
        int lanes = 0, lines = 0;
        foreach (SimDevice device in model.Devices)
        {
            lanes += device.Lanes;
            lines += device.Slots.Length;
        }

        _laneDir = new sbyte[lanes];
        _laneServing = new bool[lanes];
        _laneThrough = new int[lanes];
        _laneThroughLeft = new float[lanes];
        _laneThroughLeftNext = new float[lanes];
        _laneClosing = new bool[lanes];
        _lineCount = new int[lines];
        _waitJoin = new int[model.Devices.Count * 2];
        _queueWaitSum = new double[model.Devices.Count];
        _queueWaitCount = new long[model.Devices.Count];
        Reset();
    }

    /// <summary>The compiled model.</summary>
    public SimModel Model => _model;

    /// <summary>Agent state (read by tests and the snapshot writer).</summary>
    public AgentStore Agents => _agents;

    /// <summary>Agents removed as stuck since the start, with where they were (diagnostics).</summary>
    public IReadOnlyList<StuckRecord> StuckLog => _stuckLog;

    /// <summary>Simulated seconds since the start.</summary>
    public double TimeS => _tick * _dt;

    /// <summary>People boarded per minute at a connector's metered entries since the start (D-128 diagnostics).</summary>
    /// <param name="connector">Index into the world's connectors.</param>
    public double BoardingsPerMinute(int connector) =>
        TimeS > 0 && connector >= 0 && connector < _boardedTotal.Length ? _boardedTotal[connector] * 60.0 / TimeS : 0;

    /// <summary>True once a fixed-duration run has reached its end (D-103).</summary>
    public bool IsFinished => _model.DurationS is { } duration && TimeS >= duration - 1e-9;

    /// <summary>Current counters.</summary>
    public SimStats Stats
    {
        get
        {
            int waiting = 0;
            foreach (int p in _pending)
            {
                waiting += p;
            }

            int stressed = 0, queuing = 0, atLifts = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents.Stress[i] >= StressedLevel)
                {
                    stressed++;
                }

                if (_agents.Phase[i] is AgentPhase.Queue or AgentPhase.Service or AgentPhase.WaitJoin)
                {
                    queuing++;
                }
                else if (_agents.Phase[i] is AgentPhase.LiftWait or AgentPhase.LiftRide)
                {
                    atLifts++;
                }
            }

            return new SimStats(TimeS, _agents.Count, _spawned, _arrived, _arrived > 0 ? _journeySum / _arrived : 0, _stuckNow, _stuckRemoved, waiting, stressed, queuing, _gaveUp, _leftBehind, atLifts);
        }
    }

    /// <summary>Back to time zero with no agents and fresh random streams (same seed, same run).</summary>
    public void Reset()
    {
        _agents.Clear();
        _density?.Clear();
        ResetResults();
        _stuckLog.Clear();
        _tick = _spawned = _arrived = _stuckRemoved = 0;
        _journeySum = 0;
        _stuckNow = 0;
        ulong seed = (ulong)(uint)_model.Seed;
        _random = new DeterministicRandom(DeterministicRandom.DeriveSeed(seed, 0));
        for (int e = 0; e < _model.Emitters.Count; e++)
        {
            _ruleRandom[e] = new DeterministicRandom(DeterministicRandom.DeriveSeed(seed, 1000 + (ulong)e));
            _pending[e] = 0;
            _emitClock[e] = 0;
            EmissionRule rule = _model.Emitters[e].Rule;
            double interval = 60.0 / Math.Max(1e-6, rule.RatePerMinute);

            // Constant: a random phase within the first interval; Poisson: an exponential first gap.
            _nextArrival[e] = rule.Pattern == EmissionPattern.Constant
                ? interval * _ruleRandom[e].NextDouble()
                : _ruleRandom[e].NextExponential(interval);
        }

        Array.Fill(_tokens, 1.0);
        Array.Clear(_wait);
        Array.Clear(_boarded);
        Array.Clear(_boardedTotal);
        Array.Clear(_jammed);
        Array.Clear(_laneFactor);
        for (int k = 0; k < _rate.Length; k++)
        {
            // With comb spacing (D-128) crowds board near the cap, so escalators don't start out looking slow (D-133).
            // Stairs and ramps (D-181) start from the same share of their capacity.
            NavConnector connector = _world.Connectors[k];
            _rate[k] = BoardingRateSeed * (_metered[k] ? connector.ThroughputPerMin / 60.0 : connector.CapacityPerSecond);
        }

        Array.Clear(_onFlight);
        Array.Clear(_exitCount);
        Array.Clear(_exitWait);

        ResetDevices();
        ResetTrains();
        ResetLifts();
    }

    /// <summary>Advances one fixed step.</summary>
    public void Step()
    {
        double t1 = TimeS + _dt;
        RefillTokens();
        CountArrivals();
        UpdateTrains();
        UpdateLifts();

        UpdateWaits();
        _hash.Build(_agents.X, _agents.Y, _agents.Count);
        SpawnPending();

        int n = _agents.Count;
        for (int i = 0; i < n; i++)
        {
            NavSurface s = _world.Surfaces[_agents.Surface[i]];
            _agents.Z[i] = _agents.Phase[i] == AgentPhase.LiftRide && _agents.LiftCar[i] >= 0
                ? _carZ[_agents.LiftCar[i]]
                : s.ZAt(_agents.X[i], _agents.Y[i]);
        }

        _hash.Build(_agents.X, _agents.Y, n);
        if (_dx.Length < n)
        {
            _dx = new double[n * 2];
            _dy = new double[n * 2];
            _group = new int[n * 2];
        }

        for (int i = 0; i < n; i++)
        {
            Review(i);
        }

        // Journeys (D-149–D-153): lanes first, then each person's step (joining, service, dwell, moving on).
        UpdateDevices();
        for (int i = 0; i < n; i++)
        {
            UpdateJourney(i);
        }

        for (int i = 0; i < n; i++)
        {
            _group[i] = BoardingGroup(i);
        }

        for (int i = 0; i < n; i++)
        {
            Plan(i);
        }

        _removals.Clear();
        _stuckNow = 0;
        for (int i = 0; i < n; i++)
        {
            Move(i);
            AfterMove(i);
        }

        for (int k = _removals.Count - 1; k >= 0; k--)
        {
            (int index, bool arrived) = _removals[k];
            ReleaseDevice(index);
            RecordJourneyEnd(index, arrived, t1);
            if (arrived)
            {
                _arrived++;
                _journeySum += t1 - _agents.SpawnTime[index];
            }
            else
            {
                _stuckRemoved++;
                _stuckLog.Add(new StuckRecord(t1, _agents.Surface[index], _agents.X[index], _agents.Y[index], _agents.Mode[index], _agents.Target[index]));
            }

            _agents.RemoveAt(index);
        }

        _tick++;
        SampleDensity();
        RecordResults();
    }

    /// <summary>
    /// Writes every agent's render values (<see cref="FloatsPerAgent"/> floats each) into <paramref name="buffer"/>,
    /// growing it when needed, and returns the agent count.
    /// </summary>
    public int WriteSnapshot(ref float[] buffer)
    {
        int n = _agents.Count;
        if (buffer.Length < n * FloatsPerAgent)
        {
            buffer = new float[Math.Max(n * FloatsPerAgent * 2, 64 * FloatsPerAgent)];
        }

        for (int i = 0; i < n; i++)
        {
            int o = i * FloatsPerAgent;
            SimProfile profile = _model.Profiles[_agents.Profile[i]];
            buffer[o] = (float)_agents.X[i];
            buffer[o + 1] = (float)_agents.Y[i];
            buffer[o + 2] = _agents.Phase[i] == AgentPhase.LiftRide && _agents.LiftCar[i] >= 0
                ? (float)_carZ[_agents.LiftCar[i]]
                : (float)_world.Surfaces[_agents.Surface[i]].ZAt(_agents.X[i], _agents.Y[i]);
            buffer[o + 3] = _agents.Heading[i];
            buffer[o + 4] = profile.R;
            buffer[o + 5] = profile.G;
            buffer[o + 6] = profile.B;
            buffer[o + 7] = _agents.Radius[i];
            buffer[o + 8] = _agents.Height[i];
            buffer[o + 9] = _agents.Since[i] >= StuckFlagS ? 1f : 0f;
            buffer[o + 10] = _agents.Stress[i];
        }

        return n;
    }

    /// <summary>
    /// Per-step connector state. Expected wait for route choice: stairs and ramps, people heading for or on it ÷
    /// its capacity (D-111); metered belts, people queued near the entry ÷ the measured boarding rate plus people
    /// still on their way ÷ the cap (D-113). Also counts people on every one-way exit landing and flags a
    /// connector as jammed while its own exit landing is full (D-112). Round 010a: stairs and ramps are measured like
    /// belts (D-181) and exits get a crowd wait (D-182).
    /// </summary>
    private void UpdateWaits()
    {
        Array.Clear(_onFlight);
        Array.Clear(_exitCount);
        Array.Clear(_load);
        Array.Clear(_near);
        Array.Clear(_far);
        Array.Clear(_up);
        Array.Clear(_down);
        Array.Clear(_slowNear);
        Array.Clear(_runOffCount);
        for (int i = 0; i < _agents.Count; i++)
        {
            double x = _agents.X[i], y = _agents.Y[i];
            int target = _agents.Target[i];
            if (target >= 0 && _agents.Mode[i] != AgentMode.Direct)
            {
                NavPortal portal = _world.Portals[target];
                _load[portal.Connector]++;
                if (_agents.Mode[i] == AgentMode.OnConnector)
                {
                    _onFlight[portal.Connector]++;
                }

                if (!_metered[portal.Connector])
                {
                    CountStream(i, portal, x, y);
                }

                if (_agents.Mode[i] == AgentMode.ToPortal)
                {
                    if (portal.DistanceTo(new Point2(x, y)) <= QueueRadius)
                    {
                        _near[portal.Connector]++;
                        if ((_agents.Vx[i] * _agents.Vx[i]) + (_agents.Vy[i] * _agents.Vy[i]) < JamSlowSpeed * JamSlowSpeed)
                        {
                            _slowNear[portal.Connector]++;
                        }
                    }
                    else
                    {
                        _far[portal.Connector]++;
                    }
                }
            }

            if (_tuning.ExitCrowd)
            {
                CountExitCrowd(i, x, y);
            }

            int[] runOffs = _runOffs[_agents.Surface[i]];
            for (int r = 0; r < runOffs.Length; r++)
            {
                // People who just stepped off here are the exit's own flow, not a jam (D-120).
                if (_agents.OffPortal[i] == runOffs[r] && TimeS - _agents.OffTime[i] < WalkClearS)
                {
                    continue;
                }

                // Only people barely moving count (D-119): riders walking clear are the normal flow, not a jam.
                if (RunOffLocal(_world.Portals[runOffs[r]], x, y, out double along, out double depth, out double length)
                    && depth >= 0 && depth <= LandingDepth && along >= 0 && along <= length
                    && (_agents.Vx[i] * _agents.Vx[i]) + (_agents.Vy[i] * _agents.Vy[i]) < JamSlowSpeed * JamSlowSpeed)
                {
                    _runOffCount[runOffs[r]]++;
                }
            }
        }

        for (int k = 0; k < _wait.Length; k++)
        {
            NavConnector connector = _world.Connectors[k];
            _jammed[k] = false;
            if (connector.IsLift)
            {
                // Lifts (round 013): people waiting ÷ the bank's places per round trip.
                _wait[k] = LiftWaitS(k);
                _laneFactor[k] = 0;
                _boarded[k] = 0;
                continue;
            }
            if (connector.ThroughputPerMin > 0 && _tuning.MeasuredWait)
            {
                double cap = connector.ThroughputPerMin / 60.0;
                if (_near[k] >= 2)
                {
                    // Only measured while there's a queue, so quiet periods don't read as slow boarding.
                    _rate[k] += ((_boarded[k] / _dt) - _rate[k]) * Math.Min(1.0, _dt / RateTauS);
                }

                _rate[k] = Math.Clamp(_rate[k], 0.1 * cap, cap);
                _wait[k] = (_near[k] / _rate[k]) + (_far[k] / cap);
            }
            else if (!_metered[k] && _tuning.StairMeasuredWait)
            {
                // Stairs and ramps measured like belts (D-181).
                _wait[k] = MeasuredStairWait(k);
            }
            else
            {
                // Stairs and ramps: load ÷ capacity. D-133's stalled-queue term (counting a jam twice) is off by
                // default since D-138, as it over-favoured escalators.
                double queued = _tuning.MeasuredWait && _tuning.StalledStairWait ? _slowNear[k] : 0;
                _wait[k] = (_load[k] + queued) / connector.CapacityPerSecond;
            }

            // Lanes only where there's counterflow (D-132): scale by the minority direction's share.
            int users = _up[k] + _down[k];
            double laneTarget = !_tuning.CounterflowLanes ? 1.0
                : users == 0 ? 0.0
                : Math.Min(1.0, Math.Min(_up[k], _down[k]) / (CounterflowFull * users));
            _laneFactor[k] += (laneTarget - _laneFactor[k]) * Math.Min(1.0, _dt / LaneFactorTauS);
            _boarded[k] = 0;
        }

        UpdateLiftPriorityPenalty();
        UpdateExitWaits();

        // Indexed loop: the step allocates nothing (no interface enumerator).
        for (int p = 0; p < _jamCount.Length; p++)
        {
            if (_jamCount[p] > 0 && _runOffCount[p] >= _jamCount[p])
            {
                _jammed[_world.Portals[p].Connector] = true;
            }
        }
    }

    /// <summary>
    /// Adds an agent to its stair's or ramp's up or down stream (D-132): riders by where their exit is against their
    /// current height; people about to board a two-way end by their planned exit's height against the entry's.
    /// </summary>
    private void CountStream(int i, NavPortal portal, double x, double y)
    {
        int k = portal.Connector;
        double from, to;
        if (_agents.Mode[i] == AgentMode.OnConnector)
        {
            from = _world.Surfaces[_agents.Surface[i]].ZAt(x, y);
            to = portal.ElevationM;
        }
        else
        {
            int exit = _agents.PlannedExit[i];
            if (_agents.Mode[i] != AgentMode.ToPortal || !IsTwoWay(portal) || exit < 0
                || _world.Portals[exit].Connector != k || portal.DistanceTo(new Point2(x, y)) > KeepSideLaneRange)
            {
                return;
            }

            from = portal.ElevationM;
            to = _world.Portals[exit].ElevationM;
        }

        if (to > from + 0.05)
        {
            _up[k]++;
        }
        else if (to < from - 0.05)
        {
            _down[k]++;
        }
    }

    /// <summary>Per floor surface: its one-way exits (exit but no entry), each with the head count that means a jam.</summary>
    private static int[][] BuildRunOffs(NavWorld world, int[] jamCount)
    {
        var lists = new List<int>?[world.Surfaces.Count];
        foreach (NavPortal portal in world.Portals)
        {
            // Lift landings have no run-off: people step out of the car onto the landing (round 013).
            if (!portal.AllowsExit || portal.AllowsEntry || portal.IsLiftLanding)
            {
                continue;
            }

            (lists[portal.Floor] ??= new List<int>()).Add(portal.Index);
            double length = (portal.B - portal.A).Length;
            jamCount[portal.Index] = Math.Max(2, (int)Math.Ceiling(length * LandingDepth * JamDensity));
        }

        var result = new int[lists.Length][];
        for (int s = 0; s < lists.Length; s++)
        {
            result[s] = lists[s]?.ToArray() ?? [];
        }

        return result;
    }

    /// <summary>Marks which piece ends open onto something (a joint or a floor end); the rest get a wall (D-118).</summary>
    private void MarkOpenEnds()
    {
        foreach (NavSurface s in _world.Surfaces)
        {
            if (s.Kind != NavSurfaceKind.Piece)
            {
                continue;
            }

            double length = s.Nx * s.CellU;
            foreach (PortalSegment seg in s.Segments)
            {
                Point2 mid = (seg.A + seg.B) * 0.5;
                s.ToLocal(mid.X, mid.Y, out double u, out _);
                if (u < 0.25)
                {
                    _end0Open[s.Index] = true;
                }

                if (u > length - 0.25)
                {
                    _end1Open[s.Index] = true;
                }
            }
        }
    }

    /// <summary>A point in a portal's run-off frame: distance along its edge from A, and depth out onto the floor.</summary>
    private static bool RunOffLocal(NavPortal portal, double x, double y, out double along, out double depth, out double length)
    {
        Point2 ab = portal.B - portal.A;
        length = ab.Length;
        if (length < 1e-9)
        {
            along = depth = 0;
            return false;
        }

        double rx = x - portal.A.X, ry = y - portal.A.Y;
        along = ((rx * ab.X) + (ry * ab.Y)) / length;
        depth = (rx * portal.Outward.X) + (ry * portal.Outward.Y);
        return true;
    }

    // ------------------------------------------------------------------ demand

    private void RefillTokens()
    {
        for (int k = 0; k < _tokens.Length; k++)
        {
            double rate = _world.Connectors[k].ThroughputPerMin;
            _tokens[k] = rate > 0 ? Math.Min(1.0, _tokens[k] + (rate / 60.0 * _dt)) : 1.0;
        }
    }

    /// <summary>
    /// Queues every arrival due by the end of this step (D-099). Each rule keeps its own clock that runs at the rate
    /// curve's multiplier (D-129), and arrivals are spaced on that clock at the rule's base rate: a time change, so
    /// constant spacing stays even and Poisson arrivals stay Poisson at the curve's rate.
    /// </summary>
    private void CountArrivals()
    {
        double minute = (TimeS + (0.5 * _dt)) / 60.0;
        for (int e = 0; e < _model.Emitters.Count; e++)
        {
            if (_model.Emitters[e].TrainService >= 0)
            {
                // A train door's rules only say who gets off; the service releases them at arrivals (D-184).
                continue;
            }

            EmissionRule rule = _model.Emitters[e].Rule;
            double interval = 60.0 / Math.Max(1e-6, rule.RatePerMinute);
            _emitClock[e] += _dt * Math.Max(0.0, RateCurves.MultiplierAt(_curveMinutes[e], _curveMultipliers[e], minute));
            double until = _emitClock[e];
            while (_nextArrival[e] < until)
            {
                if (_pending[e] < MaxPendingPerRule)
                {
                    _pending[e]++;
                }

                _nextArrival[e] += rule.Pattern == EmissionPattern.Constant ? interval : _ruleRandom[e].NextExponential(interval);
            }
        }
    }

    private void SpawnPending()
    {
        _justSpawned.Clear();
        for (int e = 0; e < _model.Emitters.Count; e++)
        {
            while (_pending[e] > 0 && TrySpawn(e))
            {
                _pending[e]--;
            }
        }
    }

    /// <summary>Places one agent on a random free walkable cell of the zone; false when the zone is too crowded.</summary>
    private bool TrySpawn(int e)
    {
        SimEmitter emitter = _model.Emitters[e];
        DeterministicRandom random = _ruleRandom[e];
        SimMixLine line = PickLine(emitter, random);
        SimProfile profile = _model.Profiles[line.Profile];
        AgentProfileTemplate template = profile.Template;
        NavSurface floor = _world.Surfaces[emitter.Floor];
        double radius = Math.Clamp(template.RadiusM, 0.1, 1.0);

        for (int attempt = 0; attempt < SpawnTries; attempt++)
        {
            Point2 c = floor.CellCentre(emitter.Cells[random.NextInt(emitter.Cells.Length)]);
            double x = c.X + ((random.NextDouble() - 0.5) * floor.CellU * 0.8);
            double y = c.Y + ((random.NextDouble() - 0.5) * floor.CellV * 0.8);
            if (!floor.IsWalkable(x, y) || Overlaps(x, y, floor.Z0, radius))
            {
                continue;
            }

            int i = _agents.Add();
            _agents.X[i] = x;
            _agents.Y[i] = y;
            _agents.Z[i] = floor.Z0;
            _agents.Surface[i] = emitter.Floor;
            _agents.Profile[i] = line.Profile;
            _agents.Emitter[i] = e;
            _agents.Journey[i] = line.Journey;
            _agents.Tags[i] = SpawnTags(line, random);
            _agents.V0[i] = (float)Math.Clamp(DistributionSampler.Sample(template.Speed, random), 0.3, 3.0);
            _agents.Radius[i] = (float)radius;
            _agents.Height[i] = (float)Math.Clamp(template.HeightM, 0.5, 2.5);
            _agents.Walks[i] = random.NextDouble() < template.EscalatorWalkProbability;
            _agents.SpawnTime[i] = TimeS;
            _agents.ZoneBits[i] = ZoneBitsAt(emitter.Floor, x, y);
            _agents.Heading[i] = (float)(random.NextDouble() * 2 * Math.PI);
            StartJourney(i, random);
            _justSpawned.Add(i);
            _spawned++;
            return true;
        }

        return false;
    }

    private int PickSink(int i, SimEmitter emitter, SimProfile profile, DeterministicRandom random)
    {
        double x = _agents.X[i], y = _agents.Y[i];
        (int Sink, double Weight)[] options = emitter.Destinations;
        if (emitter.Nearest)
        {
            // The quickest sink with a little per-agent noise (D-096), plus each exit's crowd wait in sight (D-182).
            int best = options[0].Sink;
            double bestCost = double.PositiveInfinity;
            double sight = Sight(i);
            foreach ((int sink, double _) in options)
            {
                double c = RoutePlanner.Choose(_routes, _agents.Surface[i], x, y, sink, profile.Mobility, profile.Template, random, out _, out _, _wait, BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: sight, priorityPenaltyS: _liftPriorityS, barredConnector: Barred(i))
                    + ExitCrowdFor(i, sink);
                if (c < bestCost)
                {
                    bestCost = c;
                    best = sink;
                }
            }

            return best;
        }

        double total = 0;
        foreach ((int _, double w) in options)
        {
            total += w;
        }

        double pick = random.NextDouble() * total;
        foreach ((int sink, double w) in options)
        {
            pick -= w;
            if (pick < 0)
            {
                return sink;
            }
        }

        return options[^1].Sink;
    }

    private bool Overlaps(double x, double y, double z, double radius)
    {
        _hash.CellOf(x, y, out int cx, out int cy);
        ReadOnlySpan<int> items = _hash.Items;
        for (int row = cy - 1; row <= cy + 1; row++)
        {
            (int start, int end) = _hash.RowRange(row, cx - 1, cx + 1);
            for (int k = start; k < end; k++)
            {
                if (Touches(items[k], x, y, z, radius))
                {
                    return true;
                }
            }
        }

        foreach (int j in _justSpawned)
        {
            if (Touches(j, x, y, z, radius))
            {
                return true;
            }
        }

        return false;
    }

    private bool Touches(int j, double x, double y, double z, double radius)
    {
        if (j >= _agents.Count || Math.Abs(_agents.Z[j] - z) >= LevelBand || _agents.Phase[j] == AgentPhase.LiftRide)
        {
            return false;
        }

        double dx = _agents.X[j] - x;
        double dy = _agents.Y[j] - y;
        double r = radius + _agents.Radius[j];
        return (dx * dx) + (dy * dy) < r * r;
    }

    // ------------------------------------------------------------------ routing

    /// <summary>Route choice on a floor (D-100): walk to the sink or head for a connector entry.</summary>
    private void Decide(int i, DeterministicRandom random)
    {
        int s = _agents.Surface[i];
        if (_world.Surfaces[s].Kind != NavSurfaceKind.Floor)
        {
            ChooseExit(i);
            return;
        }

        SimProfile profile = _model.Profiles[_agents.Profile[i]];
        double cost = RoutePlanner.Choose(
            _routes, s, _agents.X[i], _agents.Y[i], _agents.Sink[i], profile.Mobility, profile.Template, random, out bool direct, out RouteOption option, _wait,
            BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: Sight(i), priorityPenaltyS: _liftPriorityS, barredConnector: Barred(i));
        if (!double.IsFinite(cost) && _agents.LiftBar[i] >= 0)
        {
            // The lift it gave up on is its only way after all (D-254): lift the bar and go back to it.
            _agents.LiftBar[i] = -1;
            cost = RoutePlanner.Choose(
                _routes, s, _agents.X[i], _agents.Y[i], _agents.Sink[i], profile.Mobility, profile.Template, random, out direct, out option, _wait,
                BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: Sight(i), priorityPenaltyS: _liftPriorityS);
        }

        if (direct || !double.IsFinite(cost))
        {
            SetMode(i, AgentMode.Direct, -1);
        }
        else
        {
            SetMode(i, AgentMode.ToPortal, option.Entry);
            _agents.PlannedExit[i] = option.Exit;
        }
    }

    /// <summary>
    /// Looking again at a route choice: while approaching, whenever the chosen connector or exit is in sight (D-180);
    /// with <see cref="SimTuning.ApproachReview"/> off, D-113's light balk near a connector entry.
    /// </summary>
    private void Review(int i)
    {
        if (!_tuning.Balk)
        {
            return;
        }

        if (_tuning.ApproachReview)
        {
            ReviewApproach(i);
        }
        else
        {
            ReviewNearEntry(i);
        }
    }

    /// <summary>
    /// Light balk (D-113): every few seconds someone queueing near an entry with a long expected wait looks again,
    /// without noise and with a bonus for staying, and switches only when another way is clearly quicker.
    /// </summary>
    private void ReviewNearEntry(int i)
    {
        AgentStore a = _agents;
        int target = a.Target[i];
        if (!_tuning.Balk || a.Phase[i] != AgentPhase.Walk || a.Mode[i] != AgentMode.ToPortal || target < 0)
        {
            return;
        }

        // Staggered by spawn tick so the crowd doesn't all re-choose on the same step.
        long spawnTick = (long)Math.Round(a.SpawnTime[i] / _dt);
        if ((_tick + spawnTick) % _reviewTicks != 0)
        {
            return;
        }

        NavPortal portal = _world.Portals[target];
        int k = portal.Connector;
        if (_wait[k] < BalkWaitS || portal.DistanceTo(new Point2(a.X[i], a.Y[i])) > QueueRadius)
        {
            return;
        }

        SimProfile profile = _model.Profiles[a.Profile[i]];
        double cost = RoutePlanner.Choose(
            _routes, a.Surface[i], a.X[i], a.Y[i], a.Sink[i], profile.Mobility, profile.Template, null, out bool direct, out RouteOption option, _wait,
            BeltWalkSpeed(i), k, StayBonusS, _tuning.StairEffort, _tuning.StandingRest, Sight(i), priorityPenaltyS: _liftPriorityS, barredConnector: Barred(i));
        if (!double.IsFinite(cost))
        {
            return;
        }

        if (direct)
        {
            SetMode(i, AgentMode.Direct, -1);
        }
        else if (option.Connector != k)
        {
            SetMode(i, AgentMode.ToPortal, option.Entry);
            a.PlannedExit[i] = option.Exit;
        }
    }

    /// <summary>Walking speed relative to a belt for route choice: 0 for standers (D-113).</summary>
    private double BeltWalkSpeed(int i) => !_tuning.BeltCost ? -1 : _agents.Walks[i] ? WalkOnBeltFactor * _agents.V0[i] : 0;

    /// <summary>On a connector without a plan: step off wherever leaves the least time to the sink.</summary>
    private void ChooseExit(int i)
    {
        NavSurface surface = _world.Surfaces[_agents.Surface[i]];
        if (surface.ConnectorIndex < 0)
        {
            return;
        }

        MobilityClass mobility = _model.Profiles[_agents.Profile[i]].Mobility;
        int best = -1;
        double bestCost = double.PositiveInfinity;
        foreach (int p in _world.Connectors[surface.ConnectorIndex].Portals)
        {
            if (_routes.ConnSide[p] is not { } field || !field.Covers(surface.Index))
            {
                continue;
            }

            double c = field.Sample(surface, _agents.X[i], _agents.Y[i]) + _routes.Remaining(mobility, _agents.Sink[i], p);
            if (c < bestCost)
            {
                bestCost = c;
                best = p;
            }
        }

        SetMode(i, AgentMode.OnConnector, best);
    }

    private void SetMode(int i, AgentMode mode, int target)
    {
        _agents.Mode[i] = mode;
        _agents.Target[i] = target;
        _agents.Best[i] = float.PositiveInfinity;
        _agents.PrevCost[i] = float.PositiveInfinity;
        _agents.Since[i] = Math.Min(_agents.Since[i], (float)StuckFlagS * 0.5f);
    }

    private FlowField? FieldOf(int i)
    {
        int target = _agents.Target[i];
        return _agents.Mode[i] switch
        {
            AgentMode.Direct => _agents.Sink[i] >= 0 ? _routes.SinkFields[_agents.Sink[i]] : null,
            AgentMode.ToPortal => target >= 0 ? _routes.FloorSide[target] : null,
            _ => target >= 0 ? _routes.ConnSide[target] : null,
        };
    }

    // ------------------------------------------------------------------ forces

    /// <summary>New velocity and intended displacement for agent <paramref name="i"/> (positions unchanged).</summary>
    /// <summary>
    /// The metered connector whose boarding crowd agent <paramref name="i"/> is part of (D-128): riding it, or on the
    /// floor within <see cref="CombRange"/> of the entry it's heading for. −1 otherwise, or when the rule is off.
    /// </summary>
    private int BoardingGroup(int i)
    {
        if (!_tuning.CombSpacing)
        {
            return -1;
        }

        AgentStore a = _agents;
        NavSurface s = _world.Surfaces[a.Surface[i]];
        if (s.Motion == NavMotion.Belt)
        {
            return s.ConnectorIndex >= 0 && _metered[s.ConnectorIndex] ? s.ConnectorIndex : -1;
        }

        if (s.Kind == NavSurfaceKind.Floor && a.Mode[i] == AgentMode.ToPortal && a.Target[i] >= 0)
        {
            NavPortal portal = _world.Portals[a.Target[i]];
            if (_metered[portal.Connector] && portal.DistanceTo(new Point2(a.X[i], a.Y[i])) < CombRange)
            {
                return portal.Connector;
            }
        }

        return -1;
    }

    private void Plan(int i)
    {
        AgentStore a = _agents;
        if (a.Phase[i] == AgentPhase.LiftRide)
        {
            // Held in the car (round 013): it carries them; no walking, no forces.
            _dx[i] = _dy[i] = 0;
            a.Vx[i] = a.Vy[i] = 0;
            return;
        }

        NavSurface s = _world.Surfaces[a.Surface[i]];
        FlowField? field = FieldOf(i);
        if (field is null || !field.Covers(s.Index))
        {
            if (s.Kind == NavSurfaceKind.Floor)
            {
                Decide(i, _random);
            }
            else
            {
                ChooseExit(i);
            }

            field = FieldOf(i);
        }

        double x = a.X[i], y = a.Y[i];
        double ex = 0, ey = 0;
        if (field is not null && field.Covers(s.Index))
        {
            float cost = field.Sample(s, x, y);
            double near = 1.5 * Math.Max(s.CellU, s.CellV) / NavWorld.NominalSpeed;
            if (s.Index == field.TargetSurface && cost <= near && field.TargetDirection.Length > 0.5)
            {
                Point2 push = field.PushThrough(x, y, a.Radius[i]);
                ex = push.X;
                ey = push.Y;
            }
            else if (field.Direction(s, x, y, out Point2 d))
            {
                ex = d.X;
                ey = d.Y;
            }
        }

        if (a.JointDx[i] != 0 || a.JointDy[i] != 0)
        {
            // Carry on into the landing a body length before turning (D-137); drop it once there, or if stuck.
            double gone = ((x - a.JointX[i]) * a.JointDx[i]) + ((y - a.JointY[i]) * a.JointDy[i]);
            if (!IsStairLanding(s) || gone >= a.JointDepth[i] || a.Since[i] >= StallS)
            {
                a.JointDx[i] = a.JointDy[i] = 0;
            }
            else
            {
                ex = a.JointDx[i];
                ey = a.JointDy[i];
            }
        }

        if (_tuning.StairEndLanes && s.Kind == NavSurfaceKind.Floor && a.Phase[i] == AgentPhase.Walk)
        {
            StairEndLane(i, x, y, ref ex, ref ey);
        }

        if (_tuning.WalkClear && s.Kind == NavSurfaceKind.Floor && a.OffPortal[i] >= 0 && TimeS - a.OffTime[i] < WalkClearS
            && !_world.Portals[a.OffPortal[i]].AllowsEntry)
        {
            // Walk clear of a one-way exit first (D-112), then follow the field. Not at two-way stair ends, where
            // straight out leads into the people waiting to get on (D-119).
            Point2 outward = _world.Portals[a.OffPortal[i]].Outward;
            ex = outward.X;
            ey = outward.Y;
        }

        // Queueing, service, walking through, stepping aside and dwelling steer to a point, not along the field.
        bool holding = a.Phase[i] != AgentPhase.Walk;
        if (holding && s.Kind == NavSurfaceKind.Floor)
        {
            SteerByPhase(i, x, y, ref ex, ref ey);
        }

        double v0 = a.V0[i];
        double vdx, vdy;
        if (s.Motion == NavMotion.Belt)
        {
            BeltDesired(i, s, v0, out vdx, out vdy, out ex, out ey);
        }
        else
        {
            double factor = SpeedFactor(i, s, ex, ey);
            vdx = ex * v0 * factor;
            vdy = ey * v0 * factor;
        }

        double ax = (vdx - a.Vx[i]) / Tau;
        double ay = (vdy - a.Vy[i]) / Tau;
        double cx = 0, cy = 0;
        int oncoming = 0, close = 0;

        _hash.CellOf(x, y, out int hx, out int hy);
        ReadOnlySpan<int> items = _hash.Items;
        double ri = a.Radius[i];
        double zi = a.Z[i];
        int gi = _group[i];
        bool onLanding = _tuning.LandingSqueeze && IsStairLanding(s);
        bool stalled = _tuning.StallEscape && a.Since[i] >= StallS;
        for (int row = hy - 1; row <= hy + 1; row++)
        {
            (int start, int end) = _hash.RowRange(row, hx - 1, hx + 1);
            for (int k = start; k < end; k++)
            {
                int j = items[k];
                if (j == i || Math.Abs(a.Z[j] - zi) >= LevelBand || a.Phase[j] == AgentPhase.LiftRide)
                {
                    continue;
                }

                double dx = x - a.X[j];
                double dy = y - a.Y[j];
                double d2 = (dx * dx) + (dy * dy);
                if (d2 > Range * Range)
                {
                    continue;
                }

                double d = Math.Sqrt(d2);
                if (d < CrowdRadiusM)
                {
                    close++;
                }

                if (d < 1e-6)
                {
                    // Exactly coincident: separate along an index-dependent axis so the result stays deterministic.
                    dx = i < j ? 1e-3 : -1e-3;
                    dy = 0;
                    d = 1e-3;
                }

                double nx = dx / d, ny = dy / d;
                double rij = ri + a.Radius[j];
                double magnitude = AgentA * Math.Exp((rij - d) / AgentB);
                if (gi >= 0 && _group[j] == gi)
                {
                    // Closing up at and on a metered belt (D-128); contact correction still stops overlaps.
                    magnitude *= CombSocialFactor;
                }
                else if (_tuning.StallEscape && (stalled || a.Since[j] >= StallS))
                {
                    // Either one stuck for a while (D-136): soften so they can get round each other.
                    magnitude *= StallSocialFactor;
                }
                else if (onLanding && a.Surface[j] == a.Surface[i] && (a.Vx[i] * a.Vx[j]) + (a.Vy[i] * a.Vy[j]) > 0)
                {
                    // Same way through a landing turn (D-135): squeeze past instead of jamming the corner.
                    magnitude *= LandingSocialFactor;
                }

                double cosPhi = -((nx * ex) + (ny * ey));
                double weight = ex == 0 && ey == 0 ? 1 : Lambda + ((1 - Lambda) * (1 + cosPhi) * 0.5);
                ax += nx * magnitude * weight;
                ay += ny * magnitude * weight;

                if (d < rij)
                {
                    cx += nx * (rij - d) * 0.5;
                    cy += ny * (rij - d) * 0.5;
                }

                // Oncoming: ahead within 4 m, roughly in line, walking towards us.
                double ahead = -((dx * ex) + (dy * ey));
                double lateral = Math.Abs((dx * ey) - (dy * ex));
                if (ahead > 0 && ahead < OncomingAhead && lateral < OncomingLateral && (a.Vx[j] * ex) + (a.Vy[j] * ey) < -0.2)
                {
                    oncoming++;
                }
            }
        }

        // Local density, this person included, for crowd stress (D-194).
        a.Crowd[i] = (float)((close + 1) / (Math.PI * CrowdRadiusM * CrowdRadiusM));

        if (_model.KeepSide != KeepSide.None && s.Motion != NavMotion.Belt && (ex != 0 || ey != 0) && !stalled && !holding)
        {
            // Left of the walking direction is (−ey, ex).
            double sign = _model.KeepSide == KeepSide.Left ? 1 : -1;
            double k = KeepSideStrength(i, s, x, y, oncoming) * sign;
            ax += -ey * k;
            ay += ex * k;
        }

        WallForce(i, s, x, y, ri, ref ax, ref ay);
        if (_tuning.RunOffs)
        {
            RunOffForce(i, s, x, y, ri, ref ax, ref ay);
        }

        double accel = Math.Sqrt((ax * ax) + (ay * ay));
        if (accel > MaxAccel)
        {
            ax *= MaxAccel / accel;
            ay *= MaxAccel / accel;
        }

        double vx = a.Vx[i] + (ax * _dt);
        double vy = a.Vy[i] + (ay * _dt);
        double speed = Math.Sqrt((vx * vx) + (vy * vy));
        double limit = MaxSpeedFactor * Math.Max(0.3, v0);
        if (speed > limit)
        {
            vx *= limit / speed;
            vy *= limit / speed;
        }

        a.Vx[i] = vx;
        a.Vy[i] = vy;

        double correction = Math.Sqrt((cx * cx) + (cy * cy));
        if (correction > MaxCorrection)
        {
            cx *= MaxCorrection / correction;
            cy *= MaxCorrection / correction;
        }

        Point2 belt = s.BeltVelocity;
        _dx[i] = ((vx + belt.X) * _dt) + cx;
        _dy[i] = ((vy + belt.Y) * _dt) + cy;
    }

    /// <summary>
    /// Keep-side acceleration (D-105, D-124, D-125, D-132). Two-way connectors are where lanes matter most: on a stair
    /// or ramp, approaching a two-way end and just after stepping off one, people keep to their side, so up and down
    /// streams pass instead of meeting head-on. The lane push scales with the connector's counterflow (D-132): with
    /// everyone going one way it drops back to the oncoming-only nudge and people use the full width. At one-way ends (escalators) there's no counterflow, so
    /// the bias fades out near the entry and people funnel in. Elsewhere it's the usual oncoming-only nudge, faded
    /// when barely moving (sidestepping in a jam only presses into neighbours).
    /// </summary>
    private double KeepSideStrength(int i, NavSurface s, double x, double y, int oncoming)
    {
        AgentStore a = _agents;
        double nudge = KeepSideAccel * Math.Min(1.0, oncoming / 2.0);
        if (!_tuning.KeepSideFade)
        {
            return nudge;
        }

        if (s.Kind == NavSurfaceKind.Piece)
        {
            if (s.Motion is NavMotion.Stair or NavMotion.Ramp)
            {
                return Lane(s.ConnectorIndex, nudge);
            }

            // A landing (level piece) of a stair or ramp: hug your side through the turn (D-134).
            return IsStairLanding(s) ? LandingLaneFactor * Lane(s.ConnectorIndex, nudge) : nudge;
        }

        if (a.OffPortal[i] >= 0 && TimeS - a.OffTime[i] < KeepSideOffS)
        {
            NavPortal off = _world.Portals[a.OffPortal[i]];
            return IsTwoWay(off) ? Lane(off.Connector, 0) : 0;
        }

        double speed = Math.Sqrt((a.Vx[i] * a.Vx[i]) + (a.Vy[i] * a.Vy[i]));
        double fade = Math.Clamp((speed - 0.2) / 0.5, 0, 1);
        int target = a.Target[i];
        if (target >= 0 && a.Mode[i] == AgentMode.ToPortal)
        {
            NavPortal portal = _world.Portals[target];
            double d = portal.DistanceTo(new Point2(x, y));
            if (IsTwoWay(portal))
            {
                // Line up on your own half of a two-way stair entry so people coming off have the other half.
                return d < KeepSideLaneRange ? Lane(portal.Connector, nudge) : nudge * fade;
            }

            fade *= Math.Clamp((d - 1.0) / 2.0, 0, 1);
        }

        return nudge * fade;
    }

    /// <summary>Lane push for a connector (D-132): from <paramref name="fallback"/> with no counterflow to the full lane.</summary>
    private double Lane(int connector, double fallback) =>
        connector < 0 ? KeepSideLaneAccel : fallback + ((KeepSideLaneAccel - fallback) * _laneFactor[connector]);

    /// <summary>True for a landing (level piece) of a stair or ramp (D-134, D-135).</summary>
    private bool IsStairLanding(NavSurface s) =>
        s.Kind == NavSurfaceKind.Piece && s.Motion == NavMotion.Walk && s.ConnectorIndex >= 0
        && _world.Connectors[s.ConnectorIndex].Kind is ConnectorKind.Stair or ConnectorKind.Ramp;

    /// <summary>
    /// Two-way stair and ramp ends (round 017b, D-300), so people getting on and off don't knot at the edge.
    /// Stepping off: walk straight out (along the end's outward direction) for <see cref="StairEndLaneM"/> before
    /// following the field, unless stalled or it's taking too long. Heading on: aim at a point on the line straight
    /// out from the middle of your own (keep-side) half of the end: up to <see cref="StairEndLaneM"/> out, less when
    /// already close or only a little to the side, so people merge into their lane at about 45° and walk the last
    /// stretch straight on rather than cutting in along the edge.
    /// </summary>
    private void StairEndLane(int i, double x, double y, ref double ex, ref double ey)
    {
        AgentStore a = _agents;
        if (a.Since[i] >= StallS)
        {
            return;
        }

        if (a.OffPortal[i] >= 0 && TimeS - a.OffTime[i] < StairEndOffS)
        {
            NavPortal off = _world.Portals[a.OffPortal[i]];
            if (IsTwoWay(off) && !off.IsLiftLanding && IsStairOrRamp(off.Connector))
            {
                Point2 mid = (off.A + off.B) * 0.5;
                double gone = ((x - mid.X) * off.Outward.X) + ((y - mid.Y) * off.Outward.Y);
                if (gone < StairEndLaneM)
                {
                    ex = off.Outward.X;
                    ey = off.Outward.Y;
                    return;
                }
            }
        }

        int target = a.Target[i];
        if (target < 0 || a.Mode[i] != AgentMode.ToPortal)
        {
            return;
        }

        NavPortal portal = _world.Portals[target];
        if (!IsTwoWay(portal) || portal.IsLiftLanding || !IsStairOrRamp(portal.Connector))
        {
            return;
        }

        // The middle of your half of the end: left or right of the way in (−outward) by a quarter of its width.
        Point2 along = portal.B - portal.A;
        double width = along.Length;
        Point2 centre = (portal.A + portal.B) * 0.5;
        double ix = -portal.Outward.X, iy = -portal.Outward.Y;
        double side = _model.KeepSide switch { KeepSide.Left => 1, KeepSide.Right => -1, _ => 0 };
        double lx = -iy * side * width * 0.25, ly = ix * side * width * 0.25;
        double laneX = centre.X + lx, laneY = centre.Y + ly;

        double rx = x - laneX, ry = y - laneY;
        double depth = (rx * portal.Outward.X) + (ry * portal.Outward.Y);
        double lateral = Math.Abs((rx * -portal.Outward.Y) + (ry * portal.Outward.X));
        if (depth < 0 || (depth * depth) + (lateral * lateral) > StairEndRangeM * StairEndRangeM)
        {
            return;
        }

        // Aim out along the lane by how far to the side you are (merge at ~45°), capped at the lane length and at
        // your own depth (never walk back out); close and lined up, aim at the edge itself.
        double aim = Math.Min(StairEndLaneM, Math.Min(lateral, depth));
        double tx = laneX + (portal.Outward.X * aim) - x, ty = laneY + (portal.Outward.Y * aim) - y;
        double m = Math.Sqrt((tx * tx) + (ty * ty));
        if (m < 0.2)
        {
            return;
        }

        ex = tx / m;
        ey = ty / m;
    }

    /// <summary>True for a stair or ramp connector (not a belt or a lift).</summary>
    private bool IsStairOrRamp(int connector) =>
        connector >= 0 && connector < _world.Connectors.Count && _world.Connectors[connector].Kind is ConnectorKind.Stair or ConnectorKind.Ramp;

    /// <summary>True for a connector end people both get on and off at (a two-way stair or ramp end).</summary>
    private static bool IsTwoWay(NavPortal portal) => portal.AllowsEntry && portal.AllowsExit;

    /// <summary>Speed multiplier for the surface and direction: stairs by profile (up/down), ramps uphill, Slow zones.</summary>
    private double SpeedFactor(int i, NavSurface s, double ex, double ey)
    {
        switch (s.Motion)
        {
            case NavMotion.Stair:
            {
                AgentProfileTemplate t = _model.Profiles[_agents.Profile[i]].Template;
                bool up = (ex * s.AxisU.X) + (ey * s.AxisU.Y) >= 0;
                return Math.Clamp(up ? t.StairUpFactor : t.StairDownFactor, 0.1, 1.5);
            }

            case NavMotion.Ramp:
                return (ex * s.AxisU.X) + (ey * s.AxisU.Y) > 0 ? NavCosts.RampUp : 1.0;

            default:
                if (s.Kind == NavSurfaceKind.Floor)
                {
                    int cell = s.CellAt(_agents.X[i], _agents.Y[i]);
                    return cell >= 0 ? 1.0 / s.Slowness[cell] : 1.0;
                }

                return 1.0;
        }
    }

    /// <summary>
    /// Escalators and travelators (D-107): standers ride on the stand side (the keep side; right when there's no
    /// keep side), walkers walk on the other side at 0.6 × their speed, both relative to the belt.
    /// </summary>
    private void BeltDesired(int i, NavSurface s, double v0, out double vdx, out double vdy, out double ex, out double ey)
    {
        double sign = s.BeltSpeed < 0 ? -1 : 1;
        double tx = s.AxisU.X * sign, ty = s.AxisU.Y * sign;
        double lx = -ty, ly = tx;
        s.ToLocal(_agents.X[i], _agents.Y[i], out _, out double v);
        double sideSign = (s.AxisV.X * lx) + (s.AxisV.Y * ly) >= 0 ? 1 : -1;
        double offset = (v - s.HalfWidth) * sideSign;
        bool standLeft = _model.KeepSide == KeepSide.Left;
        bool walks = _agents.Walks[i];
        double target = 0.5 * s.HalfWidth * (walks ? -1 : 1) * (standLeft ? 1 : -1);
        double lateral = Math.Clamp((target - offset) * 1.5, -0.4, 0.4);
        double along = walks ? WalkOnBeltFactor * v0 : 0;
        vdx = (tx * along) + (lx * lateral);
        vdy = (ty * along) + (ly * lateral);
        ex = tx;
        ey = ty;
    }

    /// <summary>Exponential push away from walls (off near the portal an agent is heading for, so it can get on).</summary>
    private void WallForce(int i, NavSurface s, double x, double y, double radius, ref double ax, ref double ay)
    {
        if (s.Kind == NavSurfaceKind.Floor)
        {
            if (_agents.Mode[i] == AgentMode.ToPortal && _agents.Target[i] >= 0 && !_world.Portals[_agents.Target[i]].IsLiftLanding
                && _world.Portals[_agents.Target[i]].DistanceTo(new Point2(x, y)) < WallOffNearPortal)
            {
                return;
            }

            if (!SampleWall(s, x, y, out double distance, out double gx, out double gy) || distance > WallRange)
            {
                return;
            }

            double g = Math.Sqrt((gx * gx) + (gy * gy));
            if (g < 1e-9)
            {
                return;
            }

            double push = WallA * Math.Exp((radius - distance) / WallB);
            double px = gx / g * push, py = gy / g * push;
            if (_tuning.LaneClear && _agents.Phase[i] is AgentPhase.Service or AgentPhase.Through or AgentPhase.Queue
                && LaneAxis(i, out _, out Point2 laneDir))
            {
                // In or at a gate lane (D-209): walls only push sideways, so the lane mouth's corners can't shove a
                // wide wheelchair back out of a lane it only just fits.
                double along = (px * laneDir.X) + (py * laneDir.Y);
                px -= laneDir.X * along;
                py -= laneDir.Y * along;
            }

            ax += px;
            ay += py;
            return;
        }

        // Pieces: balustrades along both long sides, and end edges that join nothing (D-118).
        s.ToLocal(x, y, out double u, out double v);
        double width = s.HalfWidth * 2;
        double toWall = Math.Min(v, width - v);
        if (toWall <= WallRange)
        {
            double dir = v < s.HalfWidth ? 1 : -1;
            double force = WallA * Math.Exp((radius - Math.Max(0, toWall)) / WallB);
            ax += s.AxisV.X * dir * force;
            ay += s.AxisV.Y * dir * force;
        }

        double length = s.Nx * s.CellU;
        if (!_end0Open[s.Index] && u < WallRange)
        {
            double force = WallA * Math.Exp((radius - Math.Max(0, u)) / WallB);
            ax += s.AxisU.X * force;
            ay += s.AxisU.Y * force;
        }

        if (!_end1Open[s.Index] && length - u < WallRange)
        {
            double force = WallA * Math.Exp((radius - Math.Max(0, length - u)) / WallB);
            ax -= s.AxisU.X * force;
            ay -= s.AxisU.Y * force;
        }
    }

    /// <summary>
    /// Keep-clear run-offs (D-112, D-122): a soft push straight out of the 2.5 m lane in front of every one-way exit
    /// for everyone except people who stepped off there. Only along the exit direction: a sideways push shoved people
    /// into whatever sits beside the exit (the demo's platform stair foot).
    /// </summary>
    private void RunOffForce(int i, NavSurface s, double x, double y, double radius, ref double ax, ref double ay)
    {
        int[] runOffs = _runOffs[s.Index];
        for (int r = 0; r < runOffs.Length; r++)
        {
            int p = runOffs[r];
            if (_agents.OffPortal[i] == p)
            {
                continue;
            }

            NavPortal portal = _world.Portals[p];
            if (!RunOffLocal(portal, x, y, out double along, out double depth, out double length)
                || depth < 0 || depth > RunOffDepth || along < -radius || along > length + radius)
            {
                continue;
            }

            // Soft (D-119): full strength on people standing in the lane, a fraction on people walking through.
            double speed = Math.Sqrt((_agents.Vx[i] * _agents.Vx[i]) + (_agents.Vy[i] * _agents.Vy[i]));
            double moving = Math.Clamp(1 - (speed / Math.Max(0.3, 0.8 * _agents.V0[i])), 0.15, 1);
            double push = RunOffAccel * (1 - (0.5 * depth / RunOffDepth)) * moving;
            ax += portal.Outward.X * push;
            ay += portal.Outward.Y * push;
        }
    }

    /// <summary>Bilinear wall distance and its plan gradient at a point (outside the grid counts as wall).</summary>
    private static bool SampleWall(NavSurface s, double x, double y, out double value, out double gx, out double gy)
    {
        s.ToLocal(x, y, out double u, out double v);
        double fu = (u / s.CellU) - 0.5;
        double fv = (v / s.CellV) - 0.5;
        int i0 = (int)Math.Floor(fu);
        int j0 = (int)Math.Floor(fv);
        double tx = fu - i0, ty = fv - j0;
        float[] w = s.WallDistance;
        double c00 = WallAt(s, w, i0, j0), c10 = WallAt(s, w, i0 + 1, j0);
        double c01 = WallAt(s, w, i0, j0 + 1), c11 = WallAt(s, w, i0 + 1, j0 + 1);
        value = (c00 * (1 - tx) * (1 - ty)) + (c10 * tx * (1 - ty)) + (c01 * (1 - tx) * ty) + (c11 * tx * ty);
        double du = (((c10 - c00) * (1 - ty)) + ((c11 - c01) * ty)) / s.CellU;
        double dv = (((c01 - c00) * (1 - tx)) + ((c11 - c10) * tx)) / s.CellV;
        gx = (s.AxisU.X * du) + (s.AxisV.X * dv);
        gy = (s.AxisU.Y * du) + (s.AxisV.Y * dv);
        return true;
    }

    private static double WallAt(NavSurface s, float[] w, int i, int j) =>
        i >= 0 && j >= 0 && i < s.Nx && j < s.Ny ? w[(j * s.Nx) + i] : 0;

    // ------------------------------------------------------------------ movement

    /// <summary>Applies the planned displacement: stay on the surface, cross a portal, or slide along the edge.</summary>
    private void Move(int i)
    {
        AgentStore a = _agents;
        if (a.Phase[i] == AgentPhase.LiftRide)
        {
            return;
        }

        NavSurface s = _world.Surfaces[a.Surface[i]];
        double nx = a.X[i] + _dx[i];
        double ny = a.Y[i] + _dy[i];
        if (s.IsWalkable(nx, ny))
        {
            a.X[i] = nx;
            a.Y[i] = ny;
        }
        else if (TryTransfer(i, s, nx, ny))
        {
            // Moved (or held at a metered entry).
        }
        else if (s.IsWalkable(nx, a.Y[i]))
        {
            a.X[i] = nx;
            a.Vy[i] *= 0.5;
        }
        else if (s.IsWalkable(a.X[i], ny))
        {
            a.Y[i] = ny;
            a.Vx[i] *= 0.5;
        }
        else
        {
            a.Vx[i] *= 0.5;
            a.Vy[i] *= 0.5;
        }

        double speed2 = (a.Vx[i] * a.Vx[i]) + (a.Vy[i] * a.Vy[i]);
        if (speed2 > 0.0025)
        {
            a.Heading[i] = (float)Math.Atan2(a.Vy[i], a.Vx[i]);
        }
    }

    /// <summary>
    /// Crosses a portal segment when the step ends just past it: on the floor only the planned entry; at an
    /// excluded level only for people already on that connector; escalator entries only with a step-loading token.
    /// </summary>
    private bool TryTransfer(int i, NavSurface s, double nx, double ny)
    {
        AgentStore a = _agents;
        foreach (PortalSegment seg in s.Segments)
        {
            if (!seg.Allowed)
            {
                continue;
            }

            if (seg.IsPassOver)
            {
                if (a.Mode[i] != AgentMode.OnConnector || a.Target[i] < 0 || _world.Portals[a.Target[i]].Connector != seg.Connector)
                {
                    continue;
                }
            }
            else if (s.Kind == NavSurfaceKind.Floor && (a.Mode[i] != AgentMode.ToPortal || seg.Portal != a.Target[i]))
            {
                continue;
            }

            Point2 ab = seg.B - seg.A;
            double length = ab.Length;
            if (length < 1e-9)
            {
                continue;
            }

            double rx = nx - seg.A.X, ry = ny - seg.A.Y;
            double t = ((rx * ab.X) + (ry * ab.Y)) / length;
            if (t < -0.3 || t > length + 0.3)
            {
                continue;
            }

            double dist = (rx * seg.Normal.X) + (ry * seg.Normal.Y);
            if (dist < -0.3 || dist > 0.6)
            {
                continue;
            }

            // Land just past the line and inside the edge's span (crossing at a corner slides in sideways), then a
            // little further while the far side's cell isn't walkable yet.
            double margin = Math.Min(0.05, length * 0.5);
            double along = Math.Clamp(t, margin, length - margin);
            double depth = Math.Max(dist, 0.02);
            double qx = seg.A.X + (ab.X / length * along) + (seg.Normal.X * depth);
            double qy = seg.A.Y + (ab.Y / length * along) + (seg.Normal.Y * depth);
            NavSurface other = _world.Surfaces[seg.Other];
            bool found = false;
            for (int k = 0; k < 4; k++)
            {
                if (other.IsWalkable(qx, qy))
                {
                    found = true;
                    break;
                }

                qx += seg.Normal.X * 0.1;
                qy += seg.Normal.Y * 0.1;
            }

            if (!found)
            {
                continue;
            }

            if (seg.IsCappedEntry)
            {
                if (_tokens[seg.Connector] < 1.0 || (_tuning.BoardingHold && _jammed[seg.Connector]))
                {
                    // Held at the comb until the step-loading cap allows another person on (D-107), and while the
                    // connector's own exit landing is full (D-112).
                    a.Vx[i] *= 0.5;
                    a.Vy[i] *= 0.5;
                    return true;
                }

                _tokens[seg.Connector] -= 1.0;
                _boarded[seg.Connector]++;
                _boardedTotal[seg.Connector]++;
            }
            else if (s.Kind == NavSurfaceKind.Floor && !seg.IsPassOver && !_metered[seg.Connector])
            {
                // Stepping onto a stair or ramp: its measured flow (D-181).
                _boarded[seg.Connector]++;
                _boardedTotal[seg.Connector]++;
            }

            // Stepping from a flight onto a landing (D-137): remember where and which way, to carry on before turning.
            a.JointDx[i] = a.JointDy[i] = 0;
            if ((_tuning.LandingCarry || _tuning.LandingWaypoints) && s.Motion is NavMotion.Stair or NavMotion.Ramp && IsStairLanding(_world.Surfaces[seg.Other]))
            {
                double mx = qx - a.X[i], my = qy - a.Y[i];
                double m = Math.Sqrt((mx * mx) + (my * my));
                if (m > 1e-6)
                {
                    a.JointX[i] = qx;
                    a.JointY[i] = qy;
                    a.JointDx[i] = mx / m;
                    a.JointDy[i] = my / m;
                    a.JointDepth[i] = (float)(_tuning.LandingWaypoints ? LandingCarryDepth(i, _world.Surfaces[seg.Other], qx, qy, mx / m, my / m) : LandingCarryM);
                }
            }

            a.X[i] = qx;
            a.Y[i] = qy;
            a.Surface[i] = seg.Other;
            OnTransfer(i, s, other, seg);
            return true;
        }

        return false;
    }

    private void OnTransfer(int i, NavSurface from, NavSurface to, PortalSegment seg)
    {
        if (seg.IsPassOver)
        {
            return;
        }

        if (to.Kind == NavSurfaceKind.Piece)
        {
            if (from.Kind == NavSurfaceKind.Floor)
            {
                _agents.OffPortal[i] = -1;
                int exit = _agents.PlannedExit[i];
                if (exit >= 0 && _world.Portals[exit].Connector == seg.Connector && _routes.ConnSide[exit] is not null)
                {
                    SetMode(i, AgentMode.OnConnector, exit);
                }
                else
                {
                    ChooseExit(i);
                }
            }

            return;
        }

        // Stepped off onto a served level: remember where (run-off and walk-clear rules, D-112), then choose again
        // from here (D-100).
        if (seg.Portal >= 0)
        {
            _agents.OffPortal[i] = seg.Portal;
            _agents.OffTime[i] = TimeS;
        }

        if (from.Motion == NavMotion.Belt)
        {
            // Step off with the belt's momentum (D-120): a stander leaves at belt speed, not from rest.
            Point2 belt = from.BeltVelocity;
            _agents.Vx[i] += belt.X;
            _agents.Vy[i] += belt.Y;
        }

        Decide(i, _random);
    }

    /// <summary>Arrival, progress and stuck bookkeeping after the move.</summary>
    private void AfterMove(int i)
    {
        AgentStore a = _agents;
        NavSurface s = _world.Surfaces[a.Surface[i]];
        int sink = a.Sink[i];
        if (a.Phase[i] == AgentPhase.Walk && s.Kind == NavSurfaceKind.Floor && sink >= 0 && _world.Sinks[sink].Floor == s.Index
            && _routes.SinkFields[sink].At(s.Index, s.CellAt(a.X[i], a.Y[i])) <= 0f
            && ReachedTarget(i))
        {
            _removals.Add((i, true));
            return;
        }

        if (a.Phase[i] != AgentPhase.Walk)
        {
            // Queueing (or waiting for a lane), being served, dwelling: waiting on purpose, not stuck (D-106).
            a.Since[i] = 0;
            a.Best[i] = float.PositiveInfinity;
            a.PrevCost[i] = float.PositiveInfinity;
            UpdateStress(i, s);
            return;
        }

        FlowField? field = FieldOf(i);
        float cost = field is not null && field.Covers(s.Index) ? field.Sample(s, a.X[i], a.Y[i]) : float.PositiveInfinity;
        UpdatePace(i, cost);
        if (cost < a.Best[i] - ProgressS)
        {
            a.Best[i] = cost;
            a.Since[i] = 0;
        }
        else
        {
            a.Since[i] += (float)_dt;
        }

        UpdateStress(i, s);
        if (a.Since[i] >= StuckRemoveS)
        {
            _removals.Add((i, false));
        }
        else if (a.Since[i] >= StuckFlagS)
        {
            _stuckNow++;
        }
    }
    /// <summary>
    /// Stress (D-145, BRIEF §8.4): held-up time builds while slower than <see cref="StressSlowFraction"/> of the
    /// desired speed (belt riders excepted), faster for people with low density tolerance, and drains at
    /// <see cref="StressDecay"/> × real time in free flow. Stress stays 0 until half the profile's patience has
    /// been spent held up, then rises linearly to 1 at full patience.
    /// </summary>
    private void UpdateStress(int i, NavSurface s)
    {
        AgentStore a = _agents;
        AgentProfileTemplate t = _model.Profiles[a.Profile[i]].Template;
        bool heldUp;
        if (s.Motion == NavMotion.Belt)
        {
            heldUp = false;
        }
        else if (_tuning.CrowdStress && a.Crowd[i] > CrowdStressDensity(t.DensityTolerance)
            && a.Phase[i] is not (AgentPhase.Service or AgentPhase.Through or AgentPhase.LiftRide))
        {
            // D-194: packed in, moving or not, past what this profile tolerates.
            heldUp = true;
        }
        else if (_tuning.ProgressStress)
        {
            // D-191: walking counts as held up when progress along the route is under half the agent's own pace
            // (jiggling on the spot makes none); standing in a queue or waiting for a lane always counts. Being
            // served, walking through, stepping aside and dwelling are not hold-ups (D-145, D-156).
            heldUp = a.Phase[i] switch
            {
                AgentPhase.Walk => a.Pace[i] < StressSlowFraction,
                AgentPhase.Queue or AgentPhase.WaitJoin or AgentPhase.LiftWait => true,
                _ => false,
            };
        }
        else
        {
            // Before D-191: instantaneous speed under half the desired speed.
            double speed2 = (a.Vx[i] * a.Vx[i]) + (a.Vy[i] * a.Vy[i]);
            double slow = StressSlowFraction * a.V0[i];
            heldUp = speed2 < slow * slow && (a.Phase[i] is AgentPhase.Walk or AgentPhase.Queue or AgentPhase.WaitJoin or AgentPhase.LiftWait);
        }

        double held = HeldStep(a.HeldS[i], heldUp, t.DensityTolerance, _dt);
        a.HeldS[i] = (float)held;
        a.Stress[i] = (float)(_tuning.EarlyStress ? StressFrom(held, t.PatienceS) : StressFromHalfPatience(held, t.PatienceS));
    }

    /// <summary>
    /// Progress pace (D-191): how fast the route cost to the current target falls, as a share of the agent's own free
    /// pace (desired speed ÷ nominal speed), smoothed over <see cref="PaceTauS"/>. A jump bigger than
    /// <see cref="PaceJumpS"/> in one step (a new target, a portal crossing) restarts the measure without counting.
    /// </summary>
    private void UpdatePace(int i, float cost)
    {
        AgentStore a = _agents;
        float prev = a.PrevCost[i];
        a.PrevCost[i] = cost;
        if (!float.IsFinite(cost) || !float.IsFinite(prev) || Math.Abs(prev - cost) > PaceJumpS)
        {
            return;
        }

        double free = Math.Max(0.1, a.V0[i] / NavWorld.NominalSpeed);
        double share = Math.Clamp((prev - cost) / _dt / free, -1.0, 1.5);
        a.Pace[i] += (float)((share - a.Pace[i]) * Math.Min(1.0, _dt / PaceTauS));
    }

    /// <summary>
    /// One step of held-up time (D-145): builds at (1.5 − density tolerance) × dt while held up (tolerance 0.5 is
    /// real time, 0 is 1.5×, 1 is 0.5×), otherwise drains at <see cref="StressDecay"/> × dt down to 0.
    /// </summary>
    public static double HeldStep(double heldS, bool heldUp, double densityTolerance, double dt) =>
        heldUp
            ? heldS + (dt * (1.5 - Math.Clamp(densityTolerance, 0, 1)))
            : Math.Max(0, heldS - (dt * StressDecay));

    /// <summary>
    /// Stress from held-up time (D-193): linear from 0 at the first second held up to 1 at full patience, so it shows
    /// as soon as people are held; STRESSED (≥ 0.5) is still half the patience.
    /// </summary>
    public static double StressFrom(double heldS, double patienceS) =>
        Math.Clamp(heldS / Math.Max(1.0, patienceS), 0, 1);

    /// <summary>Stress before D-193 (D-145): 0 until half the patience, then linear to 1 at full patience.</summary>
    public static double StressFromHalfPatience(double heldS, double patienceS)
    {
        double patience = Math.Max(1.0, patienceS);
        return Math.Clamp((heldS - (0.5 * patience)) / (0.5 * patience), 0, 1);
    }

    /// <summary>
    /// Density above which a person counts as held up (D-194), people per m²: 1 + 3 × density tolerance (tolerance 0.5
    /// = 2.5 p/m², about Fruin level of service E; event fans at 0.9 = 3.7).
    /// </summary>
    public static double CrowdStressDensity(double densityTolerance) => 1.0 + (3.0 * Math.Clamp(densityTolerance, 0, 1));

    /// <summary>
    /// How deep to carry straight into a stair landing before turning (D-192): people whose keep side is the outside
    /// of the turn go nearly to the far wall, people on the inside turn early, so the two streams stay on their own
    /// halves instead of cutting the corner. Straight-through landings and no keep side keep D-137's 0.6 m.
    /// </summary>
    private double LandingCarryDepth(int i, NavSurface landing, double qx, double qy, double dx, double dy)
    {
        double depth = 0;
        for (double step = 0.1; step <= 8.0; step += 0.1)
        {
            if (!landing.IsWalkable(qx + (dx * step), qy + (dy * step)))
            {
                break;
            }

            depth = step;
        }

        FlowField? field = FieldOf(i);
        if (_model.KeepSide == KeepSide.None || field is null || !field.Covers(landing.Index))
        {
            return LandingCarryM;
        }

        double probe = Math.Min(LandingCarryM, depth * 0.5);
        if (!field.Direction(landing, qx + (dx * probe), qy + (dy * probe), out Point2 f))
        {
            return LandingCarryM;
        }

        double cross = (dx * f.Y) - (dy * f.X);
        if (Math.Abs(cross) < 0.3)
        {
            return LandingCarryM;
        }

        bool turnLeft = cross > 0;
        bool outside = _model.KeepSide == KeepSide.Left ? !turnLeft : turnLeft;
        double room = Math.Max(LandingCarryM, depth - 0.5 - _agents.Radius[i]);
        return outside ? room : Math.Max(LandingCarryM, Math.Min(room, 0.35 * depth));
    }
}
