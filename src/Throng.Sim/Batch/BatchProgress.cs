namespace Throng.Sim.Batch;

/// <summary>How far a batch has got (round 021, D-357): for the status bar's progress line.</summary>
/// <param name="Done">Runs finished.</param>
/// <param name="Running">Runs under way.</param>
/// <param name="Total">Runs in the batch.</param>
/// <param name="Fraction">Overall progress, 0–1 (finished runs plus how far the running ones are).</param>
public readonly record struct BatchProgress(int Done, int Running, int Total, double Fraction);
