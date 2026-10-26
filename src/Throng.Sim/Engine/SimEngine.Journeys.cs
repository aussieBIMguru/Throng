using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// Journeys, tags and device queues (round 009a, D-149–D-153). Each agent works through its journey's steps: a
/// device step picks the device of the asked-for type with the lowest walk + expected queue wait (with the profile's
/// decisiveness noise on the walk), joins the shortest line on its side once close, waits to be served, then walks
/// through (gates) or steps aside (machines) and moves on; a zone step walks into the zone and, for dwell steps,
/// strolls about it for the drawn time. Steps are skipped when the agent's tags don't meet their conditions, and
/// completed steps can grant tags (D-150).
/// Round 009b adds give-up pathways (D-165), profile device preferences (D-166), the shared snake of a drawn queue
/// (D-163) and hold points for people waiting for a lane (D-167).
/// </summary>
/// <remarks>
/// Queue lines are per lane (server) and per side. A pass-through lane serves one side at a time: it takes the side
/// of whoever joins it while idle, and when people on the other side have no lane at all, the least busy lane closes
/// to new joiners, drains and then turns round (so gates work both ways without head-on collisions in a lane).
/// Line membership is kept as each agent's (device, line, slot); the engine's agents are swap-removed, so nothing
/// stores agent indices.
/// </remarks>
public sealed partial class SimEngine
{
    /// <summary>Stress at or above which a person counts as stressed in the status bar and diagnostics (D-156).</summary>
    public const double StressedLevel = 0.5;

    /// <summary>
    /// A person leaves (or won't join) a queue when its wait is over this many times the next-best device's walk +
    /// wait (D-152, accepted default).
    /// </summary>
    public const double BalkFactor = 1.5;

    // Joining: within this of the line's tail. Head: this close to the service point to be served. Arrive: slow down
    // within this of a slot; hold still within SlotHoldM.
    private const double JoinReachM = 2.0, HeadReachM = 0.5, ArriveM = 0.6, SlotHoldM = 0.15;

    // After service: step aside this far for this long (machines); give up walking through after this long (gates);
    // dwellers stroll at this share of their speed and pick a new spot this close to the last.
    private const double AsideM = 1.0, AsideS = 1.2, ThroughTimeoutS = 6.0, ThroughDoneM = 0.4, DwellSpeed = 0.5, DwellReachM = 0.5;

    private readonly sbyte[] _laneDir;
    private readonly bool[] _laneServing;
    private readonly int[] _laneThrough;
    private readonly bool[] _laneClosing;
    private readonly int[] _lineCount;
    private readonly int[] _waitJoin;
    private readonly double[] _queueWaitSum;
    private readonly long[] _queueWaitCount;
    private long _gaveUp;

    /// <summary>People who left the study because their patience ran out in a queue (D-165), since the start.</summary>
    public long GaveUp => _gaveUp;

    /// <summary>People in each device's queue lines right now (including whoever is being served).</summary>
    public int QueueLength(int device)
    {
        SimDevice d = _model.Devices[device];
        int total = 0;
        for (int line = 0; line < d.Slots.Length; line++)
        {
            total += _lineCount[d.LineBase + line];
        }

        return total;
    }

    /// <summary>People whose service has started at a device since the start.</summary>
    public long ServedAt(int device) => _queueWaitCount[device];

    /// <summary>Mean seconds from joining a line to being served, per device type, since the start (diagnostics).</summary>
    public IReadOnlyList<(string TypeTag, double MeanWaitS, long Served)> QueueWaitsByType()
    {
        var byType = new Dictionary<string, (double Sum, long Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (SimDevice device in _model.Devices)
        {
            (double sum, long count) = byType.GetValueOrDefault(device.TypeTag);
            byType[device.TypeTag] = (sum + _queueWaitSum[device.Index], count + _queueWaitCount[device.Index]);
        }

        return [.. byType.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value.Count > 0 ? kv.Value.Sum / kv.Value.Count : 0, kv.Value.Count))];
    }

    /// <summary>Mean and peak stress of the people in the study now (D-156 diagnostics).</summary>
    public (double Mean, double Peak) StressNow()
    {
        double sum = 0, peak = 0;
        for (int i = 0; i < _agents.Count; i++)
        {
            sum += _agents.Stress[i];
            peak = Math.Max(peak, _agents.Stress[i]);
        }

        return (_agents.Count > 0 ? sum / _agents.Count : 0, peak);
    }

    private void ResetDevices()
    {
        Array.Fill(_laneDir, (sbyte)-1);
        foreach (SimDevice device in _model.Devices)
        {
            // Lanes fixed to one direction (D-196) keep it for the whole run.
            for (int lane = 0; lane < device.Lanes; lane++)
            {
                if (device.FixedSide(lane) >= 0)
                {
                    _laneDir[device.LaneBase + lane] = (sbyte)device.FixedSide(lane);
                }
            }
        }

        Array.Clear(_laneServing);
        Array.Clear(_laneThrough);
        Array.Clear(_laneClosing);
        Array.Clear(_lineCount);
        Array.Clear(_waitJoin);
        Array.Clear(_queueWaitSum);
        Array.Clear(_queueWaitCount);
        _gaveUp = 0;
    }

    // ------------------------------------------------------------------ spawning and steps

    /// <summary>One of the emitter's mix lines, picked by share (D-151).</summary>
    private static SimMixLine PickLine(SimEmitter emitter, DeterministicRandom random)
    {
        SimMixLine[] lines = emitter.Lines;
        if (lines.Length == 1)
        {
            return lines[0];
        }

        double pick = random.NextDouble() * emitter.TotalShare;
        foreach (SimMixLine line in lines)
        {
            pick -= line.Share;
            if (pick < 0)
            {
                return line;
            }
        }

        return lines[^1];
    }

    /// <summary>The tags a new agent of this line starts with (D-150).</summary>
    private static ulong SpawnTags(SimMixLine line, DeterministicRandom random)
    {
        ulong tags = 0;
        foreach ((ulong bit, double probability) in line.SpawnTags)
        {
            if (probability >= 1 || random.NextDouble() < probability)
            {
                tags |= bit;
            }
        }

        return tags;
    }

    private void StartJourney(int i, DeterministicRandom random)
    {
        _agents.Step[i] = -1;
        AdvanceStep(i, random);
    }

    /// <summary>The kind of the agent's current step (exit once past the last step or without a journey).</summary>
    private JourneyStepKind CurrentKind(int i) => CurrentStep(i)?.Kind ?? JourneyStepKind.Exit;

    private SimStep? CurrentStep(int i)
    {
        int j = _agents.Journey[i];
        int k = _agents.Step[i];
        if (j < 0)
        {
            return null;
        }

        SimStep[] steps = _model.Journeys[j].Steps;
        return k >= 0 && k < steps.Length ? steps[k] : null;
    }

    /// <summary>
    /// Moves to the next step that applies (D-150) and sets the target: a device (D-152), a zone, or the exit. Steps
    /// whose targets can't be reached from here are skipped.
    /// </summary>
    private void AdvanceStep(int i, DeterministicRandom random)
    {
        AgentStore a = _agents;
        a.Phase[i] = AgentPhase.Walk;
        a.QDevice[i] = -1;
        a.QLine[i] = -1;
        int j = a.Journey[i];
        if (j < 0)
        {
            BeginExit(i, random);
            return;
        }

        SimStep[] steps = _model.Journeys[j].Steps;
        while (true)
        {
            int k = ++a.Step[i];
            if (k >= steps.Length)
            {
                BeginExit(i, random);
                return;
            }

            SimStep step = steps[k];
            if (!step.AppliesTo(a.Tags[i]))
            {
                continue;
            }

            switch (step.Kind)
            {
                case JourneyStepKind.Exit:
                    BeginExit(i, random);
                    return;

                case JourneyStepKind.BoardTrain:
                {
                    // Leave on a train (D-186): head for the quickest, quietest door of the step's services.
                    int door = ChooseTrainDoor(i, step, random);
                    if (door < 0)
                    {
                        continue;
                    }

                    a.Sink[i] = door;
                    Decide(i, random);
                    return;
                }

                case JourneyStepKind.GoToDevice:
                {
                    int d = ChooseDevice(i, step, -1, random, out _);
                    if (d < 0)
                    {
                        continue;
                    }

                    a.QDevice[i] = d;
                    a.Sink[i] = _model.Devices[d].Target;
                    Decide(i, random);
                    return;
                }

                default:
                {
                    int target = ChooseZone(i, step, random);
                    if (target < 0)
                    {
                        continue;
                    }

                    a.Sink[i] = target;
                    Decide(i, random);
                    return;
                }
            }
        }
    }

    /// <summary>Grants the step's tags and moves on.</summary>
    private void CompleteStep(int i)
    {
        if (CurrentStep(i) is { } step)
        {
            _agents.Tags[i] |= step.Grants;
        }

        AdvanceStep(i, _random);
    }

    /// <summary>Heads for a sink: the emitter's destinations, nearest from here or by weight (D-096).</summary>
    private void BeginExit(int i, DeterministicRandom random)
    {
        AgentStore a = _agents;
        int j = a.Journey[i];
        a.Step[i] = j >= 0 ? _model.Journeys[j].Steps.Length : 0;
        a.Phase[i] = AgentPhase.Walk;
        a.QDevice[i] = -1;
        SimEmitter emitter = _model.Emitters[a.Emitter[i]];
        a.Sink[i] = PickSink(i, emitter, _model.Profiles[a.Profile[i]], random);
        Decide(i, random);
    }

    /// <summary>
    /// The device of the step's type with the lowest walk (with decisiveness noise when <paramref name="random"/> is
    /// set) + expected wait + choice penalty (D-152), skipping <paramref name="exclude"/>; −1 when none can be reached.
    /// </summary>
    private int ChooseDevice(int i, SimStep step, int exclude, DeterministicRandom? random, out double bestTotal)
    {
        AgentStore a = _agents;
        SimProfile profile = _model.Profiles[a.Profile[i]];
        int best = -1;
        bestTotal = double.PositiveInfinity;
        if (_world.Surfaces[a.Surface[i]].Kind != NavSurfaceKind.Floor)
        {
            return -1;
        }

        foreach (int d in step.Targets)
        {
            if (d == exclude)
            {
                continue;
            }

            SimDevice device = _model.Devices[d];
            if (device.PassThrough && device.Floor == a.Surface[i] && !device.ServesSide(device.SideOf(new Point2(a.X[i], a.Y[i]))))
            {
                // Every lane runs the other way for someone on this side (D-196).
                continue;
            }

            double walk = RoutePlanner.Choose(_routes, a.Surface[i], a.X[i], a.Y[i], device.Target, profile.Mobility, profile.Template, random, out _, out _, _wait, BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: Sight(i));
            if (!double.IsFinite(walk))
            {
                continue;
            }

            // Preset choice weight and the profile's device preferences (D-166) are seconds on or off the cost.
            double total = walk + ExpectedWait(device) + device.ChoicePenaltyS - profile.BonusFor(d);
            if (total < bestTotal)
            {
                bestTotal = total;
                best = d;
            }
        }

        return best;
    }

    /// <summary>The quickest zone of the step to reach from here; −1 when none can be reached.</summary>
    private int ChooseZone(int i, SimStep step, DeterministicRandom random)
    {
        AgentStore a = _agents;
        if (_world.Surfaces[a.Surface[i]].Kind != NavSurfaceKind.Floor)
        {
            return -1;
        }

        SimProfile profile = _model.Profiles[a.Profile[i]];
        int best = -1;
        double bestCost = double.PositiveInfinity;
        foreach (int target in step.Targets)
        {
            double c = RoutePlanner.Choose(_routes, a.Surface[i], a.X[i], a.Y[i], target, profile.Mobility, profile.Template, random, out _, out _, _wait, BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: Sight(i));
            if (c < bestCost)
            {
                bestCost = c;
                best = target;
            }
        }

        return best;
    }

    /// <summary>
    /// Expected wait at a device: its least loaded lane's people × mean service time (D-152); with a shared snake,
    /// everyone in the snake and the lanes shared out over the lanes (D-163).
    /// </summary>
    private double ExpectedWait(SimDevice device)
    {
        if (device.HasSnake)
        {
            return SnakeWait(device, _lineCount[device.LineBase + device.SnakeLine]);
        }

        double least = double.PositiveInfinity;
        for (int lane = 0; lane < device.Lanes; lane++)
        {
            least = Math.Min(least, LaneLoad(device, lane));
        }

        return least * device.MeanServiceS;
    }

    /// <summary>Wait behind <paramref name="ahead"/> people in a shared snake: they and the lanes' loads over the lanes.</summary>
    private double SnakeWait(SimDevice device, int ahead)
    {
        int busy = 0;
        for (int lane = 0; lane < device.Lanes; lane++)
        {
            busy += LaneLoad(device, lane);
        }

        return (ahead + busy) * device.MeanServiceS / device.Lanes;
    }

    /// <summary>True when a pass-through lane can take people from <paramref name="side"/> (it's idle or already theirs and not closing).</summary>
    private bool LaneServesSide(int g, int side) => _laneDir[g] < 0 || (_laneDir[g] == side && !_laneClosing[g]);

    /// <summary>People queued for a lane on either side, plus one while someone is being served.</summary>
    private int LaneLoad(SimDevice device, int lane)
    {
        int load = _laneServing[device.LaneBase + lane] ? 1 : 0;
        for (int side = 0; side < device.Sides; side++)
        {
            load += _lineCount[device.LineBase + device.Line(side, lane)];
        }

        return load;
    }

    /// <summary>True when a newcomer on <paramref name="side"/> may join the lane's line.</summary>
    private bool LaneOpenFor(SimDevice device, int lane, int side)
    {
        int line = device.Line(side, lane);
        if (device.Slots[line].Length == 0 || _lineCount[device.LineBase + line] >= device.Slots[line].Length)
        {
            return false;
        }

        if (!device.PassThrough)
        {
            return true;
        }

        int g = device.LaneBase + lane;
        return _laneDir[g] < 0 || (_laneDir[g] == side && !_laneClosing[g]);
    }

    // ------------------------------------------------------------------ per step

    /// <summary>
    /// Lane directions at pass-through devices: an idle lane (no one queued on its side, served or walking through)
    /// forgets its side; when people on one side have no lane open to them, the other side's least busy lane closes
    /// to newcomers so it can turn round once drained.
    /// </summary>
    private void UpdateDevices()
    {
        if (_model.Devices.Count == 0)
        {
            return;
        }

        Array.Clear(_waitJoin);
        AgentStore a = _agents;
        for (int i = 0; i < a.Count; i++)
        {
            if (a.Phase[i] == AgentPhase.WaitJoin && a.QDevice[i] >= 0)
            {
                _waitJoin[(a.QDevice[i] * 2) + a.QSide[i]]++;
            }
        }

        foreach (SimDevice device in _model.Devices)
        {
            if (!device.PassThrough)
            {
                continue;
            }

            for (int lane = 0; lane < device.Lanes; lane++)
            {
                int g = device.LaneBase + lane;
                int dir = _laneDir[g];
                if (dir >= 0 && device.FixedSide(lane) < 0 && !_laneServing[g] && _laneThrough[g] == 0 && _lineCount[device.LineBase + device.Line(dir, lane)] == 0)
                {
                    _laneDir[g] = -1;
                    _laneClosing[g] = false;
                }
            }

            for (int side = 0; side < 2; side++)
            {
                // People in a shared snake (D-163) want a service-side lane as much as people waiting to join.
                bool snakeSide = side == 0 && device.HasSnake;
                int demand = _waitJoin[(device.Index * 2) + side] + (snakeSide ? _lineCount[device.LineBase + device.SnakeLine] : 0);
                if (demand == 0)
                {
                    continue;
                }

                bool open = false, closing = false;
                int pick = -1, pickLoad = int.MaxValue;
                for (int lane = 0; lane < device.Lanes; lane++)
                {
                    int g = device.LaneBase + lane;
                    if (device.Slots[device.Line(side, lane)].Length == 0)
                    {
                        continue;
                    }

                    if (snakeSide ? LaneServesSide(g, side) : LaneOpenFor(device, lane, side))
                    {
                        open = true;
                    }
                    else if (_laneDir[g] == 1 - side && device.FixedSide(lane) < 0)
                    {
                        closing |= _laneClosing[g];
                        int load = LaneLoad(device, lane);
                        if (load < pickLoad)
                        {
                            pickLoad = load;
                            pick = g;
                        }
                    }
                }

                if (!open && !closing && pick >= 0)
                {
                    _laneClosing[pick] = true;
                }
            }
        }
    }

    /// <summary>One agent's journey bookkeeping before its forces are worked out.</summary>
    private void UpdateJourney(int i)
    {
        AgentStore a = _agents;
        if (_model.Trains.Count > 0 && a.Phase[i] is AgentPhase.Walk or AgentPhase.WaitTrain)
        {
            // Train doors (D-186): wait aside until the train can take them, then go and board.
            UpdateTrainWait(i);
        }

        switch (a.Phase[i])
        {
            case AgentPhase.Walk:
                if (a.QDevice[i] >= 0 && CurrentKind(i) == JourneyStepKind.GoToDevice)
                {
                    TryJoin(i, waiting: false);
                }

                break;

            case AgentPhase.WaitJoin:
                if (!TryGiveUp(i))
                {
                    TryJoin(i, waiting: true);
                }

                break;

            case AgentPhase.Queue:
                if (!TryGiveUp(i))
                {
                    UpdateQueue(i);
                }

                break;

            case AgentPhase.Service:
                if (TimeS >= a.Timer[i])
                {
                    FinishService(i);
                }

                break;

            case AgentPhase.Through:
            {
                double dx = a.AimX[i] - a.X[i], dy = a.AimY[i] - a.Y[i];
                if ((dx * dx) + (dy * dy) < ThroughDoneM * ThroughDoneM || TimeS >= a.Timer[i])
                {
                    SimDevice device = _model.Devices[a.QDevice[i]];
                    _laneThrough[device.LaneBase + a.QSlot[i]]--;
                    CompleteStep(i);
                }

                break;
            }

            case AgentPhase.Aside:
                if (TimeS >= a.Timer[i])
                {
                    a.Phase[i] = AgentPhase.Walk;
                }

                break;

            case AgentPhase.Dwell:
            {
                double dx = a.AimX[i] - a.X[i], dy = a.AimY[i] - a.Y[i];
                if (TimeS >= a.Timer[i])
                {
                    CompleteStep(i);
                }
                else if ((dx * dx) + (dy * dy) < DwellReachM * DwellReachM)
                {
                    NewDwellAim(i);
                }

                break;
            }
        }
    }

    /// <summary>
    /// Joins the shortest open line on the agent's side of its device once within reach of that line's tail
    /// (always, when <paramref name="waiting"/>), unless another device is clearly quicker (balk, D-152). With no
    /// line open it stands and waits (<see cref="AgentPhase.WaitJoin"/>).
    /// </summary>
    private void TryJoin(int i, bool waiting)
    {
        AgentStore a = _agents;
        SimDevice device = _model.Devices[a.QDevice[i]];
        if (a.Surface[i] != device.Floor)
        {
            return;
        }

        var p = new Point2(a.X[i], a.Y[i]);
        int side = device.SideOf(p);
        if (side == 0 && device.HasSnake)
        {
            TryJoinSnake(i, device, p, waiting);
            return;
        }

        int best = -1, bestLoad = int.MaxValue, turnLoad = int.MaxValue;
        double bestDistance = double.PositiveInfinity, nearest = double.PositiveInfinity;
        int longest = 0;
        for (int lane = 0; lane < device.Lanes; lane++)
        {
            Point2[] slots = device.Slots[device.Line(side, lane)];
            if (slots.Length == 0)
            {
                continue;
            }

            double distance = Point2.Distance(p, slots[0]);
            nearest = Math.Min(nearest, distance);
            longest = Math.Max(longest, _lineCount[device.LineBase + device.Line(side, lane)]);
            if (device.FixedSide(lane) < 0 || device.FixedSide(lane) == side)
            {
                // A lane fixed the other way (D-196) never turns round for this side.
                turnLoad = Math.Min(turnLoad, LaneLoad(device, lane));
            }

            if (!LaneOpenFor(device, lane, side))
            {
                continue;
            }

            int load = LaneLoad(device, lane);
            if (load < bestLoad || (load == bestLoad && distance < bestDistance))
            {
                best = lane;
                bestLoad = load;
                bestDistance = distance;
            }
        }

        if (!waiting)
        {
            // Close enough to the line's tail (or, with no line open, to the queue area) to join.
            double reach = best >= 0
                ? (SimDevice.SlotSpacingM * _lineCount[device.LineBase + device.Line(side, best)]) + JoinReachM
                : (SimDevice.SlotSpacingM * longest) + JoinReachM;
            double distance = best >= 0 ? bestDistance : nearest;
            if (distance > reach)
            {
                return;
            }
        }

        // With no line open, the wait is about the time for the least busy lane to drain and turn round.
        double waitHere = (best >= 0 ? bestLoad : turnLoad == int.MaxValue ? double.PositiveInfinity : turnLoad + 1) * device.MeanServiceS;
        if ((!waiting || IsReviewTick(i)) && TryBalk(i, device, waitHere))
        {
            return;
        }

        if (best < 0)
        {
            WaitForLane(i, device, side, waiting);
            return;
        }

        int g = device.LaneBase + best;
        if (device.PassThrough && _laneDir[g] < 0)
        {
            _laneDir[g] = (sbyte)side;
        }

        JoinLine(i, device, device.Line(side, best), side, waiting);
    }

    /// <summary>
    /// Joins the tail of a device's shared snake (D-163) once near its head or its tail (always, when
    /// <paramref name="waiting"/>), unless another device is clearly quicker; waits at a hold point when it's full.
    /// </summary>
    private void TryJoinSnake(int i, SimDevice device, Point2 p, bool waiting)
    {
        int line = device.SnakeLine;
        Point2[] slots = device.Slots[line];
        int count = _lineCount[device.LineBase + line];
        if (!waiting)
        {
            // At the device's target (near the head) or at the snake's tail, wherever the path winds.
            Point2 tail = slots[Math.Min(count, slots.Length - 1)];
            double reach = (SimDevice.SlotSpacingM * count) + JoinReachM;
            if (Point2.Distance(p, slots[0]) > reach && Point2.Distance(p, tail) > JoinReachM)
            {
                return;
            }
        }

        if ((!waiting || IsReviewTick(i)) && TryBalk(i, device, SnakeWait(device, count)))
        {
            return;
        }

        if (count >= slots.Length)
        {
            WaitForLane(i, device, 0, waiting);
            return;
        }

        JoinLine(i, device, line, 0, waiting);
    }

    /// <summary>
    /// Takes the next slot of a line. The patience clock (and the queue wait in diagnostics) starts when the person
    /// first stops for the device: on joining, or earlier when they had to wait for a lane.
    /// </summary>
    private void JoinLine(int i, SimDevice device, int line, int side, bool fromWaiting)
    {
        AgentStore a = _agents;
        a.QLine[i] = line;
        a.QSlot[i] = _lineCount[device.LineBase + line]++;
        a.QSide[i] = side;
        a.Phase[i] = AgentPhase.Queue;
        if (!fromWaiting)
        {
            a.JoinTime[i] = TimeS;
        }
    }

    /// <summary>
    /// No line open: stand at the side's nearest hold point (D-167), 2 m out and clear of the lane mouths, or where
    /// the person is when the device has none with floor.
    /// </summary>
    private void WaitForLane(int i, SimDevice device, int side, bool alreadyWaiting)
    {
        AgentStore a = _agents;
        a.QSide[i] = side;
        if (alreadyWaiting)
        {
            return;
        }

        a.Phase[i] = AgentPhase.WaitJoin;
        a.JoinTime[i] = TimeS;
        double x = a.X[i], y = a.Y[i];
        a.AimX[i] = x;
        a.AimY[i] = y;
        double nearest = double.PositiveInfinity;
        Point2[] holds = side < device.HoldPoints.Length ? device.HoldPoints[side] : [];
        foreach (Point2 hold in holds)
        {
            double dx = hold.X - x, dy = hold.Y - y;
            double d2 = (dx * dx) + (dy * dy);
            if (d2 < nearest)
            {
                nearest = d2;
                a.AimX[i] = hold.X;
                a.AimY[i] = hold.Y;
            }
        }
    }

    /// <summary>
    /// Patience (D-165): once a queuer has waited longer than the profile's patience at a device step whose give-up
    /// is Leave or JumpTo, they leave the line and either head for an exit (counted in <see cref="GaveUp"/>) or carry
    /// on from the step it names (no tag granted). Returns true when they gave up.
    /// </summary>
    private bool TryGiveUp(int i)
    {
        if (CurrentStep(i) is not { Kind: JourneyStepKind.GoToDevice } step || step.OnGiveUp == JourneyGiveUp.KeepWaiting)
        {
            return false;
        }

        AgentStore a = _agents;
        double patience = _model.Profiles[a.Profile[i]].Template.PatienceS;
        if (!(TimeS - a.JoinTime[i] >= patience))
        {
            return false;
        }

        if (a.Phase[i] == AgentPhase.Queue)
        {
            LeaveLine(i);
        }

        a.Phase[i] = AgentPhase.Walk;
        a.QDevice[i] = -1;
        if (step.OnGiveUp == JourneyGiveUp.JumpTo && step.GiveUpStep >= 0)
        {
            a.Step[i] = step.GiveUpStep - 1;
            AdvanceStep(i, _random);
            return true;
        }

        _gaveUp++;
        BeginExit(i, _random);
        return true;
    }

    /// <summary>
    /// Switches to another device of the step's type when <paramref name="waitHere"/> plus this device's choice
    /// weight and preference adjustment is over <see cref="BalkFactor"/> × that device's adjusted walk + wait (D-152,
    /// D-166). Returns true when it switched.
    /// </summary>
    private bool TryBalk(int i, SimDevice device, double waitHere)
    {
        if (!_tuning.Balk || CurrentStep(i) is not { } step || step.Targets.Length < 2)
        {
            return false;
        }

        // Like for like (r009b-3): the other device's total includes its choice weight and the profile's preference,
        // so this device's wait carries its own too. A favoured device is kept through a longer queue (D-166).
        AgentStore a = _agents;
        double adjustHere = device.ChoicePenaltyS - _model.Profiles[a.Profile[i]].BonusFor(device.Index);
        int other = ChooseDevice(i, step, device.Index, null, out double otherTotal);
        if (other < 0 || !(waitHere + adjustHere > BalkFactor * otherTotal))
        {
            return false;
        }

        LeaveLine(i);
        a.Phase[i] = AgentPhase.Walk;
        a.QDevice[i] = other;
        a.Sink[i] = _model.Devices[other].Target;
        Decide(i, _random);
        return true;
    }

    /// <summary>In a line: the head is served when its lane is free; the rest may balk every few seconds.</summary>
    private void UpdateQueue(int i)
    {
        AgentStore a = _agents;
        SimDevice device = _model.Devices[a.QDevice[i]];
        if (a.QLine[i] == device.SnakeLine)
        {
            // Shared snake (D-163): the head goes to whichever service-side lane frees first.
            if (a.QSlot[i] == 0)
            {
                PromoteSnakeHead(i, device);
            }
            else if (a.QSlot[i] >= 2 && IsReviewTick(i))
            {
                TryBalk(i, device, SnakeWait(device, a.QSlot[i]));
            }

            return;
        }

        int lane = a.QLine[i] % device.Lanes;
        int g = device.LaneBase + lane;
        if (a.QSlot[i] == 0)
        {
            Point2 head = device.Slots[a.QLine[i]][0];
            double dx = head.X - a.X[i], dy = head.Y - a.Y[i];
            if ((dx * dx) + (dy * dy) > HeadReachM * HeadReachM || _laneServing[g])
            {
                return;
            }

            if (device.PassThrough)
            {
                if (_laneDir[g] < 0)
                {
                    _laneDir[g] = (sbyte)a.QSide[i];
                }
                else if (_laneDir[g] != a.QSide[i])
                {
                    return;
                }
            }

            _laneServing[g] = true;
            a.Phase[i] = AgentPhase.Service;
            a.Timer[i] = TimeS + Math.Max(0, DistributionSampler.Sample(device.ServiceTime, _random));
            _queueWaitSum[device.Index] += TimeS - a.JoinTime[i];
            _queueWaitCount[device.Index]++;
            return;
        }

        if (a.QSlot[i] >= 2 && IsReviewTick(i))
        {
            TryBalk(i, device, (a.QSlot[i] + 1) * device.MeanServiceS);
        }
    }

    /// <summary>
    /// Moves the snake's head into the nearest service-side lane that is empty and free for that side (D-163); the
    /// rest of the snake shuffles up. The head is then served at that lane like any line head.
    /// </summary>
    private void PromoteSnakeHead(int i, SimDevice device)
    {
        AgentStore a = _agents;
        var at = new Point2(a.X[i], a.Y[i]);
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        for (int lane = 0; lane < device.Lanes; lane++)
        {
            int line = device.Line(0, lane);
            int g = device.LaneBase + lane;
            if (device.Slots[line].Length == 0 || _lineCount[device.LineBase + line] > 0 || _laneServing[g]
                || (device.PassThrough && !LaneServesSide(g, 0)))
            {
                continue;
            }

            double distance = Point2.Distance(at, device.Slots[line][0]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = lane;
            }
        }

        if (best < 0)
        {
            return;
        }

        LeaveLine(i);
        int laneIndex = device.LaneBase + best;
        if (device.PassThrough && _laneDir[laneIndex] < 0)
        {
            _laneDir[laneIndex] = 0;
        }

        int laneLine = device.Line(0, best);
        a.QLine[i] = laneLine;
        a.QSlot[i] = _lineCount[device.LineBase + laneLine]++;
    }

    /// <summary>Service done: leave the line, then walk through (gates) or step aside and head on (machines).</summary>
    private void FinishService(int i)
    {
        AgentStore a = _agents;
        SimDevice device = _model.Devices[a.QDevice[i]];
        int lane = a.QLine[i] % device.Lanes;
        int side = a.QSide[i];
        _laneServing[device.LaneBase + lane] = false;
        LeaveLine(i);

        if (device.PassThrough)
        {
            Point2 far = device.FarPoint(side, lane);
            a.Phase[i] = AgentPhase.Through;
            a.QSlot[i] = lane;
            a.AimX[i] = far.X;
            a.AimY[i] = far.Y;
            a.Timer[i] = TimeS + ThroughTimeoutS;
            _laneThrough[device.LaneBase + lane]++;
            return;
        }

        // Step to the side away from the middle of the device, out of the line behind.
        double t = device.Lanes == 1 ? 1 : (lane < device.Lanes / 2.0 ? -1 : 1);
        Point2 aside = device.Facing.Perpendicular() * (t * AsideM);
        double x = a.X[i], y = a.Y[i];
        CompleteStep(i);
        a.Phase[i] = AgentPhase.Aside;
        a.AimX[i] = x + aside.X;
        a.AimY[i] = y + aside.Y;
        a.Timer[i] = TimeS + AsideS;
    }

    /// <summary>Takes the agent out of its line; everyone behind moves up a slot.</summary>
    private void LeaveLine(int i)
    {
        AgentStore a = _agents;
        int line = a.QLine[i];
        int d = a.QDevice[i];
        if (line < 0 || d < 0)
        {
            return;
        }

        SimDevice device = _model.Devices[d];
        _lineCount[device.LineBase + line] = Math.Max(0, _lineCount[device.LineBase + line] - 1);
        int slot = a.QSlot[i];
        for (int j = 0; j < a.Count; j++)
        {
            if (j != i && a.QDevice[j] == d && a.QLine[j] == line && a.QSlot[j] > slot
                && a.Phase[j] is AgentPhase.Queue or AgentPhase.Service)
            {
                a.QSlot[j]--;
            }
        }

        a.QLine[i] = -1;
    }

    /// <summary>Frees whatever the agent holds at a device before it's removed.</summary>
    private void ReleaseDevice(int i)
    {
        AgentStore a = _agents;
        int d = a.QDevice[i];
        if (d < 0)
        {
            return;
        }

        SimDevice device = _model.Devices[d];
        switch (a.Phase[i])
        {
            case AgentPhase.Service:
                _laneServing[device.LaneBase + (a.QLine[i] % device.Lanes)] = false;
                LeaveLine(i);
                break;

            case AgentPhase.Queue:
                LeaveLine(i);
                break;

            case AgentPhase.Through:
                _laneThrough[device.LaneBase + a.QSlot[i]]--;
                break;
        }

        a.Phase[i] = AgentPhase.Walk;
        a.QDevice[i] = -1;
    }

    /// <summary>
    /// Reached its target's cells while walking: true to leave the study (exits); otherwise the step reacts (pass
    /// zone done, dwell starts, a device join is attempted) and the agent stays.
    /// </summary>
    private bool ReachedTarget(int i)
    {
        switch (CurrentKind(i))
        {
            case JourneyStepKind.Exit:
            case JourneyStepKind.BoardTrain:
            {
                // A train door is a sink only while its service is boarding (D-184, D-187).
                int v = DoorTrainOf(_agents.Sink[i]);
                return v < 0 || ArriveAtTrainDoor(i, v);
            }

            case JourneyStepKind.PassZone:
                CompleteStep(i);
                return false;

            case JourneyStepKind.Dwell:
            {
                AgentStore a = _agents;
                SimStep step = CurrentStep(i)!;
                a.Phase[i] = AgentPhase.Dwell;
                a.Timer[i] = TimeS + Math.Max(0, DistributionSampler.Sample(step.Dwell, _random));
                NewDwellAim(i);
                return false;
            }

            default:
                if (_agents.QDevice[i] >= 0)
                {
                    TryJoin(i, waiting: false);
                }

                return false;
        }
    }

    /// <summary>A random spot in the zone being dwelt in.</summary>
    private void NewDwellAim(int i)
    {
        AgentStore a = _agents;
        NavSink zone = _world.Sinks[a.Sink[i]];
        Point2 c = _world.Surfaces[zone.Floor].CellCentre(zone.Cells[_random.NextInt(zone.Cells.Length)]);
        a.AimX[i] = c.X;
        a.AimY[i] = c.Y;
    }

    /// <summary>
    /// Desired direction (scaled to slow down on arrival) for everyone not simply walking: to their slot in the
    /// line or the service point, through the lane, aside, or about the dwell zone; standing still while waiting
    /// for a lane.
    /// </summary>
    private void SteerByPhase(int i, double x, double y, ref double ex, ref double ey)
    {
        AgentStore a = _agents;
        double tx, ty, scale = 1;
        bool arrive = false;
        switch (a.Phase[i])
        {
            case AgentPhase.Queue:
            case AgentPhase.Service:
            {
                Point2[] slots = _model.Devices[a.QDevice[i]].Slots[a.QLine[i]];
                Point2 slot = slots[Math.Clamp(a.Phase[i] == AgentPhase.Service ? 0 : a.QSlot[i], 0, slots.Length - 1)];
                tx = slot.X;
                ty = slot.Y;
                arrive = true;
                break;
            }

            case AgentPhase.WaitJoin:
            case AgentPhase.WaitTrain:
                // To the hold point (D-167) or the spot beside a train door (D-186), then stand.
                tx = a.AimX[i];
                ty = a.AimY[i];
                arrive = true;
                break;

            case AgentPhase.Dwell:
                tx = a.AimX[i];
                ty = a.AimY[i];
                scale = DwellSpeed;
                break;

            default:
                tx = a.AimX[i];
                ty = a.AimY[i];
                break;
        }

        double dx = tx - x, dy = ty - y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < (arrive ? SlotHoldM : 1e-3))
        {
            ex = ey = 0;
            return;
        }

        if (arrive)
        {
            scale = Math.Min(1.0, distance / ArriveM);
        }

        ex = dx / distance * scale;
        ey = dy / distance * scale;
    }

    /// <summary>True on this agent's review steps (every few seconds, staggered by spawn time, D-113).</summary>
    private bool IsReviewTick(int i)
    {
        long spawnTick = (long)Math.Round(_agents.SpawnTime[i] / _dt);
        return (_tick + spawnTick) % _reviewTicks == 0;
    }
}
