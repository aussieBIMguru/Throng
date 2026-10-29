namespace Throng.Sim.Nav;

/// <summary>Lift travel times (round 013): a trapezoidal speed profile, or a triangular one on short trips.</summary>
public static class LiftTiming
{
    /// <summary>Seconds to travel <paramref name="distanceM"/> from rest to rest.</summary>
    /// <param name="distanceM">Vertical distance, metres.</param>
    /// <param name="speedMps">Rated speed, m/s.</param>
    /// <param name="accelMps2">Acceleration and braking, m/s².</param>
    public static double TravelS(double distanceM, double speedMps, double accelMps2)
    {
        double d = Math.Max(0, distanceM);
        double v = Math.Max(0.1, speedMps);
        double a = Math.Max(0.1, accelMps2);
        if (d < 1e-9)
        {
            return 0;
        }

        // Full speed is reached only when the trip is longer than the run-up and the braking together.
        return d >= v * v / a ? (d / v) + (v / a) : 2 * Math.Sqrt(d / a);
    }
}
