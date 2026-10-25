using Throng.Core.Library;

namespace Throng.Sim.Nav;

/// <summary>
/// Route choice (D-100, D-111): from a point on a floor, either walk straight to the sink (when it's on this floor)
/// or take a connector, whichever has the lowest expected time. Each option's time is scaled by per-agent noise
/// from the profile's decisiveness, stairs are corrected for the profile's own stair factors, belts for whether
/// this agent stands or walks (D-113), and each connector adds its current expected wait (D-111, D-113).
/// </summary>
public static class RoutePlanner
{
    /// <summary>Spread of the noise at decisiveness 0 (the factor is 1 + N(0, 1) × spread × (1 − decisiveness)).</summary>
    public const double NoiseSpread = 0.25;

    /// <summary>
    /// Picks the best way from (x, y) on <paramref name="floor"/> to <paramref name="sink"/>. Returns its cost in
    /// nominal seconds (noise included), or +∞ when the sink can't be reached.
    /// </summary>
    /// <param name="random">Noise source; null for the plain nominal choice (checks and tests).</param>
    /// <param name="direct">True when the choice is to walk straight to the sink.</param>
    /// <param name="choice">The connector option when <paramref name="direct"/> is false.</param>
    /// <param name="waitSeconds">Expected wait per connector (D-111), or null to ignore crowding.</param>
    /// <param name="beltWalkSpeed">
    /// This agent's walking speed on belts relative to the belt (0 for a stander), or negative to use the nominal
    /// belt cost (D-113).
    /// </param>
    /// <param name="stayConnector">Connector the agent is already heading for (re-choice, D-113), or −1.</param>
    /// <param name="staySeconds">Seconds taken off <paramref name="stayConnector"/>'s options so people only switch
    /// for a clear gain.</param>
    public static double Choose(
        NavRoutes routes, int floor, double x, double y, int sink, MobilityClass mobility, AgentProfileTemplate? profile,
        DeterministicRandom? random, out bool direct, out RouteOption choice, double[]? waitSeconds = null,
        double beltWalkSpeed = -1, int stayConnector = -1, double staySeconds = 0)
    {
        NavWorld world = routes.World;
        NavSurface surface = world.Surfaces[floor];
        direct = false;
        choice = default;
        double best = double.PositiveInfinity;
        double spread = profile is null ? 0 : NoiseSpread * (1 - Math.Clamp(profile.Decisiveness, 0, 1));

        if (world.Sinks[sink].Floor == floor)
        {
            double c = routes.SinkFields[sink].Sample(surface, x, y);
            if (double.IsFinite(c))
            {
                best = c * Noise(random, spread);
                direct = true;
            }
        }

        foreach (RouteOption option in routes.OptionsByFloor[floor])
        {
            if (!routes.Usable(option.Connector, mobility) || routes.FloorSide[option.Entry] is not { } toEntry)
            {
                continue;
            }

            NavConnector connector = world.Connectors[option.Connector];
            double c = toEntry.Sample(surface, x, y) + option.ViaSeconds + routes.Remaining(mobility, sink, option.Exit)
                + StairCorrection(connector, option.DeltaZ, profile)
                + BeltCorrection(connector, option.ViaSeconds, beltWalkSpeed)
                + (waitSeconds is null ? 0 : waitSeconds[option.Connector])
                - (option.Connector == stayConnector ? staySeconds : 0);
            if (!double.IsFinite(c))
            {
                continue;
            }

            c *= Noise(random, spread);
            if (c < best)
            {
                best = c;
                direct = false;
                choice = option;
            }
        }

        return best;
    }

    /// <summary>
    /// Extra seconds a profile spends on a stair compared with the nominal factors: the flight's plan length
    /// (|Δz| × going ÷ riser) × (1 ÷ profile factor − 1 ÷ nominal factor) ÷ nominal speed.
    /// </summary>
    public static double StairCorrection(NavConnector connector, double deltaZ, AgentProfileTemplate? profile)
    {
        if (profile is null || connector.Kind != ConnectorKind.Stair || Math.Abs(deltaZ) < 1e-6 || connector.RiserM <= 1e-6)
        {
            return 0;
        }

        double length = Math.Abs(deltaZ) * connector.GoingM / connector.RiserM;
        bool up = deltaZ > 0;
        double factor = Math.Max(0.1, up ? profile.StairUpFactor : profile.StairDownFactor);
        double nominal = up ? NavCosts.StairUp : NavCosts.StairDown;
        return length * ((1 / factor) - (1 / nominal)) / NavWorld.NominalSpeed;
    }

    /// <summary>
    /// Extra seconds on a belt for this agent compared with the nominal cost (belt + <see cref="NavCosts.BeltWalk"/>):
    /// a stander rides at belt speed only, so the ride takes about twice the nominal time on a 0.5 m/s escalator.
    /// </summary>
    /// <param name="viaSeconds">Nominal seconds along the connector.</param>
    /// <param name="beltWalkSpeed">Walking speed relative to the belt (0 = stands); negative for no correction.</param>
    public static double BeltCorrection(NavConnector connector, double viaSeconds, double beltWalkSpeed)
    {
        double belt = connector.BeltSpeedMps;
        if (beltWalkSpeed < 0 || belt <= 1e-6 || connector.Kind is not (ConnectorKind.Escalator or ConnectorKind.Travelator))
        {
            return 0;
        }

        return viaSeconds * (((belt + NavCosts.BeltWalk) / (belt + beltWalkSpeed)) - 1);
    }

    private static double Noise(DeterministicRandom? random, double spread) =>
        random is null || spread <= 0 ? 1 : Math.Max(0.2, 1 + (random.NextGaussian() * spread));
}
