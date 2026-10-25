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
        V0[i] = 1.34f;
        Radius[i] = 0.25f;
        Height[i] = 1.75f;
        Walks[i] = false;
        SpawnTime[i] = 0;
        Best[i] = float.PositiveInfinity;
        Since[i] = 0;
        Heading[i] = 0;
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
        Heading[i] = Heading[last];
        Z[i] = Z[last];
        OffPortal[i] = OffPortal[last];
        OffTime[i] = OffTime[last];
        JointX[i] = JointX[last];
        JointY[i] = JointY[last];
        JointDx[i] = JointDx[last];
        JointDy[i] = JointDy[last];
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
        Array.Resize(ref Heading, capacity);
        Array.Resize(ref Z, capacity);
        Array.Resize(ref OffPortal, capacity);
        Array.Resize(ref OffTime, capacity);
        Array.Resize(ref JointX, capacity);
        Array.Resize(ref JointY, capacity);
        Array.Resize(ref JointDx, capacity);
        Array.Resize(ref JointDy, capacity);
    }
}
