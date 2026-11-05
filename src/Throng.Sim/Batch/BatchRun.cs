using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>One finished run of a batch (round 021): its place, its seed and its results copy.</summary>
/// <param name="Index">0-based position in the batch.</param>
/// <param name="Seed">The seed it ran on.</param>
/// <param name="Results">Its results, exactly as PLAY on that seed would give them.</param>
public sealed record BatchRun(int Index, int Seed, RunResults Results);
