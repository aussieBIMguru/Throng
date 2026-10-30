namespace Throng.Sim.Engine;

/// <summary>
/// Lift bank results (round 015, D-260): boarded, landing waits, rides and give-ups per bank, gathered after the warm-up
/// like the device stats (D-218). Written only on the simulation thread.
/// </summary>
public sealed partial class SimEngine
{
    private long[] _resLiftBoarded = [];
    private double[] _resLiftWaitSum = [];
    private double[] _resLiftWaitMax = [];
    private List<float>[] _resLiftWaits = [];
    private long[] _resLiftRides = [];
    private double[] _resLiftRideSum = [];
    private long[] _resLiftGaveUp = [];

    /// <summary>Back to empty lift results (from <see cref="ResetResults"/>); sizes the arrays on first use.</summary>
    private void ResetLiftResults()
    {
        int n = _model.Lifts.Count;
        if (_resLiftBoarded.Length != n)
        {
            _resLiftBoarded = new long[n];
            _resLiftWaitSum = new double[n];
            _resLiftWaitMax = new double[n];
            _resLiftWaits = new List<float>[n];
            _resLiftRides = new long[n];
            _resLiftRideSum = new double[n];
            _resLiftGaveUp = new long[n];
            for (int l = 0; l < n; l++)
            {
                _resLiftWaits[l] = [];
            }
        }

        Array.Clear(_resLiftBoarded);
        Array.Clear(_resLiftWaitSum);
        Array.Clear(_resLiftWaitMax);
        Array.Clear(_resLiftRides);
        Array.Clear(_resLiftRideSum);
        Array.Clear(_resLiftGaveUp);
        foreach (List<float> waits in _resLiftWaits)
        {
            waits.Clear();
        }
    }

    /// <summary>Someone boarded a car of <paramref name="lift"/> after <paramref name="waitS"/> on the landing.</summary>
    private void RecordLiftBoard(int lift, double waitS)
    {
        if (!Measuring || lift < 0 || lift >= _resLiftBoarded.Length)
        {
            return;
        }

        waitS = Math.Max(0, waitS);
        _resLiftBoarded[lift]++;
        _resLiftWaitSum[lift] += waitS;
        _resLiftWaitMax[lift] = Math.Max(_resLiftWaitMax[lift], waitS);
        _resLiftWaits[lift].Add((float)waitS);
        _resultsVersion++;
    }

    /// <summary>Someone got off a car of <paramref name="lift"/> after riding <paramref name="rideS"/>.</summary>
    private void RecordLiftRide(int lift, double rideS)
    {
        if (!Measuring || lift < 0 || lift >= _resLiftRides.Length)
        {
            return;
        }

        _resLiftRides[lift]++;
        _resLiftRideSum[lift] += Math.Max(0, rideS);
        _resultsVersion++;
    }

    /// <summary>Someone's patience ran out on a landing of <paramref name="lift"/> and they went another way (D-253).</summary>
    private void RecordLiftGaveUp(int lift)
    {
        if (Measuring && lift >= 0 && lift < _resLiftGaveUp.Length)
        {
            _resLiftGaveUp[lift]++;
            _resultsVersion++;
        }
    }

    /// <summary>One row per bank, in <see cref="SimModel.Lifts"/> order.</summary>
    private LiftResult[] CopyLiftResults()
    {
        IReadOnlyList<SimLift> lifts = _model.Lifts;
        var rows = new LiftResult[Math.Min(lifts.Count, _resLiftBoarded.Length)];
        for (int l = 0; l < rows.Length; l++)
        {
            long boarded = _resLiftBoarded[l];
            long rides = _resLiftRides[l];
            rows[l] = new LiftResult(
                l,
                lifts[l].Name,
                lifts[l].Cars,
                boarded,
                boarded > 0 ? _resLiftWaitSum[l] / boarded : 0,
                Percentile(_resLiftWaits[l], 0.95),
                _resLiftWaitMax[l],
                rides,
                rides > 0 ? _resLiftRideSum[l] / rides : 0,
                _resLiftGaveUp[l]);
        }

        return rows;
    }
}
