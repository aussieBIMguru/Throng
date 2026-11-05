using System.Diagnostics;
using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>
/// Runs a batch headless and in parallel (round 021, D-354–D-357): run k is a fresh <see cref="SimEngine"/> on seed
/// base + k over the one shared, read-only <see cref="SimModel"/>, stepped to the batch's end exactly as PLAY would.
/// Each worker owns its engine (agents, random streams, density); nothing mutable is shared. Finished runs keep only
/// their results and a copy of their cumulative maps, folded in seed order, and the engine is dropped.
/// </summary>
/// <remarks>
/// <see cref="Run"/> blocks, so call it from a worker thread. <see cref="Progress"/> may be read from any thread while
/// it runs. Cancelling throws <see cref="OperationCanceledException"/>; an engine fault surfaces as the exception the
/// run threw (the caller's boundary logs it).
/// </remarks>
public sealed class BatchRunner
{
    /// <summary>Steps between cancellation checks and progress updates.</summary>
    private const int CheckEverySteps = 64;

    private readonly SimModel _model;
    private readonly double[] _fractions;
    private readonly object _gate = new();
    private int _done;
    private int _running;

    /// <summary>Creates a runner for <paramref name="model"/> (compiled once for the whole batch).</summary>
    /// <param name="model">The compiled model, shared read-only by every run.</param>
    /// <param name="options">Runs, seeds and duration; a non-positive duration is treated as one second.</param>
    public BatchRunner(SimModel model, BatchOptions options)
    {
        _model = model;
        int runs = Math.Max(1, options.Runs);
        Options = options with { Runs = runs, DurationS = Math.Max(1, options.DurationS) };
        Parallelism = Options.EffectiveParallelism;
        _fractions = new double[runs];
    }

    /// <summary>How the batch runs (runs at least one, duration at least one second).</summary>
    public BatchOptions Options { get; }

    /// <summary>Runs at once (D-356).</summary>
    public int Parallelism { get; }

    /// <summary>How far the batch has got; safe from any thread.</summary>
    public BatchProgress Progress
    {
        get
        {
            lock (_gate)
            {
                double sum = _done;
                for (int k = 0; k < _fractions.Length; k++)
                {
                    double f = _fractions[k];
                    if (f is > 0 and < 1)
                    {
                        sum += f;
                    }
                }

                return new BatchProgress(_done, _running, _fractions.Length, Math.Clamp(sum / _fractions.Length, 0, 1));
            }
        }
    }

    /// <summary>Runs every run and combines them. Blocks until done or cancelled.</summary>
    /// <param name="cancel">Cancels the batch (throws <see cref="OperationCanceledException"/>).</param>
    public BatchResults Run(CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        int count = _fractions.Length;
        var runs = new BatchRun?[count];
        var waiting = new Dictionary<int, DensityFold.RunMaps>();
        var fold = new DensityFold(new DensityGrid(_model));
        int nextToFold = 0;

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancel };
        Parallel.For(0, count, parallel, k =>
        {
            lock (_gate)
            {
                _running++;
            }

            try
            {
                (BatchRun run, DensityFold.RunMaps maps) = RunOne(k, cancel);
                lock (_gate)
                {
                    runs[k] = run;
                    _fractions[k] = 1;
                    _done++;

                    // Fold maps in seed order so the averages never depend on which run finished first.
                    waiting[k] = maps;
                    while (waiting.Remove(nextToFold, out DensityFold.RunMaps? next))
                    {
                        fold.Add(next);
                        nextToFold++;
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _running--;
                }
            }
        });

        cancel.ThrowIfCancellationRequested();
        BatchRun[] finished = [.. runs.Select(r => r ?? throw new InvalidOperationException("A batch run didn't finish."))];
        return BatchAverager.Combine(finished, Options, fold.Build(), Parallelism, clock.Elapsed);
    }

    /// <summary>One run, start to end, on its own engine (the same steps PLAY takes).</summary>
    private (BatchRun Run, DensityFold.RunMaps Maps) RunOne(int k, CancellationToken cancel)
    {
        int seed = Options.SeedOf(k);
        var engine = new SimEngine(_model, seed, Options.DurationS);
        DensityGrid grid = engine.Density;
        double duration = Options.DurationS;
        int steps = 0;
        while (!engine.IsFinished)
        {
            engine.Step();
            if (++steps % CheckEverySteps == 0)
            {
                cancel.ThrowIfCancellationRequested();
                Volatile.Write(ref _fractions[k], Math.Min(0.999, engine.TimeS / duration));
            }
        }

        return (new BatchRun(k, seed, engine.CopyResults()), DensityFold.Capture(grid));
    }
}
