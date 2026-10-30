using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS ▸ LIFTS table (round 015, D-260): a lift bank's boardings, landing waits, rides and give-ups
/// after the warm-up, updated in place while running.
/// </summary>
public sealed class LiftResultRow : ObservableObject
{
    private long _boarded;
    private double _meanWaitS;
    private double _p95WaitS;
    private double _maxWaitS;
    private long _rides;
    private double _meanRideS;
    private long _gaveUp;

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
    public long Boarded { get => _boarded; private set => SetProperty(ref _boarded, value); }

    /// <summary>Mean landing wait, seconds.</summary>
    public double MeanWaitS { get => _meanWaitS; private set => SetProperty(ref _meanWaitS, value); }

    /// <summary>95th percentile landing wait, seconds.</summary>
    public double P95WaitS { get => _p95WaitS; private set => SetProperty(ref _p95WaitS, value); }

    /// <summary>Longest landing wait, seconds.</summary>
    public double MaxWaitS { get => _maxWaitS; private set => SetProperty(ref _maxWaitS, value); }

    /// <summary>People who got off.</summary>
    public long Rides { get => _rides; private set => SetProperty(ref _rides, value); }

    /// <summary>Mean ride, seconds.</summary>
    public double MeanRideS { get => _meanRideS; private set => SetProperty(ref _meanRideS, value); }

    /// <summary>People who gave up on its landings and went another way (D-253).</summary>
    public long GaveUp { get => _gaveUp; private set => SetProperty(ref _gaveUp, value); }

    /// <summary>Takes the latest numbers.</summary>
    public void Update(LiftResult result)
    {
        Boarded = result.Boarded;
        MeanWaitS = Math.Round(result.MeanWaitS, 1);
        P95WaitS = Math.Round(result.P95WaitS, 1);
        MaxWaitS = Math.Round(result.MaxWaitS, 1);
        Rides = result.Rides;
        MeanRideS = Math.Round(result.MeanRideS, 1);
        GaveUp = result.GaveUp;
    }
}
