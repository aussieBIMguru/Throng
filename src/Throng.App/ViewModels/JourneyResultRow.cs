using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS ▸ JOURNEYS table (round 012, D-229, D-232): a profile on one journey (or ALL of its
/// journeys), people spawned after the warm-up, updated in place.
/// </summary>
public sealed class JourneyResultRow : ObservableObject
{
    private long _finished;
    private double _meanS;
    private double _p95S;
    private double _withinPct;
    private double _longWaitPct;
    private long _gaveUp;
    private long _stuck;
    private int _inProgress;

    /// <summary>Creates a row.</summary>
    /// <param name="profile">Profile name.</param>
    /// <param name="journey">Journey name, "(no journey)" or "ALL".</param>
    /// <param name="isAll">True for a profile's ALL row.</param>
    /// <param name="acceptableS">The profile's acceptable journey time, seconds.</param>
    /// <param name="sink">Per-sink rows (D-312): the sink; empty otherwise.</param>
    public JourneyResultRow(string profile, string journey, bool isAll, double acceptableS, string sink = "")
    {
        Profile = profile;
        Journey = journey;
        IsAll = isAll;
        Sink = sink ?? string.Empty;
        AcceptableS = Math.Round(acceptableS);
    }

    /// <summary>The sink of a per-sink row (D-312), empty otherwise.</summary>
    public string Sink { get; }

    /// <summary>True for a per-sink row (shown dim, under its journey).</summary>
    public bool IsSink => Sink.Length > 0;

    /// <summary>Profile name.</summary>
    public string Profile { get; }

    /// <summary>Journey name.</summary>
    public string Journey { get; }

    /// <summary>True for a profile's ALL row (shown bold).</summary>
    public bool IsAll { get; }

    /// <summary>The profile's acceptable journey time, seconds.</summary>
    public double AcceptableS { get; }

    /// <summary>People who finished.</summary>
    public long Finished { get => _finished; private set => SetProperty(ref _finished, value); }

    /// <summary>Mean journey time, seconds.</summary>
    public double MeanS { get => _meanS; private set => SetProperty(ref _meanS, value); }

    /// <summary>95th percentile journey time, seconds.</summary>
    public double P95S { get => _p95S; private set => SetProperty(ref _p95S, value); }

    /// <summary>Share finished within the acceptable time, percent.</summary>
    public double WithinPct { get => _withinPct; private set => SetProperty(ref _withinPct, value); }

    /// <summary>Share with a single wait over Long wait, percent.</summary>
    public double LongWaitPct { get => _longWaitPct; private set => SetProperty(ref _longWaitPct, value); }

    /// <summary>People who gave up in a line and left.</summary>
    public long GaveUp { get => _gaveUp; private set => SetProperty(ref _gaveUp, value); }

    /// <summary>People removed as stuck.</summary>
    public long Stuck { get => _stuck; private set => SetProperty(ref _stuck, value); }

    /// <summary>People still in the model.</summary>
    public int InProgress { get => _inProgress; private set => SetProperty(ref _inProgress, value); }

    /// <summary>True when this row is the same profile and journey as <paramref name="result"/>.</summary>
    public bool Matches(JourneyResult result) =>
        result.IsAll == IsAll && string.Equals(result.Profile, Profile, StringComparison.Ordinal) && string.Equals(result.Journey, Journey, StringComparison.Ordinal)
        && string.Equals(result.Sink, Sink, StringComparison.Ordinal);

    /// <summary>Copies the latest stats in.</summary>
    public void Update(JourneyResult result)
    {
        Finished = result.Finished;
        MeanS = Math.Round(result.MeanS, 1);
        P95S = Math.Round(result.P95S, 1);
        WithinPct = Math.Round(result.WithinAcceptablePct, 1);
        LongWaitPct = Math.Round(result.LongWaitPct, 1);
        GaveUp = result.GaveUp;
        Stuck = result.Stuck;
        InProgress = result.InProgress;
    }
}
