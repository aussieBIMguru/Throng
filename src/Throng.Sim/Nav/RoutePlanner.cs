using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Sim.Nav;

/// <summary>
/// Route choice (D-100, D-111): from a point on a floor, either walk straight to the sink (when it's on this floor)
/// or take a connector, whichever has the lowest expected time. Each option's time is scaled by per-agent noise
/// from the profile's decisiveness, stairs are corrected for the profile's own stair factors, belts for whether
/// this agent stands or walks (D-113), and each connector adds its current expected wait (D-111, D-113). The
/// profile's mechanised preference then scales a belt's ride and wait (D-138). A connector's wait only counts when its
/// entry is within the person's sight (D-179); out of sight it's judged on its uncrowded time.
/// </summary>
public static class RoutePlanner
{
    /// <summary>Spread of the noise at decisiveness 0 (the factor is 1 + N(0, 1) × spread × (1 − decisiveness)).</summary>
    public const double NoiseSpread = 0.25;

    /// <summary>How far a full mechanised preference (±1) scales a belt's ride and wait (D-138): 1 ∓ this.</summary>
    public const double MechanisedWeight = 0.4;

    /// <summary>
    /// How long standing on an escalator or travelator feels per second of ride (D-175): it's rest, so 0.6. Walkers
    /// on the belt feel their ride at full time.
    /// </summary>
    public const double StandingRideWeight = 0.6;

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
    /// <param name="stairEffort">Weigh stair time by the profile's stair effort (D-174); false = time only.</param>
    /// <param name="standingRest">Standing rides feel <see cref="StandingRideWeight"/> × their time (D-175).</param>
    /// <param name="sightRadius">
    /// Straight-line distance within which a connector entry's live wait counts (D-179); +∞ = every wait counts.
    /// </param>
    /// <param name="priorityPenaltyS">
    /// Extra seconds per connector for people without boarding priority (D-248: a lift with someone who needs
    /// accessible devices waiting at it), counted only when its entry is in sight; ignored for profiles that have
    /// priority themselves (<see cref="HasLiftPriority"/>). Null for none.
    /// </param>
    /// <param name="barredConnector">A connector this agent won't use (a lift it gave up on, D-253), or −1.</param>
    public static double Choose(
        NavRoutes routes, int floor, double x, double y, int sink, MobilityClass mobility, AgentProfileTemplate? profile,
        DeterministicRandom? random, out bool direct, out RouteOption choice, double[]? waitSeconds = null,
        double beltWalkSpeed = -1, int stayConnector = -1, double staySeconds = 0, bool stairEffort = true, bool standingRest = true,
        double sightRadius = double.PositiveInfinity, double[]? priorityPenaltyS = null, int barredConnector = -1)
    {
        NavWorld world = routes.World;
        NavSurface surface = world.Surfaces[floor];
        direct = false;
        choice = default;
        double best = double.PositiveInfinity;
        double spread = profile is null ? 0 : NoiseSpread * (1 - Math.Clamp(profile.Decisiveness, 0, 1));
        double[]? penalty = HasLiftPriority(profile) ? null : priorityPenaltyS;

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
            if (option.Connector == barredConnector || !routes.Usable(option.Connector, mobility) || routes.FloorSide[option.Entry] is not { } toEntry)
            {
                continue;
            }

            NavConnector connector = world.Connectors[option.Connector];
            // The ride and its queue, felt through the profile's liking for mechanised transport (D-138).
            // Standing on a belt is rest, so its ride feels shorter (D-175); the queue doesn't.
            double rideTime = option.ViaSeconds + BeltCorrection(connector, option.ViaSeconds, beltWalkSpeed);
            if (standingRest && beltWalkSpeed == 0 && connector.Kind is ConnectorKind.Escalator or ConnectorKind.Travelator)
            {
                rideTime *= StandingRideWeight;
            }

            bool inSight = InSight(world.Portals[option.Entry], x, y, sightRadius);
            double wait = waitSeconds is not null && inSight ? waitSeconds[option.Connector] : 0;
            if (penalty is not null && inSight)
            {
                // Not part of the felt ride, so not scaled by the lift preference: it's other people's turn.
                wait += penalty[option.Connector] / MechanisedFactor(connector.Kind, profile);
            }

            double ride = (rideTime + wait) * MechanisedFactor(connector.Kind, profile);
            double c = toEntry.Sample(surface, x, y) + ride + routes.Remaining(mobility, sink, option.Exit)
                + StairCorrection(connector, option.DeltaZ, profile, stairEffort)
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
    /// True for people who board lifts first (D-247): profiles that need accessible devices or are lift/ramp only.
    /// </summary>
    public static bool HasLiftPriority(AgentProfileTemplate? profile) =>
        profile is not null && (profile.NeedsAccessibleDevices || profile.LiftOnly);

    /// <summary>True when the portal's edge is within <paramref name="sightRadius"/> of (x, y) (D-179).</summary>
    public static bool InSight(NavPortal portal, double x, double y, double sightRadius) =>
        double.IsPositiveInfinity(sightRadius) || portal.DistanceTo(new Point2(x, y)) <= sightRadius;

    /// <summary>
    /// How long a connector's ride and queue feel to this profile (D-138): 1 − <see cref="MechanisedWeight"/> ×
    /// preference on escalators and travelators (lifts too, from M4), 1 on stairs, ramps and with no profile.
    /// </summary>
    /// <remarks>Lifts (round 013) use the profile's lift preference the same way.</remarks>
    public static double MechanisedFactor(ConnectorKind kind, AgentProfileTemplate? profile)
    {
        if (profile is null)
        {
            return 1.0;
        }

        return kind switch
        {
            ConnectorKind.Escalator or ConnectorKind.Travelator => 1.0 - (MechanisedWeight * Math.Clamp(profile.MechanisedPreference, -1.0, 1.0)),
            ConnectorKind.Lift => 1.0 - (MechanisedWeight * Math.Clamp(profile.LiftPreference, -1.0, 1.0)),
            _ => 1.0,
        };
    }

    /// <summary>
    /// Extra seconds a stair feels like to a profile compared with the nominal cost already in the route: its own
    /// time on the flight (plan length |Δz| × going ÷ riser, at nominal speed × its stair factor) weighted by its
    /// stair effort (D-174), minus the nominal time. With <paramref name="effort"/> off the weight is 1 (time only).
    /// </summary>
    public static double StairCorrection(NavConnector connector, double deltaZ, AgentProfileTemplate? profile, bool effort = true)
    {
        if (profile is null || connector.Kind != ConnectorKind.Stair || Math.Abs(deltaZ) < 1e-6 || connector.RiserM <= 1e-6)
        {
            return 0;
        }

        double length = Math.Abs(deltaZ) * connector.GoingM / connector.RiserM;
        bool up = deltaZ > 0;
        double factor = Math.Max(0.1, up ? profile.StairUpFactor : profile.StairDownFactor);
        double nominal = up ? NavCosts.StairUp : NavCosts.StairDown;
        double weight = effort ? Math.Clamp(up ? profile.StairUpEffort : profile.StairDownEffort, 1.0, 10.0) : 1.0;
        if (!double.IsFinite(weight))
        {
            weight = 1.0;
        }

        return length * ((weight / factor) - (1 / nominal)) / NavWorld.NominalSpeed;
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
