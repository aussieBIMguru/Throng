using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS ▸ LIFTS table (round 015, D-260): a lift bank's boardings, landing waits, rides and give-ups
/// after the warm-up, updated in place while running.
/// </summary>
public sealed class LiftResultRow : ObservableObject
{
    private StatCell _boarded;
    private StatCell _meanWaitS;
    private StatCell _p95WaitS;
    private StatCell _maxWaitS;
    private StatCell _rides;
    private StatCell _meanRideS;
    private StatCell _gaveUp;

    /// <summary>Creates a row for a bank.</summary>
    /// <param name="index">Bank index in the run.</param>
    /// <param name="name">Bank name.</param>
    /// <param name="cars">Cars in the bank.</param>
    public LiftResultRow(int index, string name, int cars)
    {
        Index = index;
        Name = name;
        Cars = cars;
    }

    /// <summary>Bank index in the run.</summary>
    public int Index { get; }

    /// <summary>Bank name.</summary>
    public string Name { get; }

    /// <summary>Cars in the bank.</summary>
    public int Cars { get; }

    /// <summary>People who boarded.</summary>
    public StatCell Boarded { get => _boarded; private set => SetProperty(ref _boarded, value); }

    /// <summary>Mean landing wait, seconds.</summary>
    public StatCell MeanWaitS { get => _meanWaitS; private set => SetProperty(ref _meanWaitS, value); }

    /// <summary>95th percentile landing wait, seconds.</summary>
    public StatCell P95WaitS { get => _p95WaitS; private set => SetProperty(ref _p95WaitS, value); }

    /// <summary>Longest landing wait, seconds.</summary>
    public StatCell MaxWaitS { get => _maxWaitS; private set => SetProperty(ref _maxWaitS, value); }

    /// <summary>People who got off.</summary>
    public StatCell Rides { get => _rides; private set => SetProperty(ref _rides, value); }

    /// <summary>Mean ride, seconds.</summary>
    public StatCell MeanRideS { get => _meanRideS; private set => SetProperty(ref _meanRideS, value); }

    /// <summary>People who gave up on its landings and went another way (D-253).</summary>
    public StatCell GaveUp { get => _gaveUp; private set => SetProperty(ref _gaveUp, value); }

    /// <summary>Copies the latest stats in.</summary>
    public void Update(LiftResult result)
    {
        Boarded = StatCell.Single(result.Boarded);
        MeanWaitS = StatCell.Single(Math.Round(result.MeanWaitS, 1));
        P95WaitS = StatCell.Single(Math.Round(result.P95WaitS, 1));
        MaxWaitS = StatCell.Single(Math.Round(result.MaxWaitS, 1));
        Rides = StatCell.Single(result.Rides);
        MeanRideS = StatCell.Single(Math.Round(result.MeanRideS, 1));
        GaveUp = StatCell.Single(result.GaveUp);
    }

    /// <summary>Copies a batch's stats in: means with min–max (round 021, D-358).</summary>
    public void Update(LiftBatchResult result)
    {
        Boarded = StatCell.Batch(result.Boarded, 1);
        MeanWaitS = StatCell.Batch(result.MeanWaitS, 1);
        P95WaitS = StatCell.Batch(result.P95WaitS, 1);
        MaxWaitS = StatCell.Batch(result.MaxWaitS, 1);
        Rides = StatCell.Batch(result.Rides, 1);
        MeanRideS = StatCell.Batch(result.MeanRideS, 1);
        GaveUp = StatCell.Batch(result.GaveUp, 1);
    }
}
