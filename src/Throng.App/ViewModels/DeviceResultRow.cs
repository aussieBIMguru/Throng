using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS device table (round 011c, D-219): a device's stats after the warm-up, updated in place as
/// the run goes on, and whether its queue is on the graph (D-226).
/// </summary>
public sealed class DeviceResultRow : ObservableObject
{
    private readonly Action _graphedChanged;
    private StatCell _served;
    private StatCell _meanWaitS;
    private StatCell _p95WaitS;
    private StatCell _maxWaitS;
    private StatCell _maxQueue;
    private StatCell _utilisationPct;
    private StatCell _balked;
    private StatCell _gaveUp;
    private bool _isGraphed;

    /// <summary>Creates a row for a device.</summary>
    /// <param name="index">Device index in the run.</param>
    /// <param name="deviceId">The study device's id (for matching the viewport selection).</param>
    /// <param name="name">Device name.</param>
    /// <param name="type">Device type tag.</param>
    /// <param name="graphedChanged">Called when the graph tick changes.</param>
    public DeviceResultRow(int index, Guid deviceId, string name, string type, Action graphedChanged)
    {
        Index = index;
        DeviceId = deviceId;
        Name = name;
        Type = type;
        _graphedChanged = graphedChanged;
    }

    /// <summary>Device index in the run.</summary>
    public int Index { get; }

    /// <summary>The study device's id.</summary>
    public Guid DeviceId { get; }

    /// <summary>Device name.</summary>
    public string Name { get; }

    /// <summary>Device type tag.</summary>
    public string Type { get; }

    /// <summary>People served after the warm-up.</summary>
    public StatCell Served { get => _served; private set => SetProperty(ref _served, value); }

    /// <summary>Mean wait, seconds.</summary>
    public StatCell MeanWaitS { get => _meanWaitS; private set => SetProperty(ref _meanWaitS, value); }

    /// <summary>95th percentile wait, seconds.</summary>
    public StatCell P95WaitS { get => _p95WaitS; private set => SetProperty(ref _p95WaitS, value); }

    /// <summary>Longest wait, seconds.</summary>
    public StatCell MaxWaitS { get => _maxWaitS; private set => SetProperty(ref _maxWaitS, value); }

    /// <summary>Most people in line at once.</summary>
    public StatCell MaxQueue { get => _maxQueue; private set => SetProperty(ref _maxQueue, value); }

    /// <summary>Share of lane time serving, percent.</summary>
    public StatCell UtilisationPct { get => _utilisationPct; private set => SetProperty(ref _utilisationPct, value); }

    /// <summary>People who left its line for another device.</summary>
    public StatCell Balked { get => _balked; private set => SetProperty(ref _balked, value); }

    /// <summary>People whose patience ran out in its line.</summary>
    public StatCell GaveUp { get => _gaveUp; private set => SetProperty(ref _gaveUp, value); }

    /// <summary>Ticked: its queue is on the graph (D-226).</summary>
    public bool IsGraphed
    {
        get => _isGraphed;
        set
        {
            if (SetProperty(ref _isGraphed, value))
            {
                _graphedChanged();
            }
        }
    }

    /// <summary>Copies the latest stats in.</summary>
    public void Update(DeviceResult result)
    {
        Served = StatCell.Single(result.Served);
        MeanWaitS = StatCell.Single(Math.Round(result.MeanWaitS, 1));
        P95WaitS = StatCell.Single(Math.Round(result.P95WaitS, 1));
        MaxWaitS = StatCell.Single(Math.Round(result.MaxWaitS, 1));
        MaxQueue = StatCell.Single(result.MaxQueue);
        UtilisationPct = StatCell.Single(Math.Round(result.Utilisation * 100, 1));
        Balked = StatCell.Single(result.Balked);
        GaveUp = StatCell.Single(result.GaveUp);
    }

    /// <summary>Copies a batch's stats in: means with min–max (round 021, D-358).</summary>
    public void Update(DeviceBatchResult result)
    {
        Served = StatCell.Batch(result.Served, 1);
        MeanWaitS = StatCell.Batch(result.MeanWaitS, 1);
        P95WaitS = StatCell.Batch(result.P95WaitS, 1);
        MaxWaitS = StatCell.Batch(result.MaxWaitS, 1);
        MaxQueue = StatCell.Batch(result.MaxQueue, 1);
        UtilisationPct = StatCell.Batch(result.Utilisation, 1, 100);
        Balked = StatCell.Batch(result.Balked, 1);
        GaveUp = StatCell.Batch(result.GaveUp, 1);
    }
}
