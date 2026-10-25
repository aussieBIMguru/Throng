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

    /// <summary>Creates a paused runner and starts its thread.</summary>
    public SimRunner(SimModel model)
    {
        _engine = new SimEngine(model);
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

    private void Publish()
    {
        int count = _engine.WriteSnapshot(ref _back);
        SimStats stats = _engine.Stats;
        lock (_gate)
        {
            (_front, _back) = (_back, _front);
            _frontCount = count;
            _frontStats = stats;
            _version++;
        }
    }
}
