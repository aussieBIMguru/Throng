namespace Throng.Sim.Engine;

/// <summary>
/// Measurement zone results (round 012, D-228): occupancy each step from agent cells, entries on outside → inside
/// transitions, peak and mean after the warm-up, and occupancy in the time series from the start.
/// </summary>
public sealed partial class SimEngine
{
    private int[] _zoneNow = [];
    private int[] _zonePeak = [];
    private double[] _zoneOccupancyS = [];
    private long[] _zoneEntries = [];
    private readonly List<int> _seriesZones = [];

    /// <summary>Zone bits of the floor cell under a plan point (0 off the zones).</summary>
    private ulong ZoneBitsAt(int surface, double x, double y)
    {
        IReadOnlyList<ulong[]?> bits = _model.ZoneCellBits;
        if (surface < 0 || surface >= bits.Count || bits[surface] is not { } mask)
        {
            return 0;
        }

        int cell = _world.Surfaces[surface].CellAt(x, y);
        return cell >= 0 && cell < mask.Length ? mask[cell] : 0;
    }

    /// <summary>Back to empty zone results; sizes the arrays on first use.</summary>
    private void ResetZoneResults()
    {
        int n = _model.Zones.Count;
        if (_zoneNow.Length != n)
        {
            _zoneNow = new int[n];
            _zonePeak = new int[n];
            _zoneOccupancyS = new double[n];
            _zoneEntries = new long[n];
        }

        Array.Clear(_zoneNow);
        Array.Clear(_zonePeak);
        Array.Clear(_zoneOccupancyS);
        Array.Clear(_zoneEntries);
        _seriesZones.Clear();
    }

    /// <summary>
    /// Counts who is in each zone now and who just walked in (end of each step). Hot path: no allocation, no LINQ;
    /// skipped when the run has no zones.
    /// </summary>
    private void CountZones()
    {
        int zones = _zoneNow.Length;
        if (zones == 0)
        {
            return;
        }

        Array.Clear(_zoneNow);
        bool measuring = Measuring;
        AgentStore a = _agents;
        for (int i = 0; i < a.Count; i++)
        {
            ulong now = a.Phase[i] == AgentPhase.LiftRide ? 0 : ZoneBitsAt(a.Surface[i], a.X[i], a.Y[i]);
            ulong entered = now & ~a.ZoneBits[i];
            a.ZoneBits[i] = now;
            while (now != 0)
            {
                int z = System.Numerics.BitOperations.TrailingZeroCount(now);
                _zoneNow[z]++;
                now &= now - 1;
            }

            if (measuring)
            {
                while (entered != 0)
                {
                    int z = System.Numerics.BitOperations.TrailingZeroCount(entered);
                    _zoneEntries[z]++;
                    entered &= entered - 1;
                    _resultsVersion++;
                }
            }
        }

        if (measuring)
        {
            for (int z = 0; z < zones; z++)
            {
                _zoneOccupancyS[z] += _zoneNow[z] * _dt;
                if (_zoneNow[z] > _zonePeak[z])
                {
                    _zonePeak[z] = _zoneNow[z];
                    _resultsVersion++;
                }
            }
        }
    }

    /// <summary>Adds each zone's occupancy to the time series (called with the other series samples).</summary>
    private void SampleZones()
    {
        for (int z = 0; z < _zoneNow.Length; z++)
        {
            _seriesZones.Add(_zoneNow[z]);
        }
    }

    /// <summary>Zone rows for a results copy.</summary>
    private ZoneResult[] CopyZoneResults()
    {
        IReadOnlyList<SimZone> zones = _model.Zones;
        var rows = new ZoneResult[zones.Count];
        double minutes = _resMeasuredS / 60.0;
        for (int z = 0; z < zones.Count; z++)
        {
            SimZone zone = zones[z];
            double perMin = minutes > 0 ? _zoneEntries[z] / minutes : 0;
            rows[z] = new ZoneResult(
                z,
                zone.Name,
                zone.AreaM2,
                zone.WidthM,
                _zonePeak[z],
                _resMeasuredS > 0 ? _zoneOccupancyS[z] / _resMeasuredS : 0,
                _zoneEntries[z],
                perMin,
                zone.WidthM > 0 ? perMin / zone.WidthM : 0);
        }

        return rows;
    }
}
