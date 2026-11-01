namespace Throng.App.Services;

/// <summary>What the BATCH dialog asked for (round 021, D-354, D-361).</summary>
/// <param name="Runs">Number of runs (2–20).</param>
/// <param name="DurationMinutes">Minutes per run: the study's duration, or the one typed for "until stopped".</param>
public sealed record BatchRequest(int Runs, double DurationMinutes);
