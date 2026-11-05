using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Engine;

/// <summary>
/// Groups (round 024, D-382–D-389): people who arrive, walk, queue and ride together. A group line spawns its members
/// side by side; they share a journey, tags, sink and every journey decision (made by whichever member gets there
/// first and kept per node for the rest, D-389); they walk at the slowest member's pace with a pull toward the
/// group's centre and the ones ahead easing off (D-383, after Moussaïd et al. 2010); one member's route is followed
/// by the others on the same surface; at devices they queue together and wait past it for the rest, balk and give up
/// together, and board lifts and trains all at once or not at all (D-384).
/// </summary>
/// <remarks>
/// Groups live in a slot table (reused once a group's last member leaves); members are found through a per-step index
/// (agents are swap-removed, so nothing keeps agent indices across steps).
/// </remarks>
public sealed partial class SimEngine
{
    /// <summary>Most seconds a member waits past a device for the rest of the group (D-384).</summary>
    public const double RegroupMaxS = 120;

    /// <summary>How far past a device a member stands to wait for the rest, metres (D-384).</summary>
    public const double RegroupAheadM = 1.0;

    /// <summary>Group mates further apart than this don't pull on each other (out of view), metres (D-383).</summary>
    public const double GroupViewM = 8.0;

    // Cohesion (D-383): pull toward the centre past (N − 1) / 2 m; ease off when more than GroupAheadM ahead of the
    // centre (down to GroupMinScale of the pace over GroupSlowRangeM); catch up a little when well behind; mates push
    // each other at this share of the social force so they can walk side by side.
    private const double GroupPullAccel = 1.2, GroupAheadM = 1.0, GroupSlowRangeM = 2.0, GroupMinScale = 0.25;
    private const double GroupCatchUp = 1.25, GroupBehindM = 1.5, GroupSocialFactor = 0.5;

    // Placing members around the first at spawn: within this ring, this many tries each.
    private const double GroupSpawnMinM = 0.5, GroupSpawnMaxM = 1.6;
    private const int GroupSpawnTries = 10;

    private const int NoChoice = int.MinValue;

    // Group table, per slot.
    private int _groupSlots;
    private int[] _gAlive = [];
    private int[][] _gChoice = [];
    private ushort[] _gTag = [];
    private readonly Stack<int> _gFree = new();
    private long _groupNumber;

    // Per-step member index: members of slot g are _gList[_gFirst[g] .. _gFirst[g + 1]).
    private int[] _gFirst = [0];
    private int[] _gFill = [];
    private int[] _gList = [];
    private int _indexedSlots;

    // Spawning: a group line picked but not yet placed keeps its roll until it fits (per emitter; −1 = none).
    private int[] _heldLine = [];
    private int[]?[] _heldCounts = [];

    // Spawn scratch.
    private readonly List<(int Profile, double X, double Y, double Radius)> _placing = [];

    /// <summary>Groups in the study now (diagnostics and tests).</summary>
    public int GroupCount => _groupSlots - _gFree.Count;

    /// <summary>Most group tags before they wrap round (round 024b).</summary>
    public const int MaxGroupTag = ushort.MaxValue;

    /// <summary>
    /// The tag of an agent's group (1–65535, the group's number counted from the start of the run, wrapping), or 0 for
    /// people on their own (round 024b). It goes in the snapshot and the replay: mates share it, so hover tethers find
    /// them, and COLOUR ▸ GROUP turns it into a hue (D-391).
    /// </summary>
    public ushort GroupTag(int i) => _agents.Group[i] is int g and >= 0 && g < _gTag.Length ? _gTag[g] : (ushort)0;

    /// <summary>The profile that makes agent <paramref name="i"/>'s choices (D-387).</summary>
    private SimProfile RouteOf(int i)
    {
        int r = _agents.Route[i];
        IReadOnlyList<SimProfile> routes = _model.RouteProfiles;
        return r >= 0 && r < routes.Count ? routes[r] : _model.Profiles[_agents.Profile[i]];
    }

    private void ResetGroups()
    {
        _groupSlots = 0;
        _gFree.Clear();
        _groupNumber = 0;
        int emitters = _model.Emitters.Count;
        if (_heldLine.Length != emitters)
        {
            _heldLine = new int[emitters];
            _heldCounts = new int[]?[emitters];
        }

        Array.Fill(_heldLine, -1);
        IndexGroups();
    }

    /// <summary>A free group slot for a group following journey <paramref name="journey"/> (decisions cleared).</summary>
    private int NewGroup(int journey)
    {
        int g;
        if (_gFree.Count > 0)
        {
            g = _gFree.Pop();
        }
        else
        {
            g = _groupSlots++;
            if (g >= _gAlive.Length)
            {
                int size = Math.Max(16, _gAlive.Length * 2);
                Array.Resize(ref _gAlive, size);
                Array.Resize(ref _gChoice, size);
                Array.Resize(ref _gTag, size);
                EnsureGroupResultSlots(size);
            }
        }

        int keys = journey >= 0 ? _model.Journeys[journey].Nodes.Length + _model.Journeys[journey].Steps.Length + 1 : 1;
        if (_gChoice[g] is null || _gChoice[g].Length < keys)
        {
            _gChoice[g] = new int[keys];
        }

        Array.Fill(_gChoice[g], NoChoice);
        _gAlive[g] = 0;

        // A tag per group, unique among any 65535 in a row; the app turns it into a hue (D-391).
        _gTag[g] = (ushort)(1 + (_groupNumber++ % MaxGroupTag));
        return g;
    }

    /// <summary>One fewer member in group <paramref name="g"/> (left the study); the slot is freed with the last.</summary>
    private void LeaveGroup(int g)
    {
        if (g >= 0 && g < _groupSlots && --_gAlive[g] == 0)
        {
            _gFree.Push(g);
        }
    }

    /// <summary>Rebuilds the per-step member index (call after agents are added or removed).</summary>
    private void IndexGroups()
    {
        int slots = _groupSlots;
        if (_gFirst.Length < slots + 1)
        {
            _gFirst = new int[Math.Max(16, (slots + 1) * 2)];
            _gFill = new int[_gFirst.Length];
        }

        Array.Clear(_gFirst, 0, slots + 1);
        AgentStore a = _agents;
        int members = 0;
        for (int i = 0; i < a.Count; i++)
        {
            int g = a.Group[i];
            if (g >= 0 && g < slots)
            {
                _gFirst[g + 1]++;
                members++;
            }
        }

        for (int g = 0; g < slots; g++)
        {
            _gFirst[g + 1] += _gFirst[g];
            _gFill[g] = _gFirst[g];
        }

        if (_gList.Length < members)
        {
            _gList = new int[Math.Max(64, members * 2)];
        }

        for (int i = 0; i < a.Count; i++)
        {
            int g = a.Group[i];
            if (g >= 0 && g < slots)
            {
                _gList[_gFill[g]++] = i;
            }
        }

        _indexedSlots = slots;
    }

    /// <summary>The members of agent <paramref name="i"/>'s group (including <paramref name="i"/>); empty when on their own.</summary>
    private ReadOnlySpan<int> MatesOf(int i)
    {
        int g = _agents.Group[i];
        if (g < 0 || g >= _indexedSlots)
        {
            // On their own, or a group spawned this step that the index doesn't hold yet.
            return ReadOnlySpan<int>.Empty;
        }

        return new ReadOnlySpan<int>(_gList, _gFirst[g], _gFirst[g + 1] - _gFirst[g]);
    }

    // ------------------------------------------------------------------ shared decisions (D-389)

    /// <summary>
    /// The group's decision at journey key <paramref name="key"/> (a node index, or an exit key), when a member has
    /// already made it. Always false for people on their own, so their runs draw exactly as before.
    /// </summary>
    private bool Recall(int i, int key, out int value)
    {
        value = NoChoice;
        int g = _agents.Group[i];
        if (g < 0 || g >= _groupSlots || key < 0 || key >= _gChoice[g].Length)
        {
            return false;
        }

        value = _gChoice[g][key];
        return value != NoChoice;
    }

    /// <summary>Keeps a member's decision at <paramref name="key"/> for the rest of the group.</summary>
    private void Remember(int i, int key, int value)
    {
        int g = _agents.Group[i];
        if (g >= 0 && g < _groupSlots && key >= 0 && key < _gChoice[g].Length)
        {
            _gChoice[g][key] = value;
        }
    }

    /// <summary>The decision key of heading for a sink: after the nodes, one per sink step and one for the rule's sinks.</summary>
    private int ExitKey(int i, int sinkStep)
    {
        int j = _agents.Journey[i];
        if (j < 0)
        {
            return 0;
        }

        SimJourney journey = _model.Journeys[j];
        return journey.Nodes.Length + (sinkStep >= 0 && sinkStep < journey.Steps.Length ? sinkStep : journey.Steps.Length);
    }

    // ------------------------------------------------------------------ spawning (D-382, D-386)

    /// <summary>
    /// Places a group of line <paramref name="lineIndex"/> at emitter <paramref name="e"/>: counts per row rolled (or kept from a
    /// try that didn't fit), the first member on a random free cell, the rest in a ring round them. All or none: false
    /// when the zone is too crowded (the roll is kept for the next try).
    /// </summary>
    private int TrySpawnGroup(int e, int lineIndex)
    {
        SimEmitter emitter = _model.Emitters[e];
        SimMixLine line = emitter.Lines[lineIndex];
        DeterministicRandom random = _ruleRandom[e];
        int[] counts;
        if (_heldLine[e] == lineIndex && _heldCounts[e] is { } held)
        {
            counts = held;
        }
        else
        {
            var rows = new (int Min, int Max)[line.Members.Length];
            for (int k = 0; k < rows.Length; k++)
            {
                rows[k] = (line.Members[k].Min, line.Members[k].Max);
            }

            counts = GroupTemplate.Roll(rows, random.NextDouble);
        }

        NavSurface floor = _world.Surfaces[emitter.Floor];
        _placing.Clear();
        bool placed = false;
        for (int attempt = 0; attempt < SpawnTries && !placed; attempt++)
        {
            _placing.Clear();
            placed = true;
            for (int k = 0; k < counts.Length && placed; k++)
            {
                int profile = line.Members[k].Profile;
                double radius = Math.Clamp(_model.Profiles[profile].Template.RadiusM, 0.1, 1.0);
                for (int c = 0; c < counts[k]; c++)
                {
                    if (!PlaceMember(floor, emitter, random, profile, radius))
                    {
                        placed = false;
                        break;
                    }
                }
            }
        }

        if (!placed || _placing.Count == 0)
        {
            _heldLine[e] = lineIndex;
            _heldCounts[e] = counts;
            return 0;
        }

        _heldLine[e] = -1;
        _heldCounts[e] = null;

        // Shared at spawn: journey, tags, the slowest member's pace (D-383) and the lead's escalator habit.
        int g = NewGroup(line.Journey);
        ulong tags = SpawnTags(line, random);
        bool walks = random.NextDouble() < _model.Profiles[line.Profile].Template.EscalatorWalkProbability;
        double pace = double.PositiveInfinity;
        Span<double> speeds = stackalloc double[_placing.Count];
        for (int m = 0; m < _placing.Count; m++)
        {
            AgentProfileTemplate t = _model.Profiles[_placing[m].Profile].Template;
            speeds[m] = Math.Clamp(DistributionSampler.Sample(t.Speed, random), 0.3, 3.0);
            pace = Math.Min(pace, speeds[m]);
        }

        int route = line.RouteProfile >= 0 ? line.RouteProfile : line.Profile;
        for (int m = 0; m < _placing.Count; m++)
        {
            (int profile, double x, double y, double radius) = _placing[m];
            AgentProfileTemplate t = _model.Profiles[profile].Template;
            int i = _agents.Add();
            _agents.X[i] = x;
            _agents.Y[i] = y;
            _agents.Z[i] = floor.Z0;
            _agents.Surface[i] = emitter.Floor;
            _agents.Profile[i] = profile;
            _agents.Route[i] = route;
            _agents.Group[i] = g;
            _agents.Emitter[i] = e;
            _agents.Journey[i] = line.Journey;
            _agents.Tags[i] = tags;
            _agents.V0[i] = (float)pace;
            _agents.Radius[i] = (float)radius;
            _agents.Height[i] = (float)Math.Clamp(t.HeightM, 0.5, 2.5);
            _agents.Walks[i] = walks;
            _agents.SpawnTime[i] = TimeS;
            _agents.ZoneBits[i] = ZoneBitsAt(emitter.Floor, x, y);
            _agents.Heading[i] = (float)(random.NextDouble() * 2 * Math.PI);
            _gAlive[g]++;
            StartJourney(i, random);
            _justSpawned.Add(i);
            _spawned++;
        }

        // Group results (round 024b, D-390): timed from now until the last member leaves.
        StartGroupResults(g, line, _placing.Count);
        return _placing.Count;
    }

    /// <summary>A free spot for the next member: the first anywhere in the zone, the rest in a ring round the first.</summary>
    private bool PlaceMember(NavSurface floor, SimEmitter emitter, DeterministicRandom random, int profile, double radius)
    {
        for (int t = 0; t < GroupSpawnTries; t++)
        {
            double x, y;
            if (_placing.Count == 0 || t >= GroupSpawnTries - 2)
            {
                Point2 c = floor.CellCentre(emitter.Cells[random.NextInt(emitter.Cells.Length)]);
                x = c.X + ((random.NextDouble() - 0.5) * floor.CellU * 0.8);
                y = c.Y + ((random.NextDouble() - 0.5) * floor.CellV * 0.8);
            }
            else
            {
                double angle = random.NextDouble() * 2 * Math.PI;
                double distance = GroupSpawnMinM + (random.NextDouble() * (GroupSpawnMaxM - GroupSpawnMinM));
                x = _placing[0].X + (Math.Cos(angle) * distance);
                y = _placing[0].Y + (Math.Sin(angle) * distance);
            }

            if (!floor.IsWalkable(x, y) || Overlaps(x, y, floor.Z0, radius) || OverlapsPlacing(x, y, radius))
            {
                continue;
            }

            _placing.Add((profile, x, y, radius));
            return true;
        }

        return false;
    }

    private bool OverlapsPlacing(double x, double y, double radius)
    {
        foreach ((int _, double px, double py, double pr) in _placing)
        {
            double dx = px - x, dy = py - y, r = pr + radius;
            if ((dx * dx) + (dy * dy) < r * r)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ walking together (D-383)

    /// <summary>
    /// Following the group (D-383): a member walking on the same surface as a mate with an earlier serial, heading for
    /// the same target, takes that mate's route (mode, target, planned exit) instead of choosing its own. True when it
    /// did (the member then skips its own review).
    /// </summary>
    private bool FollowGuide(int i)
    {
        AgentStore a = _agents;
        if (a.Group[i] < 0 || a.Phase[i] != AgentPhase.Walk)
        {
            return false;
        }

        int guide = -1;
        foreach (int m in MatesOf(i))
        {
            if (m != i && a.Serial[m] < a.Serial[i] && a.Phase[m] == AgentPhase.Walk && a.Surface[m] == a.Surface[i] && a.Sink[m] == a.Sink[i]
                && (guide < 0 || a.Serial[m] < a.Serial[guide]))
            {
                guide = m;
            }
        }

        if (guide < 0 || (a.Target[guide] < 0 && a.Mode[guide] != AgentMode.Direct))
        {
            return false;
        }

        if (a.Mode[i] != a.Mode[guide] || a.Target[i] != a.Target[guide])
        {
            SetMode(i, a.Mode[guide], a.Target[guide]);
        }

        a.PlannedExit[i] = a.PlannedExit[guide];
        return true;
    }

    /// <summary>
    /// Cohesion (D-383) for a walking member: the pull toward the centre of the mates in view (past (N − 1) / 2 m), and a
    /// pace scale: easing off when ahead of the centre along the walking direction, catching up a little when behind.
    /// </summary>
    private void GroupPull(int i, double ex, double ey, out double scale, out double pullX, out double pullY)
    {
        AgentStore a = _agents;
        scale = 1;
        pullX = pullY = 0;
        double x = a.X[i], y = a.Y[i], z = a.Z[i];
        double sx = x, sy = y;
        int n = 0;
        foreach (int m in MatesOf(i))
        {
            if (m == i || a.Phase[m] == AgentPhase.LiftRide || Math.Abs(a.Z[m] - z) >= LevelBand)
            {
                continue;
            }

            double dx = a.X[m] - x, dy = a.Y[m] - y;
            if ((dx * dx) + (dy * dy) > GroupViewM * GroupViewM)
            {
                continue;
            }

            sx += a.X[m];
            sy += a.Y[m];
            n++;
        }

        if (n == 0)
        {
            return;
        }

        double cx = sx / (n + 1), cy = sy / (n + 1);
        double ox = cx - x, oy = cy - y;
        double distance = Math.Sqrt((ox * ox) + (oy * oy));
        double threshold = 0.5 * n;
        if (distance > threshold && distance > 1e-6)
        {
            double k = GroupPullAccel * Math.Min(1.0, distance - threshold) / distance;
            pullX = ox * k;
            pullY = oy * k;
        }

        if (ex == 0 && ey == 0)
        {
            return;
        }

        double ahead = -((ox * ex) + (oy * ey));
        if (ahead > GroupAheadM)
        {
            scale = Math.Max(GroupMinScale, 1 - ((ahead - GroupAheadM) / GroupSlowRangeM));
        }
        else if (ahead < -GroupBehindM)
        {
            scale = GroupCatchUp;
        }
    }

    // ------------------------------------------------------------------ devices (D-384)

    /// <summary>The lane of <paramref name="device"/> a mate of <paramref name="i"/> is lined up in on <paramref name="side"/>, or −1.</summary>
    private int MateLane(int i, SimDevice device, int side)
    {
        AgentStore a = _agents;
        foreach (int m in MatesOf(i))
        {
            if (m != i && a.QDevice[m] == device.Index && a.QLine[m] >= 0 && a.QLine[m] != device.SnakeLine
                && a.Phase[m] is AgentPhase.Queue or AgentPhase.Service && a.QLine[m] / device.Lanes == side)
            {
                return a.QLine[m] % device.Lanes;
            }
        }

        return -1;
    }

    /// <summary>True when a mate of <paramref name="i"/> is being served at (or walking through) <paramref name="device"/>.</summary>
    private bool MateServedAt(int i, int device)
    {
        AgentStore a = _agents;
        foreach (int m in MatesOf(i))
        {
            if (m != i && a.QDevice[m] == device && a.Phase[m] is AgentPhase.Service or AgentPhase.Through)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while a mate of <paramref name="i"/> still has device <paramref name="device"/> to get through.</summary>
    private bool MateStillAt(int i, int device)
    {
        AgentStore a = _agents;
        foreach (int m in MatesOf(i))
        {
            if (m != i && a.QDevice[m] == device
                && a.Phase[m] is AgentPhase.Walk or AgentPhase.WaitJoin or AgentPhase.Queue or AgentPhase.Service or AgentPhase.Through)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Just past device <paramref name="device"/> and walking on: when a mate still has it to get through, stand a little
    /// further on and wait for them (D-384).
    /// </summary>
    private void MaybeRegroup(int i, int device)
    {
        AgentStore a = _agents;
        a.LastDevice[i] = device;
        if (a.Group[i] < 0 || device < 0 || a.Phase[i] != AgentPhase.Walk || !MateStillAt(i, device))
        {
            return;
        }

        double x = a.X[i], y = a.Y[i];
        double heading = a.Heading[i];
        double ax = x + (Math.Cos(heading) * RegroupAheadM), ay = y + (Math.Sin(heading) * RegroupAheadM);
        NavSurface s = _world.Surfaces[a.Surface[i]];
        a.Phase[i] = AgentPhase.Regroup;
        a.AimX[i] = s.IsWalkable(ax, ay) ? ax : x;
        a.AimY[i] = s.IsWalkable(ax, ay) ? ay : y;
        a.Timer[i] = TimeS + RegroupMaxS;
    }

    /// <summary>Waiting past a device: walk on once nobody in the group still has it to get through, or after the cap.</summary>
    private void UpdateRegroup(int i)
    {
        AgentStore a = _agents;
        if (TimeS >= a.Timer[i] || !MateStillAt(i, a.LastDevice[i]))
        {
            a.Phase[i] = AgentPhase.Walk;
        }
    }

    /// <summary>
    /// A member balked from <paramref name="from"/> to <paramref name="to"/> (D-384): the group's choice at that node is
    /// now <paramref name="to"/>, and mates still heading for or waiting at <paramref name="from"/> for the same step
    /// switch with them.
    /// </summary>
    private void BalkMates(int i, SimStep step, int from, int to)
    {
        AgentStore a = _agents;
        if (a.Group[i] < 0)
        {
            return;
        }

        Remember(i, step.Node, to);
        foreach (int m in MatesOf(i))
        {
            if (m == i || a.QDevice[m] != from || a.Step[m] != a.Step[i] || a.Phase[m] is not (AgentPhase.Walk or AgentPhase.WaitJoin or AgentPhase.Queue))
            {
                continue;
            }

            if (a.Phase[m] == AgentPhase.Queue)
            {
                LeaveLine(m);
            }

            RecordBalk(from);
            CountBalk(m, step, from, to);
            a.Phase[m] = AgentPhase.Walk;
            a.QDevice[m] = to;
            a.Sink[m] = _model.Devices[to].Target;
            Decide(m, _random);
        }
    }

    /// <summary>
    /// A member gave up at device <paramref name="device"/> (D-384): mates heading for or waiting at it for the same step
    /// give up with them (those already served carry on).
    /// </summary>
    private void GiveUpMates(int i, int stepIndex, int device)
    {
        AgentStore a = _agents;
        if (a.Group[i] < 0)
        {
            return;
        }

        foreach (int m in MatesOf(i))
        {
            if (m != i && a.QDevice[m] == device && a.Step[m] == stepIndex
                && a.Phase[m] is AgentPhase.Walk or AgentPhase.WaitJoin or AgentPhase.Queue
                && CurrentStep(m) is { Kind: JourneyStepKind.GoToDevice } step)
            {
                GiveUp(m, step);
            }
        }
    }

    // ------------------------------------------------------------------ lifts and trains (D-384)

    /// <summary>
    /// Lifts take a group all at once or not at all (D-384): true when <paramref name="i"/> may board car
    /// <paramref name="car"/> as far as its group goes: a mate already got on it here, or every mate coming to this landing
    /// is waiting on it and they all fit in <paramref name="room"/>. A group too big for any car boards as people on their
    /// own. <paramref name="mateAboard"/> says a mate is already in the car (they board next).
    /// </summary>
    private bool GroupReadyForLift(int i, int car, int p0, int p1, double room, double capacity, out bool mateAboard)
    {
        AgentStore a = _agents;
        mateAboard = false;
        if (a.Group[i] < 0)
        {
            return true;
        }

        double need = 0;
        foreach (int m in MatesOf(i))
        {
            if (a.Phase[m] == AgentPhase.LiftRide)
            {
                mateAboard |= a.LiftCar[m] == car;
                continue;
            }

            bool here = a.Target[m] == p0 || a.Target[m] == p1;
            if (!here)
            {
                continue;
            }

            if (a.Phase[m] == AgentPhase.LiftWait)
            {
                need += PlacesOf(m);
            }
            else if (a.Phase[m] == AgentPhase.Walk && a.Mode[m] == AgentMode.ToPortal)
            {
                // Still on the way to this landing: wait for them.
                return false;
            }
        }

        return mateAboard || need > capacity + 1e-9 || need <= room + 1e-9;
    }

    /// <summary>Destination dispatch (D-255): true when a mate of <paramref name="i"/> was given car <paramref name="car"/>.</summary>
    private bool MateGivenCar(int i, int car)
    {
        AgentStore a = _agents;
        foreach (int m in MatesOf(i))
        {
            if (m != i && a.LiftCar[m] == car && a.Phase[m] is AgentPhase.LiftWait or AgentPhase.LiftRide)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Trains take a group all at once or not at all (D-384): true when every mate leaving by service <paramref name="v"/>
    /// is waiting by a door or within reach of theirs, and the train has room for all of them (a group bigger than the
    /// whole train boards as people on their own).
    /// </summary>
    private bool GroupReadyForTrain(int i, int v)
    {
        AgentStore a = _agents;
        if (a.Group[i] < 0)
        {
            return true;
        }

        int count = 0;
        SimTrain train = _model.Trains[v];
        foreach (int m in MatesOf(i))
        {
            if (DoorTrainOf(a.Sink[m]) != v || CurrentKind(m) is not (JourneyStepKind.Exit or JourneyStepKind.BoardTrain))
            {
                continue;
            }

            count++;
            if (m == i || a.Phase[m] == AgentPhase.WaitTrain)
            {
                continue;
            }

            int door = DoorIndex(train, a.Sink[m]);
            if (a.Surface[m] != train.Floor || door < 0 || Point2.Distance(new Point2(a.X[m], a.Y[m]), train.DoorCentres[door]) > TrainWaitNearM)
            {
                return false;
            }
        }

        return count > train.Capacity || _trainBoarded[v] + count <= train.Capacity;
    }
}
