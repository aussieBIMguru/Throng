using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// Round 013a, lifts (D-236–D-244): cars moving in their shafts on a trapezoidal speed profile, doors opening and
/// closing, hall calls from the people waiting on each landing assigned to the car that can get there soonest,
/// collective stopping (Shuttle is the same rule over two stops, Up-peak returns to the lobby when idle, a schedule
/// switches between them by time of day), getting off before getting on, boarding in arrival order with wheelchair
/// users first at accessible-priority lifts, and car space by body size (D-243).
/// </summary>
/// <remarks>
/// People keep their agent slot while riding (<see cref="AgentPhase.LiftRide"/>): they're held inside the car's
/// footprint on the floor they boarded from, carried at the car's height, and skipped by movement and by other
/// people's forces. Getting off puts them just outside the door on the new floor, and they choose their route again.
/// </remarks>
public sealed partial class SimEngine
{
    /// <summary>Seconds per person getting on or off a car (one door stream per car).</summary>
    public const double LiftTransferS = 0.8;

    /// <summary>Doors stay open at least this long after the last person got on or off, s.</summary>
    public const double LiftDoorHoldS = 1.5;

    /// <summary>A car this full (share of its places) counts as full when calls are assigned.</summary>
    public const double LiftFullShare = 0.85;

    /// <summary>Body radius that takes one place in a car (D-243).</summary>
    public const double LiftPlaceRadiusM = 0.25;

    /// <summary>
    /// Seconds a lift feels slower to people without priority while someone with priority waits at it (D-248).
    /// </summary>
    public const double LiftPriorityPenaltyS = 60.0;

    private const double CarArriveEpsM = 0.005;

    private int _carCount;
    private int[] _carLift = [];
    private double[] _carZ = [], _carV = [], _carTimer = [], _carHold = [], _carNextTransfer = [], _carLoad = [];
    private LiftCarStatus[] _carState = [];
    private int[] _carStop = [], _carTarget = [], _carRiders = [];
    private sbyte[] _carDir = [];
    private int[][] _carCalls = [];
    private int[][] _hallCount = [], _hallCar = [];
    private int[] _liftWaiting = [], _liftPriorityWaiting = [];
    private double[] _liftPriorityS = [];
    private int[] _portalLift = [];

    /// <summary>Cars in every bank.</summary>
    public int LiftCarCount => _carCount;

    /// <summary>A car's state for the plan symbols (D-240): its bank, height, state, riders and direction (+1, 0, −1).</summary>
    public (int Lift, double Z, LiftCarStatus State, int Riders, int Direction) LiftCarState(int car) =>
        car >= 0 && car < _carCount ? (_carLift[car], _carZ[car], _carState[car], _carRiders[car], _carDir[car]) : (-1, 0, LiftCarStatus.Idle, 0, 0);

    /// <summary>People waiting on any landing of a bank.</summary>
    public int LiftWaiting(int lift) => lift >= 0 && lift < _liftWaiting.Length ? _liftWaiting[lift] : 0;

    /// <summary>Sets up the lift arrays (called from the constructor).</summary>
    private void InitLifts()
    {
        IReadOnlyList<SimLift> lifts = _model.Lifts;
        _carCount = 0;
        foreach (SimLift lift in lifts)
        {
            _carCount += lift.Cars;
        }

        _carLift = new int[_carCount];
        _carZ = new double[_carCount];
        _carV = new double[_carCount];
        _carTimer = new double[_carCount];
        _carHold = new double[_carCount];
        _carNextTransfer = new double[_carCount];
        _carLoad = new double[_carCount];
        _carState = new LiftCarStatus[_carCount];
        _carStop = new int[_carCount];
        _carTarget = new int[_carCount];
        _carRiders = new int[_carCount];
        _carDir = new sbyte[_carCount];
        _carCalls = new int[_carCount][];
        _hallCount = new int[lifts.Count][];
        _hallCar = new int[lifts.Count][];
        _liftWaiting = new int[lifts.Count];
        _liftPriorityWaiting = new int[lifts.Count];
        _liftPriorityS = new double[_world.Connectors.Count];
        _portalLift = new int[_world.Portals.Count];
        Array.Fill(_portalLift, -1);
        foreach (SimLift lift in lifts)
        {
            _hallCount[lift.Index] = new int[lift.Stops * 2];
            _hallCar[lift.Index] = new int[lift.Stops * 2];
            for (int c = 0; c < lift.Cars; c++)
            {
                int car = lift.FirstCar + c;
                _carLift[car] = lift.Index;
                _carCalls[car] = new int[lift.Stops];
            }

            foreach (int p in lift.Portals)
            {
                if (p >= 0)
                {
                    _portalLift[p] = lift.Index;
                }
            }
        }
    }

    /// <summary>Every car empty and idle at its lobby (D-244), no calls.</summary>
    private void ResetLifts()
    {
        foreach (SimLift lift in _model.Lifts)
        {
            Array.Fill(_hallCar[lift.Index], -1);
            Array.Clear(_hallCount[lift.Index]);
            for (int c = 0; c < lift.Cars; c++)
            {
                int car = lift.FirstCar + c;
                _carStop[car] = _carTarget[car] = lift.LobbyStop;
                _carZ[car] = lift.StopZ[lift.LobbyStop];
                Array.Clear(_carCalls[car]);
            }
        }

        Array.Clear(_carV);
        Array.Clear(_carTimer);
        Array.Clear(_carHold);
        Array.Clear(_carNextTransfer);
        Array.Clear(_carLoad);
        Array.Clear(_carState);
        Array.Clear(_carRiders);
        Array.Clear(_carDir);
        Array.Clear(_liftWaiting);
        Array.Clear(_liftPriorityWaiting);
        Array.Clear(_liftPriorityS);
    }

    /// <summary>The bank a portal is a landing of, or −1.</summary>
    private int LiftOfPortal(int portal) => portal >= 0 && portal < _portalLift.Length ? _portalLift[portal] : -1;

    /// <summary>Expected extra wait at a bank for route choice: people waiting ÷ the bank's places per round trip.</summary>
    private double LiftWaitS(int connector)
    {
        foreach (SimLift lift in _model.Lifts)
        {
            if (lift.Connector == connector)
            {
                return _liftWaiting[lift.Index] / Math.Max(1.0, lift.Cars * lift.Capacity) * lift.RoundTripS;
            }
        }

        return 0;
    }

    /// <summary>
    /// The D-248 penalty per connector for route choice: <see cref="LiftPriorityPenaltyS"/> on a bank while someone
    /// with priority waits at any of its landings, else 0 (refreshed with the waits each step).
    /// </summary>
    private void UpdateLiftPriorityPenalty()
    {
        foreach (SimLift lift in _model.Lifts)
        {
            _liftPriorityS[lift.Connector] = _tuning.LiftPriority && _liftPriorityWaiting[lift.Index] > 0 ? LiftPriorityPenaltyS : 0;
        }
    }

    /// <summary>True when this person boards lifts first (D-247; with the switch off, only at accessible-priority presets).</summary>
    private bool HasLiftPriority(int i, SimLift lift) =>
        (_tuning.LiftPriority || lift.AccessiblePriority) && RoutePlanner.HasLiftPriority(_model.Profiles[_agents.Profile[i]].Template);

    /// <summary>Places a person takes in a car (D-243).</summary>
    private double PlacesOf(int i)
    {
        double r = _agents.Radius[i] / LiftPlaceRadiusM;
        return Math.Max(0.5, r * r);
    }

    /// <summary>The stop a rider or waiter wants to get off at (from its planned exit landing), or −1.</summary>
    private int ExitStopOf(int i, int lift)
    {
        int exit = _agents.PlannedExit[i];
        return LiftOfPortal(exit) == lift ? _world.Portals[exit].LiftStop : -1;
    }

    /// <summary>+1 up, −1 down, 0 unknown, for someone waiting at a landing.</summary>
    private int WaitDirection(int i, int lift, int stop)
    {
        int exitStop = ExitStopOf(i, lift);
        return exitStop < 0 || exitStop == stop ? 0 : exitStop > stop ? 1 : -1;
    }

    /// <summary>Time of day on the study clock, for the dispatch schedule (D-238).</summary>
    private TimeSpan ClockTimeOfDay()
    {
        double seconds = (_model.StartTimeOfDay.TotalSeconds + TimeS) % 86400.0;
        return TimeSpan.FromSeconds(seconds);
    }

    // ------------------------------------------------------------------ arriving at a landing

    /// <summary>
    /// Walking to a lift landing (journey bookkeeping): once on the landing's cells, start waiting there (D-239).
    /// </summary>
    private void CheckLiftArrival(int i)
    {
        AgentStore a = _agents;
        int target = a.Target[i];
        int lift = LiftOfPortal(target);
        if (a.Mode[i] != AgentMode.ToPortal || lift < 0)
        {
            return;
        }

        NavPortal portal = _world.Portals[target];
        if (a.Surface[i] != portal.Floor || _routes.FloorSide[target] is not { } field)
        {
            return;
        }

        NavSurface floor = _world.Surfaces[portal.Floor];
        int cell = floor.CellAt(a.X[i], a.Y[i]);
        if (cell < 0 || field.At(portal.Floor, cell) > 0f)
        {
            return;
        }

        StartLiftWait(i, lift, portal);
    }

    /// <summary>Stands on the landing (away from the doorway) until a car going their way has room.</summary>
    private void StartLiftWait(int i, int lift, NavPortal portal)
    {
        AgentStore a = _agents;
        SimLift bank = _model.Lifts[lift];
        int stop = portal.LiftStop;
        if (ExitStopOf(i, lift) is int exitStop && (exitStop < 0 || exitStop == stop))
        {
            a.PlannedExit[i] = BestLiftExit(i, bank, stop);
            if (a.PlannedExit[i] < 0)
            {
                return;
            }
        }

        a.Phase[i] = AgentPhase.LiftWait;
        a.LiftCar[i] = -1;
        a.JoinTime[i] = TimeS;
        NavSurface floor = _world.Surfaces[portal.Floor];
        Point2 spot = new(a.X[i], a.Y[i]);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Point2 c = floor.CellCentre(portal.FloorCells[_random.NextInt(portal.FloorCells.Length)]);
            double depth = Point2.Dot(c - portal.A, portal.Outward);
            spot = c;
            if (depth >= 0.8)
            {
                break;
            }
        }

        a.AimX[i] = spot.X;
        a.AimY[i] = spot.Y;
    }

    /// <summary>The landing of this bank on another stop that leaves the least time to the agent's target, or −1.</summary>
    private int BestLiftExit(int i, SimLift lift, int fromStop)
    {
        MobilityClass mobility = _model.Profiles[_agents.Profile[i]].Mobility;
        int best = -1;
        double bestCost = double.PositiveInfinity;
        for (int s = 0; s < lift.Stops; s++)
        {
            for (int side = 0; side < 2; side++)
            {
                int p = lift.PortalAt(s, side);
                if (s == fromStop || p < 0 || !_world.Portals[p].AllowsExit)
                {
                    continue;
                }

                double c = _routes.Remaining(mobility, _agents.Sink[i], p) + LiftTiming.TravelS(Math.Abs(lift.StopZ[s] - lift.StopZ[fromStop]), lift.SpeedMps, lift.AccelMps2);
                if (c < bestCost)
                {
                    bestCost = c;
                    best = p;
                }
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ the step

    /// <summary>Calls from the landings, assignment to cars, then each car's step (start of every step).</summary>
    private void UpdateLifts()
    {
        if (_carCount == 0)
        {
            return;
        }

        // Fresh neighbours for the doorway checks when people step out (the step's own hash comes later).
        _hash.Build(_agents.X, _agents.Y, _agents.Count);
        CountHallCalls();
        foreach (SimLift lift in _model.Lifts)
        {
            AssignCalls(lift);
            LiftDispatch mode = lift.DispatchAt(ClockTimeOfDay());
            for (int c = 0; c < lift.Cars; c++)
            {
                StepCar(lift, lift.FirstCar + c, mode);
            }
        }
    }

    /// <summary>Who is waiting where and which way (from scratch each step; hot path, no allocation).</summary>
    private void CountHallCalls()
    {
        foreach (SimLift lift in _model.Lifts)
        {
            Array.Clear(_hallCount[lift.Index]);
        }

        Array.Clear(_liftWaiting);
        Array.Clear(_liftPriorityWaiting);
        AgentStore a = _agents;
        for (int i = 0; i < a.Count; i++)
        {
            int lift = a.Phase[i] == AgentPhase.LiftWait ? LiftOfPortal(a.Target[i]) : -1;
            if (lift < 0)
            {
                continue;
            }

            int stop = _world.Portals[a.Target[i]].LiftStop;
            int dir = WaitDirection(i, lift, stop);
            _hallCount[lift][(stop * 2) + (dir >= 0 ? 0 : 1)]++;
            _liftWaiting[lift]++;
            if (HasLiftPriority(i, _model.Lifts[lift]))
            {
                _liftPriorityWaiting[lift]++;
            }
        }
    }

    /// <summary>Unassigned calls go to the car with the soonest estimated arrival; calls with no one waiting are dropped.</summary>
    private void AssignCalls(SimLift lift)
    {
        int[] counts = _hallCount[lift.Index], cars = _hallCar[lift.Index];
        for (int k = 0; k < counts.Length; k++)
        {
            if (counts[k] == 0)
            {
                cars[k] = -1;
                continue;
            }

            if (cars[k] >= 0)
            {
                continue;
            }

            int stop = k / 2, dir = k % 2 == 0 ? 1 : -1;
            double best = double.PositiveInfinity;
            for (int c = 0; c < lift.Cars; c++)
            {
                int car = lift.FirstCar + c;
                double eta = CallEta(lift, car, stop, dir);
                if (eta < best)
                {
                    best = eta;
                    cars[k] = car;
                }
            }
        }
    }

    /// <summary>Rough seconds until a car could open at a stop for a call going <paramref name="dir"/>.</summary>
    private double CallEta(SimLift lift, int car, int stop, int dir)
    {
        double dz = lift.StopZ[stop] - _carZ[car];
        double eta = Math.Abs(dz) / lift.SpeedMps;
        int pending = 0;
        int[] calls = _carCalls[car];
        for (int s = 0; s < calls.Length; s++)
        {
            pending += calls[s] > 0 ? 1 : 0;
        }

        int[] assigned = _hallCar[lift.Index];
        for (int k = 0; k < assigned.Length; k++)
        {
            pending += assigned[k] == car ? 1 : 0;
        }

        eta += pending * (lift.DoorOpenS + lift.DwellS + lift.DoorCloseS);
        int carDir = _carDir[car];
        if (carDir != 0 && (Math.Sign(dz) == -carDir || (Math.Abs(dz) < CarArriveEpsM && carDir != dir)))
        {
            // Heading away (or leaving this stop the other way): it has to finish its run first.
            eta += (2 * Math.Abs(lift.StopZ[^1] - lift.StopZ[0]) / lift.SpeedMps) + 10;
        }

        if (_carLoad[car] >= LiftFullShare * lift.Capacity)
        {
            eta += 120;
        }

        return eta;
    }

    /// <summary>True when the car has a reason to stop at <paramref name="stop"/>: a rider getting off, or a call assigned to it.</summary>
    private bool CarWantsStop(SimLift lift, int car, int stop) =>
        _carCalls[car][stop] > 0 || _hallCar[lift.Index][stop * 2] == car || _hallCar[lift.Index][(stop * 2) + 1] == car;

    /// <summary>
    /// True when the car should open at the stop it's at: a rider gets off, or a call assigned to it here goes its way
    /// (any way when it has none). A call the other way waits until it comes back. A car standing with riders after its
    /// doors closed doesn't reopen for calls (those left were full out or going the other way), so it can't cycle.
    /// </summary>
    private bool WantsOpenHere(SimLift lift, int car, int stop, bool idle)
    {
        if (_carCalls[car][stop] > 0)
        {
            return true;
        }

        if (idle && _carRiders[car] > 0)
        {
            return false;
        }

        int dir = _carDir[car];
        int[] hall = _hallCar[lift.Index];
        return (dir >= 0 && hall[stop * 2] == car) || (dir <= 0 && hall[(stop * 2) + 1] == car);
    }

    /// <summary>
    /// The next stop under the collective rule: going up, the nearest stop above with a rider getting off or an up
    /// call, else the highest stop above with any call (then it turns); likewise down. Idle cars take the nearest
    /// request. Moving cars only look at stops they can still brake for. −1 when nothing is wanted.
    /// </summary>
    private int NextStop(SimLift lift, int car, bool moving)
    {
        int dir = _carDir[car];
        double z = _carZ[car], v = Math.Abs(_carV[car]);
        double brake = moving ? (v * v / (2 * lift.AccelMps2)) + CarArriveEpsM : CarArriveEpsM;
        int[] hall = _hallCar[lift.Index];
        for (int pass = 0; pass < 2; pass++)
        {
            int d = dir != 0 ? dir : 0;
            if (d == 0)
            {
                // Idle: the nearest request either way, not counting where it stands (opening here is decided
                // separately, so a call it can't take here doesn't pin it).
                int nearest = -1;
                double gap = double.PositiveInfinity;
                for (int s = 0; s < lift.Stops; s++)
                {
                    if (CarWantsStop(lift, car, s) && Math.Abs(lift.StopZ[s] - z) >= brake && Math.Abs(lift.StopZ[s] - z) < gap)
                    {
                        gap = Math.Abs(lift.StopZ[s] - z);
                        nearest = s;
                    }
                }

                return nearest;
            }

            int nearestSame = -1, farthestAny = -1;
            for (int s = 0; s < lift.Stops; s++)
            {
                double ahead = (lift.StopZ[s] - z) * d;
                if (ahead < brake || !CarWantsStop(lift, car, s))
                {
                    continue;
                }

                bool same = _carCalls[car][s] > 0 || hall[(s * 2) + (d > 0 ? 0 : 1)] == car;
                if (same && (nearestSame < 0 || ahead < (lift.StopZ[nearestSame] - z) * d))
                {
                    nearestSame = s;
                }

                if (farthestAny < 0 || ahead > (lift.StopZ[farthestAny] - z) * d)
                {
                    farthestAny = s;
                }
            }

            if (nearestSame >= 0)
            {
                return nearestSame;
            }

            if (farthestAny >= 0)
            {
                return farthestAny;
            }

            if (moving)
            {
                return -1;
            }

            // Nothing ahead: turn round (once).
            dir = -dir;
            if (pass == 0)
            {
                _carDir[car] = (sbyte)dir;
            }
        }

        _carDir[car] = 0;
        return -1;
    }

    /// <summary>One car's step: door timers, getting off and on, choosing where to go, and moving.</summary>
    private void StepCar(SimLift lift, int car, LiftDispatch mode)
    {
        switch (_carState[car])
        {
            case LiftCarStatus.Idle:
            {
                int here = _carStop[car];
                if (WantsOpenHere(lift, car, here, idle: true) && lift.Serves(here))
                {
                    OpenDoors(lift, car);
                    break;
                }

                int next = NextStop(lift, car, moving: false);
                if (next < 0 && mode == LiftDispatch.UpPeak && here != lift.LobbyStop && _carRiders[car] == 0)
                {
                    next = lift.LobbyStop;
                }

                if (next >= 0 && next != here)
                {
                    _carTarget[car] = next;
                    _carDir[car] = (sbyte)Math.Sign(lift.StopZ[next] - _carZ[car]);
                    _carState[car] = LiftCarStatus.Moving;
                }

                break;
            }

            case LiftCarStatus.Moving:
                MoveCar(lift, car);
                break;

            case LiftCarStatus.Opening:
                if (TimeS >= _carTimer[car])
                {
                    _carState[car] = LiftCarStatus.Open;
                    _carHold[car] = TimeS + lift.DwellS;
                    _carNextTransfer[car] = TimeS;
                }

                break;

            case LiftCarStatus.Open:
                StepOpenCar(lift, car);
                break;

            case LiftCarStatus.Closing:
                if (TimeS >= _carTimer[car])
                {
                    _carState[car] = LiftCarStatus.Idle;
                }

                break;
        }
    }

    /// <summary>Trapezoidal travel to the target, re-picking a nearer stop it can still brake for (collective).</summary>
    private void MoveCar(SimLift lift, int car)
    {
        int better = NextStop(lift, car, moving: true);
        if (better >= 0 && (lift.StopZ[better] - _carZ[car]) * _carDir[car] < (lift.StopZ[_carTarget[car]] - _carZ[car]) * _carDir[car])
        {
            _carTarget[car] = better;
        }

        int target = _carTarget[car];
        double remaining = lift.StopZ[target] - _carZ[car];
        double speed = Math.Abs(_carV[car]);
        double wanted = Math.Min(lift.SpeedMps, Math.Sqrt(2 * lift.AccelMps2 * Math.Abs(remaining)));
        speed = speed < wanted ? Math.Min(wanted, speed + (lift.AccelMps2 * _dt)) : Math.Max(wanted, speed - (lift.AccelMps2 * _dt));
        double step = Math.Max(speed, 0.05) * _dt;
        if (Math.Abs(remaining) <= step + CarArriveEpsM)
        {
            _carZ[car] = lift.StopZ[target];
            _carV[car] = 0;
            _carStop[car] = target;
            ArriveCar(lift, car);
            return;
        }

        _carV[car] = Math.Sign(remaining) * speed;
        _carZ[car] += Math.Sign(remaining) * step;
        _carDir[car] = (sbyte)Math.Sign(remaining);
    }

    /// <summary>At a stop: decide which way it carries on (or none), then open if anyone gets off or on here.</summary>
    private void ArriveCar(SimLift lift, int car)
    {
        int here = _carStop[car];
        int dir = _carDir[car];
        bool ahead = false;
        for (int s = 0; s < lift.Stops; s++)
        {
            if (s != here && CarWantsStop(lift, car, s) && (lift.StopZ[s] - lift.StopZ[here]) * dir > 0)
            {
                ahead = true;
                break;
            }
        }

        if (!ahead)
        {
            // Last stop this way: it takes anyone here, whichever way they go (the first on sets the direction).
            _carDir[car] = 0;
        }

        if (lift.Serves(here) && (WantsOpenHere(lift, car, here, idle: false) || WaitersFor(lift, car, here) > 0))
        {
            OpenDoors(lift, car);
        }
        else
        {
            _carState[car] = LiftCarStatus.Idle;
        }
    }

    /// <summary>Starts opening; the calls it answers here stay its own until the doors close.</summary>
    private void OpenDoors(SimLift lift, int car)
    {
        _carState[car] = LiftCarStatus.Opening;
        _carTimer[car] = TimeS + lift.DoorOpenS;
    }

    /// <summary>People at this stop's landings who could board this car now (direction and room permitting).</summary>
    private int WaitersFor(SimLift lift, int car, int stop) => FindBoarder(lift, car, stop, out _) ? 1 : 0;

    /// <summary>Doors open: one person off (first) or on per <see cref="LiftTransferS"/>; close once no one is left to move.</summary>
    private void StepOpenCar(SimLift lift, int car)
    {
        int here = _carStop[car];
        bool moved = false;
        if (TimeS >= _carNextTransfer[car])
        {
            if (FindAlighter(lift, car, here) is int i and >= 0)
            {
                moved = TryAlight(lift, car, i);
            }
            else if (FindBoarder(lift, car, here, out int b))
            {
                Board(lift, car, b);
                moved = true;
            }
        }

        if (moved)
        {
            _carNextTransfer[car] = TimeS + LiftTransferS;
            _carHold[car] = Math.Max(_carHold[car], TimeS + LiftDoorHoldS);
            return;
        }

        if (TimeS >= _carHold[car] && TimeS >= _carNextTransfer[car] && FindAlighter(lift, car, here) < 0 && !FindBoarder(lift, car, here, out _))
        {
            _carState[car] = LiftCarStatus.Closing;
            _carTimer[car] = TimeS + lift.DoorCloseS;
            int[] hall = _hallCar[lift.Index];
            for (int d = 0; d < 2; d++)
            {
                if (hall[(here * 2) + d] == car)
                {
                    hall[(here * 2) + d] = -1;
                }
            }

            if (_carRiders[car] == 0 && NextStop(lift, car, moving: false) < 0)
            {
                _carDir[car] = 0;
            }
        }
    }

    /// <summary>A rider of this car getting off here, or −1.</summary>
    private int FindAlighter(SimLift lift, int car, int stop)
    {
        if (_carCalls[car][stop] == 0)
        {
            return -1;
        }

        AgentStore a = _agents;
        for (int i = 0; i < a.Count; i++)
        {
            if (a.Phase[i] == AgentPhase.LiftRide && a.LiftCar[i] == car && ExitStopOf(i, lift.Index) == stop)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The next person to board here (D-239): waiting at an entry landing of this stop, going the car's way (any way
    /// when it has none), fitting the room left; people who need accessible devices first at every lift (D-247), then
    /// arrival order.
    /// </summary>
    private bool FindBoarder(SimLift lift, int car, int stop, out int boarder)
    {
        boarder = -1;
        AgentStore a = _agents;
        double room = lift.Capacity - _carLoad[car];
        if (room <= 0)
        {
            return false;
        }

        int p0 = lift.PortalAt(stop, 0), p1 = lift.PortalAt(stop, 1);
        int dir = _carDir[car];
        bool bestPriority = false;
        double bestJoin = double.PositiveInfinity;
        for (int i = 0; i < a.Count; i++)
        {
            int target = a.Target[i];
            if (a.Phase[i] != AgentPhase.LiftWait || target < 0 || (target != p0 && target != p1) || !_world.Portals[target].AllowsEntry)
            {
                continue;
            }

            int wants = WaitDirection(i, lift.Index, stop);
            // Someone always fits an empty car, however big the preset's places say they are.
            if ((dir != 0 && wants != dir) || (_carRiders[car] > 0 && PlacesOf(i) > room + 1e-9))
            {
                continue;
            }

            bool priority = HasLiftPriority(i, lift);
            if ((priority && !bestPriority) || (priority == bestPriority && a.JoinTime[i] < bestJoin))
            {
                boarder = i;
                bestPriority = priority;
                bestJoin = a.JoinTime[i];
            }
        }

        return boarder >= 0;
    }

    /// <summary>Into the car: held at a spot inside its footprint, counted against its places and its stops.</summary>
    private void Board(SimLift lift, int car, int i)
    {
        AgentStore a = _agents;
        int exitStop = ExitStopOf(i, lift.Index);
        if (_carDir[car] == 0 && exitStop >= 0)
        {
            _carDir[car] = (sbyte)Math.Sign(lift.StopZ[exitStop] - lift.StopZ[_carStop[car]]);
        }

        LiftCarLayout layout = lift.CarLayouts[car - lift.FirstCar];
        double u = (_random.NextDouble() - 0.5) * 1.4 * Math.Max(0, layout.HalfWidth - a.Radius[i]);
        double v = (_random.NextDouble() - 0.5) * 1.4 * Math.Max(0, layout.HalfDepth - a.Radius[i]);
        Point2 spot = layout.Centre + (layout.Along * u) + (layout.Inward * v);
        a.Phase[i] = AgentPhase.LiftRide;
        a.LiftCar[i] = car;
        a.X[i] = spot.X;
        a.Y[i] = spot.Y;
        a.Vx[i] = a.Vy[i] = 0;
        a.Crowd[i] = 0;
        _carLoad[car] += PlacesOf(i);
        _carRiders[car]++;
        if (exitStop >= 0)
        {
            _carCalls[car][exitStop]++;
        }
    }

    /// <summary>
    /// Out onto the landing just past the door on the rider's exit side, if there's room; then they choose their way
    /// on from the new floor. False when the doorway is blocked this step.
    /// </summary>
    private bool TryAlight(SimLift lift, int car, int i)
    {
        AgentStore a = _agents;
        int exit = a.PlannedExit[i];
        NavPortal portal = _world.Portals[exit];
        LiftCarLayout layout = lift.CarLayouts[car - lift.FirstCar];
        if (layout.Door(portal.LiftSide) is not { } door)
        {
            return false;
        }

        NavSurface floor = _world.Surfaces[portal.Floor];
        Point2 mid = (door.A + door.B) * 0.5;
        Point2 along = door.B - door.A;
        along = along.Length > 1e-9 ? along * (1 / along.Length) : new Point2(1, 0);
        double radius = a.Radius[i];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            double depth = radius + 0.25 + (0.475 * attempt);
            Point2 spot = mid + (portal.Outward * depth) + (along * ((_random.NextDouble() - 0.5) * 0.6));
            if (!floor.IsWalkable(spot.X, spot.Y) || Overlaps(spot.X, spot.Y, floor.Z0, radius))
            {
                continue;
            }

            a.Phase[i] = AgentPhase.Walk;
            a.LiftCar[i] = -1;
            a.Surface[i] = portal.Floor;
            a.X[i] = spot.X;
            a.Y[i] = spot.Y;
            a.Z[i] = floor.ZAt(spot.X, spot.Y);
            a.Vx[i] = portal.Outward.X * 0.6;
            a.Vy[i] = portal.Outward.Y * 0.6;
            a.OffPortal[i] = exit;
            a.OffTime[i] = TimeS;
            _carLoad[car] = Math.Max(0, _carLoad[car] - PlacesOf(i));
            _carRiders[car] = Math.Max(0, _carRiders[car] - 1);
            _carCalls[car][portal.LiftStop] = Math.Max(0, _carCalls[car][portal.LiftStop] - 1);
            Decide(i, _random);
            return true;
        }

        return false;
    }
}
