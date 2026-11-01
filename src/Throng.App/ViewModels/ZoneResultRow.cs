using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the RESULTS ▸ ZONES table (round 012, D-228, D-232): a measurement zone's occupancy and entries after
/// the warm-up, updated in place, and whether its occupancy is on the graph (ZONES, like D-226).
/// </summary>
public sealed class ZoneResultRow : ObservableObject
{
    private readonly Action _graphedChanged;
    private StatCell _peakOccupancy;
    private StatCell _meanOccupancy;
    private StatCell _peakDensity;
    private StatCell _entries;
    private StatCell _entriesPerMin;
    private StatCell _entriesPerMinPerM;
    private bool _isGraphed;

    /// <summary>Creates a row for a zone.</summary>
    /// <param name="index">Zone index in the run.</param>
    /// <param name="regionId">The study region's id (for matching the viewport selection).</param>
    /// <param name="name">Zone name.</param>
    /// <param name="widthM">Width people cross, metres.</param>
    /// <param name="graphedChanged">Called when the graph tick changes.</param>
    public ZoneResultRow(int index, Guid regionId, string name, double widthM, Action graphedChanged)
    {
        Index = index;
        RegionId = regionId;
        Name = name;
        WidthM = Math.Round(widthM, 2);
        _graphedChanged = graphedChanged;
    }

    /// <summary>Zone index in the run.</summary>
    public int Index { get; }

    /// <summary>The study region's id.</summary>
    public Guid RegionId { get; }

    /// <summary>Zone name.</summary>
    public string Name { get; }

    /// <summary>Width people cross (shorter side of the rotated bounding rectangle), metres.</summary>
    public double WidthM { get; }

    /// <summary>Most people in it at once.</summary>
    public StatCell PeakOccupancy { get => _peakOccupancy; private set => SetProperty(ref _peakOccupancy, value); }

    /// <summary>Average number of people in it.</summary>
    public StatCell MeanOccupancy { get => _meanOccupancy; private set => SetProperty(ref _meanOccupancy, value); }

    /// <summary>Peak occupancy over the zone area, people per m².</summary>
    public StatCell PeakDensity { get => _peakDensity; private set => SetProperty(ref _peakDensity, value); }

    /// <summary>People who walked in.</summary>
    public StatCell Entries { get => _entries; private set => SetProperty(ref _entries, value); }

    /// <summary>Entries per minute.</summary>
    public StatCell EntriesPerMin { get => _entriesPerMin; private set => SetProperty(ref _entriesPerMin, value); }

    /// <summary>Entries per minute per metre of width.</summary>
    public StatCell EntriesPerMinPerM { get => _entriesPerMinPerM; private set => SetProperty(ref _entriesPerMinPerM, value); }

    /// <summary>Ticked: its occupancy is on the graph (ZONES).</summary>
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
    public void Update(ZoneResult result)
    {
        PeakOccupancy = StatCell.Single(result.PeakOccupancy);
        MeanOccupancy = StatCell.Single(Math.Round(result.MeanOccupancy, 1));
        PeakDensity = StatCell.Single(Math.Round(result.PeakDensity, 2));
        Entries = StatCell.Single(result.Entries);
        EntriesPerMin = StatCell.Single(Math.Round(result.EntriesPerMin, 1));
        EntriesPerMinPerM = StatCell.Single(Math.Round(result.EntriesPerMinPerM, 1));
    }

    /// <summary>Copies a batch's stats in: means with min–max (round 021, D-358).</summary>
    public void Update(ZoneBatchResult result)
    {
        PeakOccupancy = StatCell.Batch(result.PeakOccupancy, 1);
        MeanOccupancy = StatCell.Batch(result.MeanOccupancy, 1);
        PeakDensity = StatCell.Batch(result.PeakDensity, 2);
        Entries = StatCell.Batch(result.Entries, 1);
        EntriesPerMin = StatCell.Batch(result.EntriesPerMin, 1);
        EntriesPerMinPerM = StatCell.Batch(result.EntriesPerMinPerM, 1);
    }
}
