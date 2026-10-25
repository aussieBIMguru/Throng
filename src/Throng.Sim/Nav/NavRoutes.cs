using Throng.Core.Library;

namespace Throng.Sim.Nav;

/// <summary>Who may use which connectors when routing.</summary>
public enum MobilityClass
{
    /// <summary>Everything public: stairs, escalators, ramps, travelators.</summary>
    Normal = 0,

    /// <summary>Lift-only profiles: ramps and travelators (lifts arrive in M4).</summary>
    StepFree = 1,
}

/// <summary>
/// A way to change level (or ride a travelator) from a floor: step on at <see cref="Entry"/>, off at <see cref="Exit"/>.
/// </summary>
/// <param name="Entry">Portal stepped onto (on this floor).</param>
/// <param name="Exit">Portal stepped off at.</param>
/// <param name="Connector">The connector.</param>
/// <param name="ViaSeconds">Nominal seconds along the connector from the entry to the exit.</param>
/// <param name="DeltaZ">Exit height minus entry height (for the per-profile stair correction).</param>
public readonly record struct RouteOption(int Entry, int Exit, int Connector, float ViaSeconds, double DeltaZ);

/// <summary>
/// Flow fields and the portal graph (BRIEF §3.0, D-100): per served floor end a field on its floor leading to it
/// and a field over its connector leading off at it; per sink a field on its floor; and per mobility class the
/// nominal seconds from stepping off at each portal to each sink (Bellman–Ford over the portals).
/// </summary>
public sealed class NavRoutes
{
    private readonly float[]?[] _remaining;
    private readonly int _portalCount;

    private NavRoutes(NavWorld world, FlowField?[] floorSide, FlowField?[] connSide, FlowField[] sinkFields, List<RouteOption>[] options, float[]?[] remaining)
    {
        World = world;
        FloorSide = floorSide;
        ConnSide = connSide;
        SinkFields = sinkFields;
        OptionsByFloor = options;
        _remaining = remaining;
        _portalCount = world.Portals.Count;
    }

    /// <summary>The navigation world.</summary>
    public NavWorld World { get; }

    /// <summary>Per portal: field on its floor leading onto the connector there (entry portals only).</summary>
    public IReadOnlyList<FlowField?> FloorSide { get; }

    /// <summary>Per portal: field over its connector leading off onto the floor there (exit portals only).</summary>
    public IReadOnlyList<FlowField?> ConnSide { get; }

    /// <summary>Per sink: field on its floor leading into it.</summary>
    public IReadOnlyList<FlowField> SinkFields { get; }

    /// <summary>Per surface index: the level-change options from that floor (empty for pieces).</summary>
    public IReadOnlyList<List<RouteOption>> OptionsByFloor { get; }

    /// <summary>Builds every field and the remaining-cost tables.</summary>
    /// <param name="needStepFree">Build the step-free table too (only when a lift-only profile is emitted).</param>
    public static NavRoutes Build(NavWorld world, bool needStepFree, CancellationToken cancel = default)
    {
        var builder = new FieldBuilder(world);
        int portalCount = world.Portals.Count;
        var floorSide = new FlowField?[portalCount];
        var connSide = new FlowField?[portalCount];
        foreach (NavPortal portal in world.Portals)
        {
            cancel.ThrowIfCancellationRequested();
            if (portal.AllowsEntry)
            {
                floorSide[portal.Index] = builder.Build([portal.Floor], portal.Floor, portal.FloorCells, portal.Outward * -1, (portal.A, portal.B));
            }

            if (portal.AllowsExit)
            {
                NavConnector connector = world.Connectors[portal.Connector];
                var scope = new List<int>(connector.Pieces);
                scope.AddRange(connector.PassOverFloors);
                connSide[portal.Index] = builder.Build(scope, portal.Piece, portal.PieceCells, portal.Outward, (portal.A, portal.B));
            }
        }

        var sinkFields = new FlowField[world.Sinks.Count];
        foreach (NavSink sink in world.Sinks)
        {
            cancel.ThrowIfCancellationRequested();
            sinkFields[sink.Index] = builder.Build([sink.Floor], sink.Floor, sink.Cells, default);
        }

        // Options: every allowed entry on a floor paired with every other allowed exit of the same connector.
        var options = new List<RouteOption>[world.Surfaces.Count];
        for (int s = 0; s < options.Length; s++)
        {
            options[s] = [];
        }

        foreach (NavPortal entry in world.Portals)
        {
            if (!entry.AllowsEntry)
            {
                continue;
            }

            foreach (int exitIndex in world.Connectors[entry.Connector].Portals)
            {
                NavPortal exit = world.Portals[exitIndex];
                if (exitIndex == entry.Index || !exit.AllowsExit || connSide[exitIndex] is not { } field)
                {
                    continue;
                }

                float via = field.At(entry.Piece, entry.PieceCell);
                if (float.IsFinite(via))
                {
                    options[entry.Floor].Add(new RouteOption(entry.Index, exitIndex, entry.Connector, via, exit.ElevationM - entry.ElevationM));
                }
            }
        }

        var routes = new NavRoutes(world, floorSide, connSide, sinkFields, options, new float[]?[2]);
        routes._remaining[(int)MobilityClass.Normal] = routes.BuildRemaining(MobilityClass.Normal);
        if (needStepFree)
        {
            routes._remaining[(int)MobilityClass.StepFree] = routes.BuildRemaining(MobilityClass.StepFree);
        }

        return routes;
    }

    /// <summary>True when people of a class may use a connector.</summary>
    public bool Usable(int connector, MobilityClass mobility) =>
        mobility == MobilityClass.Normal || World.Connectors[connector].StepFreeOk;

    /// <summary>Nominal seconds from stepping off at a portal to reaching a sink (+∞ when unreachable).</summary>
    public float Remaining(MobilityClass mobility, int sink, int portal) =>
        _remaining[(int)mobility] is { } table ? table[(sink * _portalCount) + portal] : float.PositiveInfinity;

    /// <summary>
    /// Bellman–Ford: R[d][p] = min(sink field at p's floor cell, over options from p's floor: floor field to the
    /// entry + via + R[d][exit]).
    /// </summary>
    private float[] BuildRemaining(MobilityClass mobility)
    {
        int p = _portalCount;
        var table = new float[World.Sinks.Count * p];
        Array.Fill(table, float.PositiveInfinity);
        foreach (NavSink sink in World.Sinks)
        {
            foreach (NavPortal portal in World.Portals)
            {
                if (portal.Floor == sink.Floor)
                {
                    table[(sink.Index * p) + portal.Index] = SinkFields[sink.Index].At(portal.Floor, portal.FloorCell);
                }
            }
        }

        for (int iteration = 0; iteration <= p; iteration++)
        {
            bool changed = false;
            foreach (NavSink sink in World.Sinks)
            {
                int row = sink.Index * p;
                foreach (NavPortal portal in World.Portals)
                {
                    float best = table[row + portal.Index];
                    foreach (RouteOption option in OptionsByFloor[portal.Floor])
                    {
                        if (!Usable(option.Connector, mobility) || FloorSide[option.Entry] is not { } toEntry)
                        {
                            continue;
                        }

                        float c = toEntry.At(portal.Floor, portal.FloorCell) + option.ViaSeconds + table[row + option.Exit];
                        if (c < best)
                        {
                            best = c;
                        }
                    }

                    if (best < table[row + portal.Index])
                    {
                        table[row + portal.Index] = best;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return table;
    }

    /// <summary>The mobility class of a profile.</summary>
    public static MobilityClass ClassOf(AgentProfileTemplate profile) => profile.LiftOnly ? MobilityClass.StepFree : MobilityClass.Normal;
}
