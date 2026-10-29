namespace Throng.Sim.Engine;

/// <summary>What an agent is currently walking towards (round 006 design).</summary>
public enum AgentMode : byte
{
    /// <summary>Straight to its sink on this floor.</summary>
    Direct,

    /// <summary>To a connector entry on this floor (<see cref="AgentStore.Target"/> is the portal).</summary>
    ToPortal,

    /// <summary>Along a connector to step off at a portal (<see cref="AgentStore.Target"/>).</summary>
    OnConnector,
}

/// <summary>
/// Agents as structure-of-arrays (BRIEF §17): every per-agent value in its own array, grown by doubling and
/// compacted by swap-remove, so the step loop touches contiguous memory and allocates nothing.
/// </summary>
public sealed class AgentStore
{
    private int _capacity;

    /// <summary>Creates an empty store.</summary>
    public AgentStore(int capacity = 256)
    {
        _capacity = Math.Max(16, capacity);
        Allocate(_capacity);
    }

    /// <summary>Agents in the store.</summary>
    public int Count { get; private set; }

    /// <summary>Plan position.</summary>
    public double[] X = [], Y = [];

    /// <summary>Walking velocity (relative to any belt underfoot).</summary>
    public double[] Vx = [], Vy = [];

    /// <summary>Surface index.</summary>
    public int[] Surface = [];

    /// <summary>Current mode.</summary>
    public AgentMode[] Mode = [];

    /// <summary>Portal the mode targets (ToPortal, OnConnector).</summary>
    public int[] Target = [];

    /// <summary>Exit portal planned when heading for an entry.</summary>
    public int[] PlannedExit = [];

    /// <summary>Destination sink.</summary>
    public int[] Sink = [];

    /// <summary>Profile index.</summary>
    public int[] Profile = [];

    /// <summary>Free walking speed, m/s.</summary>
    public float[] V0 = [];

    /// <summary>Body radius, m.</summary>
    public float[] Radius = [];

    /// <summary>Height, m.</summary>
    public float[] Height = [];

    /// <summary>Walks on escalators (D-107).</summary>
    public bool[] Walks = [];

    /// <summary>Simulated time the agent appeared, s.</summary>
    public double[] SpawnTime = [];

    /// <summary>Lowest field cost reached in the current mode (progress tracking, D-106).</summary>
    public float[] Best = [];

    /// <summary>Seconds since the last progress.</summary>
    public float[] Since = [];

    /// <summary>
    /// Field cost (route seconds left) at the previous step, for the progress pace (D-191); +∞ after a change of
    /// target or a pause, so the jump isn't counted.
    /// </summary>
    public float[] PrevCost = [];

    /// <summary>
    /// Smoothed progress along the route as a share of the agent's own free pace (D-191): 1 = walking freely towards
    /// its target, about 0 = shuffling on the spot however much it jiggles.
    /// </summary>
    public float[] Pace = [];

    /// <summary>Local density around the agent, people per m² (D-194), updated each step.</summary>
    public float[] Crowd = [];

    /// <summary>Facing, radians counter-clockwise from +X.</summary>
    public float[] Heading = [];

    /// <summary>Height above datum, refreshed every step.</summary>
    public double[] Z = [];

    /// <summary>Portal the agent last stepped off a connector at (−1 once it boards again), for the run-off rules (D-112).</summary>
    public int[] OffPortal = [];

    /// <summary>Simulated time of that step off, s.</summary>
    public double[] OffTime = [];

    /// <summary>Where the agent stepped onto a landing, and its travel direction then (zero once it's carried on; D-137).</summary>
    public double[] JointX = [], JointY = [], JointDx = [], JointDy = [];

    /// <summary>How far to carry straight into the landing before turning, m (D-137, D-192).</summary>
    public float[] JointDepth = [];

    /// <summary>Stress (frustration) 0–1 (D-145).</summary>
    public float[] Stress = [];

    /// <summary>Weighted seconds spent held up, decaying in free flow; drives <see cref="Stress"/> (D-145).</summary>
    public float[] HeldS = [];

    /// <summary>
    /// Distance left to the far point at the last checkpoint while walking through a gate (D-209): the walk-through
    /// timeout only runs out after this long without getting closer.
    /// </summary>
    public float[] ThroughLeft = [];

    /// <summary>Emitter that spawned the agent (its destinations decide the exit).</summary>
    public int[] Emitter = [];

    /// <summary>Journey index, or −1 to walk straight to a sink (D-149).</summary>
    public int[] Journey = [];

    /// <summary>Current journey step index.</summary>
    public int[] Step = [];

    /// <summary>Tag bits carried (D-150).</summary>
    public ulong[] Tags = [];

    /// <summary>What the agent is doing about its step.</summary>
    public AgentPhase[] Phase = [];

    /// <summary>Device it is using (GoToDevice steps), or −1.</summary>
    public int[] QDevice = [];

    /// <summary>Queue line (device-local line index), or −1 when not in a line.</summary>
    public int[] QLine = [];

    /// <summary>Position in the line (0 = head).</summary>
    public int[] QSlot = [];

    /// <summary>Side of the device it queues on (0 = service side).</summary>
    public int[] QSide = [];

    /// <summary>End time of the current service, dwell or step-aside, s; or the start of queueing.</summary>
    public double[] Timer = [];

    /// <summary>When it joined its queue line, s (queue wait diagnostics).</summary>
    public double[] JoinTime = [];

    /// <summary>Point it is heading for while queueing, walking through, stepping aside or dwelling.</summary>
    public double[] AimX = [], AimY = [];

    /// <summary>Density map cell it stood on at the last map sample, or −1 (footfall trail, D-216).</summary>
    public int[] TrailCell = [];

    /// <summary>Bits of the measurement zones the agent stood in at the last count (D-228).</summary>
    public ulong[] ZoneBits = [];

    /// <summary>Longest single queue wait so far, s (journey results, D-229).</summary>
    public float[] MaxWaitS = [];

    /// <summary>True once its patience ran out in a line and it headed for an exit (journey results, D-229).</summary>
    public bool[] LeftEarly = [];

    /// <summary>Lift car (global car index) it's waiting for or riding, or −1 (round 013).</summary>
    public int[] LiftCar = [];

    /// <summary>Adds an agent with default state and returns its index.</summary>
    public int Add()
    {
        if (Count == _capacity)
        {
            _capacity *= 2;
            Allocate(_capacity);
        }

        int i = Count++;
        X[i] = Y[i] = Vx[i] = Vy[i] = Z[i] = 0;
        Surface[i] = 0;
        Mode[i] = AgentMode.Direct;
        Target[i] = PlannedExit[i] = Sink[i] = Profile[i] = OffPortal[i] = -1;
        OffTime[i] = 0;
        JointX[i] = JointY[i] = JointDx[i] = JointDy[i] = 0;
        JointDepth[i] = 0;
        V0[i] = 1.34f;
        Radius[i] = 0.25f;
        Height[i] = 1.75f;
        Walks[i] = false;
        SpawnTime[i] = 0;
        Best[i] = float.PositiveInfinity;
        Since[i] = 0;
        PrevCost[i] = float.PositiveInfinity;
        Pace[i] = 1;
        Crowd[i] = 0;
        Heading[i] = 0;
        Stress[i] = HeldS[i] = ThroughLeft[i] = 0;
        Emitter[i] = Journey[i] = QDevice[i] = QLine[i] = -1;
        Step[i] = QSlot[i] = QSide[i] = 0;
        Tags[i] = 0;
        Phase[i] = AgentPhase.Walk;
        Timer[i] = JoinTime[i] = AimX[i] = AimY[i] = 0;
        TrailCell[i] = -1;
        ZoneBits[i] = 0;
        MaxWaitS[i] = 0;
        LeftEarly[i] = false;
        LiftCar[i] = -1;
        return i;
    }

    /// <summary>Removes an agent by moving the last one into its slot.</summary>
    public void RemoveAt(int i)
    {
        int last = --Count;
        if (i == last)
        {
            return;
        }

        X[i] = X[last];
        Y[i] = Y[last];
        Vx[i] = Vx[last];
        Vy[i] = Vy[last];
        Surface[i] = Surface[last];
        Mode[i] = Mode[last];
        Target[i] = Target[last];
        PlannedExit[i] = PlannedExit[last];
        Sink[i] = Sink[last];
        Profile[i] = Profile[last];
        V0[i] = V0[last];
        Radius[i] = Radius[last];
        Height[i] = Height[last];
        Walks[i] = Walks[last];
        SpawnTime[i] = SpawnTime[last];
        Best[i] = Best[last];
        Since[i] = Since[last];
        PrevCost[i] = PrevCost[last];
        Pace[i] = Pace[last];
        Crowd[i] = Crowd[last];
        Heading[i] = Heading[last];
        Z[i] = Z[last];
        OffPortal[i] = OffPortal[last];
        OffTime[i] = OffTime[last];
        JointX[i] = JointX[last];
        JointY[i] = JointY[last];
        JointDx[i] = JointDx[last];
        JointDepth[i] = JointDepth[last];
        JointDy[i] = JointDy[last];
        Stress[i] = Stress[last];
        HeldS[i] = HeldS[last];
        ThroughLeft[i] = ThroughLeft[last];
        Emitter[i] = Emitter[last];
        Journey[i] = Journey[last];
        Step[i] = Step[last];
        Tags[i] = Tags[last];
        Phase[i] = Phase[last];
        QDevice[i] = QDevice[last];
        QLine[i] = QLine[last];
        QSlot[i] = QSlot[last];
        QSide[i] = QSide[last];
        Timer[i] = Timer[last];
        JoinTime[i] = JoinTime[last];
        AimX[i] = AimX[last];
        AimY[i] = AimY[last];
        TrailCell[i] = TrailCell[last];
        ZoneBits[i] = ZoneBits[last];
        MaxWaitS[i] = MaxWaitS[last];
        LeftEarly[i] = LeftEarly[last];
        LiftCar[i] = LiftCar[last];
    }

    /// <summary>Removes every agent.</summary>
    public void Clear() => Count = 0;

    private void Allocate(int capacity)
    {
        Array.Resize(ref X, capacity);
        Array.Resize(ref Y, capacity);
        Array.Resize(ref Vx, capacity);
        Array.Resize(ref Vy, capacity);
        Array.Resize(ref Surface, capacity);
        Array.Resize(ref Mode, capacity);
        Array.Resize(ref Target, capacity);
        Array.Resize(ref PlannedExit, capacity);
        Array.Resize(ref Sink, capacity);
        Array.Resize(ref Profile, capacity);
        Array.Resize(ref V0, capacity);
        Array.Resize(ref Radius, capacity);
        Array.Resize(ref Height, capacity);
        Array.Resize(ref Walks, capacity);
        Array.Resize(ref SpawnTime, capacity);
        Array.Resize(ref Best, capacity);
        Array.Resize(ref Since, capacity);
        Array.Resize(ref PrevCost, capacity);
        Array.Resize(ref Pace, capacity);
        Array.Resize(ref Crowd, capacity);
        Array.Resize(ref Heading, capacity);
        Array.Resize(ref Z, capacity);
        Array.Resize(ref OffPortal, capacity);
        Array.Resize(ref OffTime, capacity);
        Array.Resize(ref JointX, capacity);
        Array.Resize(ref JointY, capacity);
        Array.Resize(ref JointDx, capacity);
        Array.Resize(ref JointDepth, capacity);
        Array.Resize(ref JointDy, capacity);
        Array.Resize(ref Stress, capacity);
        Array.Resize(ref HeldS, capacity);
        Array.Resize(ref ThroughLeft, capacity);
        Array.Resize(ref Emitter, capacity);
        Array.Resize(ref Journey, capacity);
        Array.Resize(ref Step, capacity);
        Array.Resize(ref Tags, capacity);
        Array.Resize(ref Phase, capacity);
        Array.Resize(ref QDevice, capacity);
        Array.Resize(ref QLine, capacity);
        Array.Resize(ref QSlot, capacity);
        Array.Resize(ref QSide, capacity);
        Array.Resize(ref Timer, capacity);
        Array.Resize(ref JoinTime, capacity);
        Array.Resize(ref AimX, capacity);
        Array.Resize(ref AimY, capacity);
        Array.Resize(ref TrailCell, capacity);
        Array.Resize(ref ZoneBits, capacity);
        Array.Resize(ref MaxWaitS, capacity);
        Array.Resize(ref LeftEarly, capacity);
        Array.Resize(ref LiftCar, capacity);
    }
}
