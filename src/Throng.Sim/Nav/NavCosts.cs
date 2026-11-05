using Throng.Core.Geometry;

namespace Throng.Sim.Nav;

/// <summary>
/// Nominal step times for the flow fields (round 006 design): seconds to move a plan displacement on a surface at
/// <see cref="NavWorld.NominalSpeed"/>, with stair, ramp and belt factors. Per-profile differences are applied
/// at route choice, not here, so fields can be shared by every profile.
/// </summary>
public static class NavCosts
{
    /// <summary>Nominal stair speed factors (the defaults of the built-in profiles).</summary>
    public const double StairUp = 0.6, StairDown = 0.7, StairAcross = 0.65;

    /// <summary>Ramp uphill speed factor.</summary>
    public const double RampUp = 0.9;

    /// <summary>Nominal walking speed added on a belt, and the sideways speed on a belt, m/s.</summary>
    public const double BeltWalk = 0.5, BeltAcross = 0.6;

    /// <summary>
    /// Seconds to move <paramref name="delta"/> (plan) on <paramref name="surface"/>; +∞ when the move isn't allowed
    /// (against a belt).
    /// </summary>
    /// <param name="slowness">Average slowness of the two cells (floors; 1 elsewhere).</param>
    public static float StepSeconds(NavSurface surface, Point2 delta, float slowness)
    {
        double v0 = NavWorld.NominalSpeed;
        if (surface.Kind == NavSurfaceKind.Floor || surface.Motion == NavMotion.Walk)
        {
            return (float)(delta.Length * slowness / v0);
        }

        double du = Point2.Dot(delta, surface.AxisU);
        double dv = Point2.Dot(delta, surface.AxisV);
        double along, across;
        switch (surface.Motion)
        {
            case NavMotion.Stair:
                along = v0 * (du >= 0 ? StairUp : StairDown);
                across = v0 * StairAcross;
                break;

            case NavMotion.Ramp:
                along = v0 * (du >= 0 ? RampUp : 1.0);
                across = v0;
                break;

            case NavMotion.Belt:
                if (du * surface.BeltSpeed < -1e-6)
                {
                    return float.PositiveInfinity;
                }

                along = Math.Abs(surface.BeltSpeed) + BeltWalk;
                across = BeltAcross;
                break;

            default:
                along = across = v0;
                break;
        }

        double tu = du / along;
        double tv = dv / across;
        return (float)Math.Sqrt((tu * tu) + (tv * tv));
    }
}
