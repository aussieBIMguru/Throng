using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// Round 010b, trains (D-184–D-187): each service's clock (arrive, people get off over a few seconds, boarding once
/// that eases, doors close), boarders waiting beside the doors, boarding up to the train's places, and counting who
/// was left behind. A door zone owned by a service is a sink only while that service is boarding.
/// </summary>
public sealed partial class SimEngine
{
    /// <summary>Boarding starts once this share of the getting-off time has passed (alight first, then board, D-186).</summary>
    public const double BoardStartShare = 0.75;

    /// <summary>People heading for a door start waiting when within this distance of it and boarding isn't open, metres.</summary>
    public const double TrainWaitNearM = 6.0;

    // Waiting spots: this far in from the door (plus up to the spread), and this far past the door's end along the
    // edge (plus up to the spread), so the doorway stays clear for people getting off.
    private const double WaitInM = 1.2, WaitInSpreadM = 1.5, WaitAsideM = 0.5, WaitAsideSpreadM = 1.2;

    private int[] _doorTrain = [];
    private int[] _trainNumber = [];
    private int[] _alightTarget = [];
    private int[] _alightReleased = [];
    private int[] _trainBoarded = [];
    private bool[] _trainIn = [];
    private bool[] _trainBoarding = [];
    private long _leftBehind, _boardedTrains;

    /// <summary>People still waiting on the platform when their train left (D-187), since the start.</summary>
    public long LeftBehind => _leftBehind;

    /// <summary>People who boarded a train, since the start.</summary>
    public long BoardedTrains => _boardedTrains;

    /// <summary>Whether service <paramref name="train"/> has a train in, and whether it's boarding (D-190).</summary>
    public (bool In, bool Boarding) TrainState(int train) =>
        train >= 0 && train < _trainIn.Length ? (_trainIn[train], _trainBoarding[train]) : (false, false);

    /// <summary>People who boarded the service's current (or last) train.</summary>
    public int BoardedOnTrain(int train) => train >= 0 && train < _trainBoarded.Length ? _trainBoarded[train] : 0;

    /// <summary>Sets up the train arrays (called from the constructor).</summary>
    private void InitTrains()
    {
        int n = _model.Trains.Count;
        _doorTrain = new int[_world.Sinks.Count];
        Array.Fill(_doorTrain, -1);
        foreach (SimTrain train in _model.Trains)
        {
            foreach (int sink in train.DoorSinks)
            {
                _doorTrain[sink] = train.Index;
            }
        }

        _trainNumber = new int[n];
        _alightTarget = new int[n];
        _alightReleased = new int[n];
        _trainBoarded = new int[n];
        _trainIn = new bool[n];
        _trainBoarding = new bool[n];
    }

    private void ResetTrains()
    {
        Array.Clear(_trainNumber);
        Array.Fill(_alightTarget, -1);
        Array.Clear(_alightReleased);
        Array.Clear(_trainBoarded);
        Array.Clear(_trainIn);
        Array.Clear(_trainBoarding);
        _leftBehind = _boardedTrains = 0;
    }

    /// <summary>
    /// Advances every service's clock for this step: departures (counting who was left behind), arrivals (how many
    /// get off, from the load curve), releasing people off the train through the doors in turn, and boarding.
    /// </summary>
    private void UpdateTrains()
    {
        double t = TimeS;
        for (int v = 0; v < _model.Trains.Count; v++)
        {
            SimTrain train = _model.Trains[v];
            double arrive = train.OffsetS + (_trainNumber[v] * train.HeadwayS);
            if (t >= arrive + train.DwellS)
            {
                Depart(v, arrive + train.DwellS);
                _trainNumber[v]++;
                _alightTarget[v] = -1;
                _alightReleased[v] = 0;
                _trainBoarded[v] = 0;
                arrive = train.OffsetS + (_trainNumber[v] * train.HeadwayS);
            }

            _trainIn[v] = t >= arrive && t < arrive + train.DwellS;
            if (!_trainIn[v])
            {
                _trainBoarding[v] = false;
                continue;
            }

            if (_alightTarget[v] < 0)
            {
                double multiplier = Math.Max(0, RateCurves.MultiplierAt(train.CurveMinutes, train.CurveMultipliers, arrive / 60.0));
                _alightTarget[v] = (int)Math.Round(train.AlightPerTrain * multiplier);
            }

            ReleaseAlighters(v, train, t - arrive);
            _trainBoarding[v] = t - arrive >= BoardStartShare * train.AlightOverS;
        }
    }

    /// <summary>Hands people off the train to the doors' emitters in turn, evenly over the getting-off time.</summary>
    private void ReleaseAlighters(int v, SimTrain train, double sinceArrival)
    {
        double share = Math.Min(1.0, (sinceArrival + _dt) / Math.Max(_dt, train.AlightOverS));
        int due = (int)Math.Floor(_alightTarget[v] * share);
        int doors = train.DoorSinks.Length;
        int guard = 0;
        while (_alightReleased[v] < due && guard++ < doors * 4)
        {
            (int Emitter, double Weight)[] options = train.DoorEmitters[_alightReleased[v] % doors];
            _alightReleased[v]++;
            if (options.Length == 0)
            {
                continue;
            }

            int e = options[0].Emitter;
            if (options.Length > 1)
            {
                double total = 0;
                foreach ((int _, double w) in options)
                {
                    total += w;
                }

                double pick = _random.NextDouble() * total;
                foreach ((int option, double w) in options)
                {
                    e = option;
                    pick -= w;
                    if (pick < 0)
                    {
                        break;
                    }
                }
            }

            if (_pending[e] < MaxPendingPerRule)
            {
                _pending[e]++;
            }

            guard = 0;
        }
    }

    /// <summary>Doors close: everyone still waiting for this service since before they closed was left behind (D-187).</summary>
    private void Depart(int v, double closedAt)
    {
        AgentStore a = _agents;
        for (int i = 0; i < a.Count; i++)
        {
            if (a.Phase[i] == AgentPhase.WaitTrain && DoorTrainOf(a.Sink[i]) == v && a.JoinTime[i] < closedAt - 1.0)
            {
                _leftBehind++;
            }
        }

        _trainIn[v] = false;
        _trainBoarding[v] = false;
    }

    /// <summary>The service whose door a route target is, or −1.</summary>
    private int DoorTrainOf(int sink) => sink >= 0 && sink < _doorTrain.Length ? _doorTrain[sink] : -1;

    /// <summary>True while service <paramref name="v"/> takes boarders and has room.</summary>
    private bool CanBoard(int v) => _trainBoarding[v] && _trainBoarded[v] < _model.Trains[v].Capacity;

    /// <summary>True when the agent is on its way to leave by a train door (an exit or Board train step).</summary>
    private bool HeadingForTrain(int i, out int v)
    {
        v = DoorTrainOf(_agents.Sink[i]);
        return v >= 0 && CurrentKind(i) is JourneyStepKind.Exit or JourneyStepKind.BoardTrain;
    }

    /// <summary>
    /// While walking to a train door: start waiting once near it if the train can't take them yet. While waiting: go
    /// and board as soon as it can.
    /// </summary>
    private void UpdateTrainWait(int i)
    {
        AgentStore a = _agents;
        if (!HeadingForTrain(i, out int v))
        {
            return;
        }

        if (a.Phase[i] == AgentPhase.WaitTrain)
        {
            if (CanBoard(v))
            {
                a.Phase[i] = AgentPhase.Walk;
                Decide(i, _random);
            }

            return;
        }

        if (a.Phase[i] == AgentPhase.Walk && !CanBoard(v) && a.Surface[i] == _model.Trains[v].Floor
            && DoorIndex(_model.Trains[v], a.Sink[i]) is int k and >= 0
            && Point2.Distance(new Point2(a.X[i], a.Y[i]), _model.Trains[v].DoorCentres[k]) < TrainWaitNearM)
        {
            StartWaitingForTrain(i, v, k);
        }
    }

    /// <summary>Reached a train door while walking: board when the train can take them (true = leave), else wait.</summary>
    private bool ArriveAtTrainDoor(int i, int v)
    {
        if (CanBoard(v))
        {
            _trainBoarded[v]++;
            _boardedTrains++;
            return true;
        }

        StartWaitingForTrain(i, v, DoorIndex(_model.Trains[v], _agents.Sink[i]));
        return false;
    }

    /// <summary>Stands aside of the door (D-186): in from the platform edge and past the door's end, on a random side.</summary>
    private void StartWaitingForTrain(int i, int v, int door)
    {
        AgentStore a = _agents;
        SimTrain train = _model.Trains[v];
        a.Phase[i] = AgentPhase.WaitTrain;
        a.JoinTime[i] = TimeS;
        door = Math.Max(0, door);
        Point2 centre = train.DoorCentres[door];
        NavSurface floor = _world.Surfaces[train.Floor];
        double side = _random.NextDouble() < 0.5 ? -1 : 1;
        Point2 spot = centre + (train.Inward * (WaitInM + (_random.NextDouble() * WaitInSpreadM)))
            + (train.Along * (side * (train.DoorHalfWidths[door] + WaitAsideM + (_random.NextDouble() * WaitAsideSpreadM))));
        if (!floor.IsWalkable(spot.X, spot.Y))
        {
            spot = centre + (train.Inward * (WaitInM + WaitInSpreadM));
        }

        if (!floor.IsWalkable(spot.X, spot.Y))
        {
            spot = new Point2(a.X[i], a.Y[i]);
        }

        a.AimX[i] = spot.X;
        a.AimY[i] = spot.Y;
    }

    /// <summary>Position of a door sink in the service's door list, or −1.</summary>
    private static int DoorIndex(SimTrain train, int sink)
    {
        for (int k = 0; k < train.DoorSinks.Length; k++)
        {
            if (train.DoorSinks[k] == sink)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>
    /// A Board train step (D-186): the quickest door of the step's services from here, plus each door's crowd in sight
    /// (D-182), so people spread to quieter doors. −1 when none can be reached.
    /// </summary>
    private int ChooseTrainDoor(int i, SimStep step, DeterministicRandom random)
    {
        AgentStore a = _agents;
        SimProfile profile = _model.Profiles[a.Profile[i]];
        double sight = Sight(i);
        int best = -1;
        double bestCost = double.PositiveInfinity;
        foreach (int sink in step.Targets)
        {
            double c = RoutePlanner.Choose(_routes, a.Surface[i], a.X[i], a.Y[i], sink, profile.Mobility, profile.Template, random, out _, out _, _wait,
                BeltWalkSpeed(i), stairEffort: _tuning.StairEffort, standingRest: _tuning.StandingRest, sightRadius: sight) + ExitCrowdFor(i, sink);
            if (c < bestCost)
            {
                bestCost = c;
                best = sink;
            }
        }

        return double.IsFinite(bestCost) ? best : -1;
    }
}
