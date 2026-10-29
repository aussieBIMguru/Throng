using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS device table (round 011c, D-219): a device's stats after the warm-up, updated in place as
/// the run goes on, and whether its queue is on the graph (D-226).
/// </summary>
public sealed class DeviceResultRow : ObservableObject
{
    private readonly Action _graphedChanged;
    private long _served;
    private double _meanWaitS;
    private double _p95WaitS;
    private double _maxWaitS;
    private int _maxQueue;
    private double _utilisationPct;
    private long _balked;
    private long _gaveUp;
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
    public long Served { get => _served; private set => SetProperty(ref _served, value); }

    /// <summary>Mean wait, seconds.</summary>
    public double MeanWaitS { get => _meanWaitS; private set => SetProperty(ref _meanWaitS, value); }

    /// <summary>95th percentile wait, seconds.</summary>
    public double P95WaitS { get => _p95WaitS; private set => SetProperty(ref _p95WaitS, value); }

    /// <summary>Longest wait, seconds.</summary>
    public double MaxWaitS { get => _maxWaitS; private set => SetProperty(ref _maxWaitS, value); }

    /// <summary>Most people in line at once.</summary>
    public int MaxQueue { get => _maxQueue; private set => SetProperty(ref _maxQueue, value); }

    /// <summary>Share of lane time serving, percent.</summary>
    public double UtilisationPct { get => _utilisationPct; private set => SetProperty(ref _utilisationPct, value); }

    /// <summary>People who left its line for another device.</summary>
    public long Balked { get => _balked; private set => SetProperty(ref _balked, value); }

    /// <summary>People whose patience ran out in its line.</summary>
    public long GaveUp { get => _gaveUp; private set => SetProperty(ref _gaveUp, value); }

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
        Served = result.Served;
        MeanWaitS = Math.Round(result.MeanWaitS, 1);
        P95WaitS = Math.Round(result.P95WaitS, 1);
        MaxWaitS = Math.Round(result.MaxWaitS, 1);
        MaxQueue = result.MaxQueue;
        UtilisationPct = Math.Round(result.Utilisation * 100, 1);
        Balked = result.Balked;
        GaveUp = result.GaveUp;
    }
}
