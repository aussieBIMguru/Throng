using Throng.Sim.Engine;

namespace Throng.Sim.Replay;

/// <summary>
/// A run's replay (round 023, D-373–D-375): a frame every <see cref="FrameIntervalS"/> simulated seconds, captured on
/// the simulation thread with <see cref="CaptureIfDue"/>, read on any thread with <see cref="TryReadAgents"/> (people
/// interpolated between frames by serial) and <see cref="StateAt"/>. Past <see cref="CapBytes"/> the older half of the
/// frames is thinned (every other one dropped), so the frame rate halves for the oldest part each time and memory
/// stays bounded. Only reads the engine: recording never changes a run.
/// </summary>
public sealed class ReplayRecording
{
    /// <summary>Simulated seconds between frames.</summary>
    public const double FrameIntervalS = 0.5;

    /// <summary>Memory the frames may hold before thinning: 1 GB.</summary>
    public const long DefaultCapBytes = 1L << 30;

    /// <summary>Values per lift car in a frame: height, state, riders, direction, door share (0–1).</summary>
    public const int CarStride = 5;

    private readonly object _gate = new();
    private readonly List<ReplayFrame> _frames = [];
    private readonly List<AgentLook> _colours = [];
    private readonly Dictionary<AgentLook, ushort> _colourIndex = new();
    private long _bytes;
    private double _nextDueS;

    // Simulation-thread scratch.
    private float[] _snapshot = new float[64 * SimEngine.FloatsPerAgent];

    // Reader cache: the serial → index map of the frame last interpolated towards.
    private ReplayFrame? _mapFrame;
    private readonly Dictionary<int, int> _map = new();

    /// <summary>Creates an empty recording.</summary>
    /// <param name="capBytes">Memory the frames may hold before the older half thins.</param>
    public ReplayRecording(long capBytes = DefaultCapBytes) => CapBytes = Math.Max(1, capBytes);

    /// <summary>Memory the frames may hold before thinning.</summary>
    public long CapBytes { get; }

    /// <summary>Number of frames held.</summary>
    public int FrameCount
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count;
            }
        }
    }

    /// <summary>Approximate memory the frames hold, bytes.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Time of the first frame, seconds (0 when empty).</summary>
    public double StartS
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count > 0 ? _frames[0].TimeS : 0;
            }
        }
    }

    /// <summary>Time of the latest frame, seconds (0 when empty).</summary>
    public double EndS
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count > 0 ? _frames[^1].TimeS : 0;
            }
        }
    }

    /// <summary>Drops every frame (RESET).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
            _colours.Clear();
            _colourIndex.Clear();
            _bytes = 0;
            _nextDueS = 0;
            _mapFrame = null;
            _map.Clear();
        }
    }

    /// <summary>Captures a frame when the engine has reached the next frame time (call on the engine's thread after each step).</summary>
    /// <returns>True when a frame was captured.</returns>
    public bool CaptureIfDue(SimEngine engine)
    {
        double now = engine.TimeS;
        if (now + 1e-9 < Volatile.Read(ref _nextDueS))
        {
            return false;
        }

        Capture(engine);
        return true;
    }

    /// <summary>Captures a frame of the engine now (call on the engine's thread).</summary>
    public void Capture(SimEngine engine)
    {
        int n = engine.WriteSnapshot(ref _snapshot);
        int[] serials = engine.Agents.Serial;
        const int F = SimEngine.FloatsPerAgent;
        var agents = new PackedAgent[n];
        var cars = new double[engine.LiftCarCount * CarStride];
        for (int c = 0; c < engine.LiftCarCount; c++)
        {
            (int _, double z, LiftCarStatus state, int riders, int direction) = engine.LiftCarState(c);
            int o = c * CarStride;
            cars[o] = z;
            cars[o + 1] = (int)state;
            cars[o + 2] = riders;
            cars[o + 3] = direction;
            cars[o + 4] = engine.LiftDoorShare(c);
        }

        var trains = new byte[engine.Model.Trains.Count];
        for (int v = 0; v < trains.Length; v++)
        {
            (bool isIn, bool boarding) = engine.TrainState(v);
            trains[v] = (byte)(!isIn ? 0 : boarding ? 2 : 1);
        }

        SimStats stats = engine.Stats;
        double time = engine.TimeS;
        lock (_gate)
        {
            for (int i = 0; i < n; i++)
            {
                int o = i * F;
                var look = new AgentLook(_snapshot[o + 4], _snapshot[o + 5], _snapshot[o + 6]);
                if (!_colourIndex.TryGetValue(look, out ushort colour))
                {
                    colour = (ushort)Math.Min(_colours.Count, ushort.MaxValue);
                    if (_colours.Count < ushort.MaxValue)
                    {
                        _colours.Add(look);
                        _colourIndex[look] = colour;
                    }
                }

                agents[i] = new PackedAgent
                {
                    Serial = serials[i],
                    X = _snapshot[o],
                    Y = _snapshot[o + 1],
                    Z = (Half)_snapshot[o + 2],
                    Heading = (Half)_snapshot[o + 3],
                    Colour = colour,
                    RadiusCm = ToByte(_snapshot[o + 7] * 100),
                    Height2Cm = ToByte(_snapshot[o + 8] * 50),
                    Stuck = _snapshot[o + 9] > 0.5f ? (byte)1 : (byte)0,
                    Stress = ToByte(_snapshot[o + 10] * 255),
                    GroupHue = ToByte(_snapshot[o + 11]),
                };
            }

            var frame = new ReplayFrame(time, stats, agents, cars, trains);
            _frames.Add(frame);
            _bytes += frame.Bytes;
            _nextDueS = (Math.Floor((time + 1e-9) / FrameIntervalS) + 1) * FrameIntervalS;
            Thin();
        }
    }

    /// <summary>
    /// Writes everyone at <paramref name="timeS"/> into <paramref name="buffer"/> in snapshot layout
    /// (<see cref="SimEngine.FloatsPerAgent"/> floats each, grown when needed): the people of the frame at or before that
    /// time, moved part-way towards where the next frame has them. False when there are no frames.
    /// </summary>
    public bool TryReadAgents(double timeS, ref float[] buffer, out int count)
    {
        count = 0;
        lock (_gate)
        {
            int a = FrameAt(timeS);
            if (a < 0)
            {
                return false;
            }

            ReplayFrame from = _frames[a];
            ReplayFrame? to = a + 1 < _frames.Count ? _frames[a + 1] : null;
            double f = to is null || to.TimeS <= from.TimeS ? 0 : Math.Clamp((timeS - from.TimeS) / (to.TimeS - from.TimeS), 0, 1);
            if (to is not null && f > 0 && !ReferenceEquals(to, _mapFrame))
            {
                _map.Clear();
                for (int i = 0; i < to.Agents.Length; i++)
                {
                    _map[to.Agents[i].Serial] = i;
                }

                _mapFrame = to;
            }

            const int F = SimEngine.FloatsPerAgent;
            int n = from.Agents.Length;
            if (buffer.Length < n * F)
            {
                buffer = new float[Math.Max(n * F * 2, 64 * F)];
            }

            for (int i = 0; i < n; i++)
            {
                PackedAgent p = from.Agents[i];
                float x = p.X, y = p.Y, z = (float)p.Z, heading = (float)p.Heading;
                if (to is not null && f > 0 && _map.TryGetValue(p.Serial, out int j))
                {
                    PackedAgent q = to.Agents[j];
                    x += (float)((q.X - x) * f);
                    y += (float)((q.Y - y) * f);
                    z += (float)(((float)q.Z - z) * f);
                    heading = LerpAngle(heading, (float)q.Heading, (float)f);
                }

                AgentLook look = p.Colour < _colours.Count ? _colours[p.Colour] : new AgentLook(1, 1, 1);
                int o = i * F;
                buffer[o] = x;
                buffer[o + 1] = y;
                buffer[o + 2] = z;
                buffer[o + 3] = heading;
                buffer[o + 4] = look.R;
                buffer[o + 5] = look.G;
                buffer[o + 6] = look.B;
                buffer[o + 7] = p.RadiusCm / 100f;
                buffer[o + 8] = p.Height2Cm / 50f;
                buffer[o + 9] = p.Stuck;
                buffer[o + 10] = p.Stress / 255f;
                buffer[o + 11] = p.GroupHue;
            }

            count = n;
            return true;
        }
    }

    /// <summary>
    /// Counters, lift cars and trains at <paramref name="timeS"/>: the frame at or before it, with car heights and doors
    /// moved part-way to the next frame. Null when there are no frames.
    /// </summary>
    public ReplayState? StateAt(double timeS)
    {
        lock (_gate)
        {
            int a = FrameAt(timeS);
            if (a < 0)
            {
                return null;
            }

            ReplayFrame from = _frames[a];
            ReplayFrame? to = a + 1 < _frames.Count ? _frames[a + 1] : null;
            double f = to is null || to.TimeS <= from.TimeS ? 0 : Math.Clamp((timeS - from.TimeS) / (to.TimeS - from.TimeS), 0, 1);
            var cars = (double[])from.Cars.Clone();
            if (to is not null && f > 0 && to.Cars.Length == cars.Length)
            {
                for (int o = 0; o < cars.Length; o += CarStride)
                {
                    cars[o] += (to.Cars[o] - cars[o]) * f;
                    cars[o + 4] += (to.Cars[o + 4] - cars[o + 4]) * f;
                }
            }

            double shown = Math.Max(from.TimeS, timeS);
            return new ReplayState(shown, from.Stats with { TimeS = shown }, cars, (byte[])from.Trains.Clone());
        }
    }

    /// <summary>Index of the last frame at or before <paramref name="timeS"/> (the first frame when earlier); −1 when empty. Call under the lock.</summary>
    private int FrameAt(double timeS)
    {
        if (_frames.Count == 0)
        {
            return -1;
        }

        int lo = 0, hi = _frames.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_frames[mid].TimeS <= timeS + 1e-9)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>Drops every other frame in the older half while over the cap (keeps the first and latest). Call under the lock.</summary>
    private void Thin()
    {
        while (_bytes > CapBytes && _frames.Count >= 4)
        {
            int half = _frames.Count / 2;
            var kept = new List<ReplayFrame>(_frames.Count);
            for (int i = 0; i < _frames.Count; i++)
            {
                if (i < half && i % 2 == 1)
                {
                    _bytes -= _frames[i].Bytes;
                    continue;
                }

                kept.Add(_frames[i]);
            }

            if (kept.Count == _frames.Count)
            {
                break;
            }

            _frames.Clear();
            _frames.AddRange(kept);
            _mapFrame = null;
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    /// <summary>Turns from <paramref name="a"/> towards <paramref name="b"/> the short way round (radians).</summary>
    private static float LerpAngle(float a, float b, float f)
    {
        double d = b - a;
        d = Math.IEEERemainder(d, 2 * Math.PI);
        return (float)(a + (d * f));
    }
}
