using System.Diagnostics;
using Throng.Sim.Engine;

namespace Throng.Sim;

/// <summary>Run speed: a multiple of real time, or as fast as possible (D-104).</summary>
/// <param name="Multiplier">Simulated seconds per real second; ignored when <paramref name="IsMax"/>.</param>
/// <param name="IsMax">Run flat out, publishing about every 30 ms.</param>
public readonly record struct RunSpeed(double Multiplier, bool IsMax)
{
    /// <summary>Real time.</summary>
    public static RunSpeed RealTime => new(1, false);

    /// <summary>As fast as possible.</summary>
    public static RunSpeed Max => new(1, true);
}

/// <summary>
/// Runs a <see cref="SimEngine"/> on its own background thread (BRIEF §17) with play, pause, single step, reset and
/// speed control, publishing a double-buffered snapshot of agent render values and counters for the UI to copy.
/// </summary>
/// <remarks>
/// The engine is only touched by the runner's thread. Commands are flags read at the top of each loop; snapshots
/// are swapped under a lock, and <see cref="CopySnapshot"/> copies the front buffer under the same lock, so the UI
/// never sees a half-written frame. A fixed-duration run pauses itself at the end (D-103). Exceptions on the thread
/// pause the run and are reported through <see cref="Faulted"/> (the app logs them).
/// </remarks>
public sealed class SimRunner : IDisposable
{
    private const double MaxBatchMs = 30;

    private readonly SimEngine _engine;
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private float[] _front = new float[64 * SimEngine.FloatsPerAgent];
    private float[] _back = new float[64 * SimEngine.FloatsPerAgent];
    private int _frontCount;
    private SimStats _frontStats;
    private long _version;
    private volatile bool _playing;
    private volatile bool _stop;
    private int _stepRequests;
    private volatile bool _resetRequested;
    private RunSpeed _speed = RunSpeed.RealTime;

    // Crowd waits for the run-time hover label (D-183): connectors first, then route targets, published with each
    // snapshot; the id lookup is built once.
    private readonly Dictionary<Guid, int> _waitIndex = new();
    private double[] _frontWaits = [];
    private double[] _backWaits = [];

    // Train states for the train box (D-190): 0 = no train, 1 = in, 2 = in and boarding.
    private byte[] _frontTrains = [];
    private byte[] _backTrains = [];
    private double[] _frontCars = [];
    private double[] _backCars = [];

    /// <summary>Values per car in the published car buffers: height, state, riders, direction, door opening (0–1).</summary>
    private const int CarStride = 5;

    // Density maps (D-202, D-216): every measure back to back (DensityMeasure order, CellCount each), copied only
    // when the engine has sampled since the last publish.
    private static readonly DensityMeasure[] Measures = Enum.GetValues<DensityMeasure>();
    private float[] _frontDensity = [];
    private float[] _backDensity = [];
    private long _frontDensityVersion = -1;
    private long _backDensityVersion = -1;
    private double _frontAccumulatedS;
    private double _backAccumulatedS;

    // Run results (round 011c): an immutable copy, rebuilt at most every ResultsIntervalMs while playing.
    private const long ResultsIntervalMs = 500;
    private readonly Stopwatch _resultsClock = Stopwatch.StartNew();
    private RunResults _frontResults = RunResults.Empty;
    private long _publishedResultsVersion = -1;

    /// <summary>Creates a paused runner and starts its thread.</summary>
    public SimRunner(SimModel model)
    {
        _engine = new SimEngine(model);
        DensityLayout = _engine.Density;
        _frontDensity = new float[DensityLayout.CellCount * Measures.Length];
        _backDensity = new float[DensityLayout.CellCount * Measures.Length];
        BuildWaitIndex();
        Publish();
        _thread = new Thread(Loop) { IsBackground = true, Name = "Throng simulation" };
        _thread.Start();
    }

    /// <summary>Raised on the runner's thread when the engine throws; the run is paused.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>Raised on the runner's thread when a fixed-duration run reaches its end and pauses.</summary>
    public event Action? Finished;

    /// <summary>The model being run.</summary>
    public SimModel Model => _engine.Model;

    /// <summary>
    /// The density map's layers and cell kinds (fixed for the run, safe to read from any thread). Its values come from
    /// <see cref="CopyDensity"/>, never read them here directly.
    /// </summary>
    public DensityGrid DensityLayout { get; }

    /// <summary>
    /// Copies the latest density map for <paramref name="measure"/> (one value per cell, <see cref="DensityLayout"/>
    /// order; live is people/m²) into <paramref name="buffer"/> when it differs from <paramref name="version"/>, and
    /// returns true when it did. Callers switching measure reset their version to −1.
    /// </summary>
    public bool CopyDensity(ref float[] buffer, ref long version, DensityMeasure measure = DensityMeasure.Live)
    {
        int cells = DensityLayout.CellCount;
        lock (_gate)
        {
            if (_frontDensityVersion == version)
            {
                return false;
            }

            if (buffer.Length != cells)
            {
                buffer = new float[cells];
            }

            Array.Copy(_frontDensity, (int)measure * cells, buffer, 0, cells);
            version = _frontDensityVersion;
            return true;
        }
    }

    /// <summary>Seconds gathered into the cumulative maps at the last publish (0 during the warm-up, D-218).</summary>
    public double DensityAccumulatedS
    {
        get
        {
            lock (_gate)
            {
                return _frontAccumulatedS;
            }
        }
    }

    /// <summary>
    /// The latest results copy (device stats after the warm-up and the time series, round 011c). Safe from any
    /// thread; at most about half a second behind while playing, current once paused or finished.
    /// </summary>
    public RunResults LatestResults
    {
        get
        {
            lock (_gate)
            {
                return _frontResults;
            }
        }
    }

    /// <summary>True while playing.</summary>
    public bool IsPlaying => _playing;

    /// <summary>Counters from the latest snapshot.</summary>
    public SimStats LatestStats
    {
        get
        {
            lock (_gate)
            {
                return _frontStats;
            }
        }
    }

    /// <summary>True once a fixed-duration run has reached its end.</summary>
    public bool IsFinished => _engine.Model.DurationS is { } d && LatestStats.TimeS >= d - 1e-9;

    /// <summary>Current speed.</summary>
    public RunSpeed Speed
    {
        get
        {
            lock (_gate)
            {
                return _speed;
            }
        }

        set
        {
            lock (_gate)
            {
                _speed = value.IsMax ? value : value with { Multiplier = Math.Clamp(value.Multiplier, 0.1, 64) };
            }

            _wake.Set();
        }
    }

    /// <summary>Starts or resumes (no effect once a fixed-duration run has finished).</summary>
    public void Play()
    {
        _playing = true;
        _wake.Set();
    }

    /// <summary>Pauses after the current batch.</summary>
    public void Pause() => _playing = false;

    /// <summary>Advances one step while paused.</summary>
    public void Step()
    {
        Interlocked.Increment(ref _stepRequests);
        _wake.Set();
    }

    /// <summary>Back to time zero with no agents (paused).</summary>
    public void Reset()
    {
        _playing = false;
        _resetRequested = true;
        _wake.Set();
    }

    /// <summary>
    /// Copies the latest snapshot into <paramref name="buffer"/> (grown when needed). Returns the agent count, and
    /// the counters and snapshot version through the out parameters.
    /// </summary>
    public int CopySnapshot(ref float[] buffer, out SimStats stats, out long version)
    {
        lock (_gate)
        {
            int floats = _frontCount * SimEngine.FloatsPerAgent;
            if (buffer.Length < floats)
            {
                buffer = new float[Math.Max(floats * 2, 64 * SimEngine.FloatsPerAgent)];
            }

            Array.Copy(_front, buffer, floats);
            stats = _frontStats;
            version = _version;
            return _frontCount;
        }
    }

    /// <summary>
    /// The latest crowd wait in seconds for a connector or an exit zone (D-183), by its study id. False for anything
    /// else (devices, zones that aren't exits).
    /// </summary>
    public bool TryGetCrowdWait(Guid id, out double seconds)
    {
        seconds = 0;
        if (!_waitIndex.TryGetValue(id, out int index))
        {
            return false;
        }

        lock (_gate)
        {
            seconds = index < _frontWaits.Length ? _frontWaits[index] : 0;
            return true;
        }
    }

    /// <summary>Stops the thread.</summary>
    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(2000);
        }

        // Only dispose the wake event once the loop can no longer wait on it.
        if (!_thread.IsAlive)
        {
            _wake.Dispose();
        }
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        double owedSimSeconds = 0;
        double lastWall = 0;
        while (!_stop)
        {
            try
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    _engine.Reset();
                    owedSimSeconds = 0;
                    Publish();
                }

                bool stepped = false;
                while (Interlocked.CompareExchange(ref _stepRequests, 0, 0) > 0 && !_playing)
                {
                    Interlocked.Decrement(ref _stepRequests);
                    if (!_engine.IsFinished)
                    {
                        _engine.Step();
                        stepped = true;
                    }
                }

                if (stepped)
                {
                    Publish();
                }

                if (!_playing)
                {
                    PublishResults(force: true);
                    Interlocked.Exchange(ref _stepRequests, 0);
                    lastWall = clock.Elapsed.TotalSeconds;
                    owedSimSeconds = 0;
                    _wake.WaitOne(100);
                    continue;
                }

                RunSpeed speed = Speed;
                double now = clock.Elapsed.TotalSeconds;
                double wall = Math.Min(0.25, now - lastWall);
                lastWall = now;
                double batchStart = clock.Elapsed.TotalMilliseconds;
                double dt = _engine.Model.TimeStepS;

                if (speed.IsMax)
                {
                    while (clock.Elapsed.TotalMilliseconds - batchStart < MaxBatchMs && !_engine.IsFinished && _playing && !_stop)
                    {
                        _engine.Step();
                    }
                }
                else
                {
                    owedSimSeconds += wall * speed.Multiplier;
                    while (owedSimSeconds >= dt && !_engine.IsFinished && _playing && !_stop)
                    {
                        _engine.Step();
                        owedSimSeconds -= dt;
                        if (clock.Elapsed.TotalMilliseconds - batchStart > MaxBatchMs)
                        {
                            // Falling behind (a big crowd at a high speed): drop the debt rather than spiral.
                            owedSimSeconds = Math.Min(owedSimSeconds, dt);
                            break;
                        }
                    }
                }

                Publish();
                if (_engine.IsFinished)
                {
                    _playing = false;
                    Finished?.Invoke();
                    continue;
                }

                if (!speed.IsMax)
                {
                    _wake.WaitOne(8);
                }
            }
            catch (Exception ex)
            {
                // Boundary for the simulation thread: never let it take the app down.
                _playing = false;
                Faulted?.Invoke(ex);
                _wake.WaitOne(200);
            }
        }
    }

    private void BuildWaitIndex()
    {
        Nav.NavWorld world = _engine.Model.World;
        int connectors = world.Connectors.Count;
        for (int k = 0; k < connectors; k++)
        {
            _waitIndex.TryAdd(world.Connectors[k].ConnectorId, k);
        }

        for (int s = 0; s < world.Sinks.Count; s++)
        {
            if (world.Sinks[s].Role == Nav.NavTargetRole.Exit)
            {
                _waitIndex.TryAdd(world.Sinks[s].RegionId, connectors + s);
            }
        }

        _frontWaits = new double[connectors + world.Sinks.Count];
        _backWaits = new double[_frontWaits.Length];
        _frontTrains = new byte[_engine.Model.Trains.Count];
        _backTrains = new byte[_frontTrains.Length];
        _frontCars = new double[_engine.LiftCarCount * CarStride];
        _backCars = new double[_frontCars.Length];
    }

    /// <summary>
    /// The latest state of every lift car (round 013, plan symbols): its bank, its place in the bank, height, state,
    /// riders and direction.
    /// </summary>
    public IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> CopyLiftCarStates()
    {
        IReadOnlyList<SimLift> lifts = _engine.Model.Lifts;
        var result = new List<(SimLift, int, double, LiftCarStatus, int, int)>();
        lock (_gate)
        {
            foreach (SimLift lift in lifts)
            {
                for (int c = 0; c < lift.Cars; c++)
                {
                    int o = (lift.FirstCar + c) * CarStride;
                    if (o + CarStride - 1 < _frontCars.Length)
                    {
                        result.Add((lift, c, _frontCars[o], (LiftCarStatus)(int)_frontCars[o + 1], (int)_frontCars[o + 2], (int)_frontCars[o + 3]));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// How far open each car's doors are, 0 (shut) to 1 (open), indexed like the engine's cars (round 014, D-258: the
    /// sliding leaves in 3D).
    /// </summary>
    public double[] CopyLiftDoorShares()
    {
        lock (_gate)
        {
            var shares = new double[_frontCars.Length / CarStride];
            for (int c = 0; c < shares.Length; c++)
            {
                shares[c] = _frontCars[(c * CarStride) + 4];
            }

            return shares;
        }
    }

    /// <summary>
    /// The latest state of every train service (D-190): whether a train is in and whether it's boarding, with the
    /// compiled service (its box and doors).
    /// </summary>
    public IReadOnlyList<(SimTrain Train, bool In, bool Boarding)> CopyTrainStates()
    {
        IReadOnlyList<SimTrain> trains = _engine.Model.Trains;
        var result = new List<(SimTrain, bool, bool)>(trains.Count);
        lock (_gate)
        {
            for (int v = 0; v < trains.Count && v < _frontTrains.Length; v++)
            {
                result.Add((trains[v], _frontTrains[v] > 0, _frontTrains[v] > 1));
            }
        }

        return result;
    }

    private void Publish()
    {
        int count = _engine.WriteSnapshot(ref _back);
        SimStats stats = _engine.Stats;
        int connectors = _engine.Model.World.Connectors.Count;
        for (int w = 0; w < _backWaits.Length; w++)
        {
            _backWaits[w] = w < connectors ? _engine.ConnectorWaitS(w) : _engine.ExitCrowdS(w - connectors);
        }

        for (int v = 0; v < _backTrains.Length; v++)
        {
            (bool isIn, bool boarding) = _engine.TrainState(v);
            _backTrains[v] = (byte)(!isIn ? 0 : boarding ? 2 : 1);
        }

        for (int c = 0; c * CarStride < _backCars.Length; c++)
        {
            (int _, double z, LiftCarStatus state, int riders, int direction) = _engine.LiftCarState(c);
            int o = c * CarStride;
            _backCars[o] = z;
            _backCars[o + 1] = (int)state;
            _backCars[o + 2] = riders;
            _backCars[o + 3] = direction;
            _backCars[o + 4] = _engine.LiftDoorShare(c);
        }

        // The back buffer holds the map from two publishes ago; refresh it only when the engine has sampled since.
        DensityGrid density = _engine.Density;
        bool densityChanged = density.Version != _frontDensityVersion;
        if (densityChanged && density.Version != _backDensityVersion)
        {
            int cells = density.CellCount;
            foreach (DensityMeasure measure in Measures)
            {
                density.Values(measure).CopyTo(_backDensity.AsSpan((int)measure * cells, cells));
            }

            _backDensityVersion = density.Version;
            _backAccumulatedS = density.AccumulatedS;
        }

        lock (_gate)
        {
            (_front, _back) = (_back, _front);
            (_frontWaits, _backWaits) = (_backWaits, _frontWaits);
            (_frontTrains, _backTrains) = (_backTrains, _frontTrains);
            (_frontCars, _backCars) = (_backCars, _frontCars);
            if (densityChanged)
            {
                (_frontDensity, _backDensity) = (_backDensity, _frontDensity);
                (_frontDensityVersion, _backDensityVersion) = (_backDensityVersion, _frontDensityVersion);
                (_frontAccumulatedS, _backAccumulatedS) = (_backAccumulatedS, _frontAccumulatedS);
            }
            _frontCount = count;
            _frontStats = stats;
            _version++;
        }

        PublishResults(force: !_playing || _engine.IsFinished);
    }

    /// <summary>
    /// Copies the engine's results when they've changed, throttled to <see cref="ResultsIntervalMs"/> unless
    /// <paramref name="force"/> (paused, finished, reset). Runs on the simulation thread.
    /// </summary>
    private void PublishResults(bool force)
    {
        long version = _engine.ResultsVersion;
        if (version == _publishedResultsVersion || (!force && _resultsClock.ElapsedMilliseconds < ResultsIntervalMs))
        {
            return;
        }

        RunResults results = _engine.CopyResults();
        _resultsClock.Restart();
        _publishedResultsVersion = version;
        lock (_gate)
        {
            _frontResults = results;
        }
    }
}
