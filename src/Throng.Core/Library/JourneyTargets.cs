using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// The devices a device node can use (D-318), shared by validation, the simulation set-up and the editor: the picked
/// devices that still exist when the node has a pick list, else every placed device of its type tag. A pick list that
/// matches nothing in the study (a journey from your library, or every pick deleted) falls back to the type tag
/// (round 020, D-349).
/// </summary>
public static class JourneyTargets
{
    /// <summary>The node's candidate devices (empty for other kinds of node).</summary>
    public static IReadOnlyList<Device> Devices(Study study, JourneyNode node)
    {
        if (node.Kind != JourneyNodeKind.Device)
        {
            return [];
        }

        if (node.HasPicks)
        {
            List<Device> picked = PickedDevices(study, node);
            if (picked.Count > 0)
            {
                return picked;
            }
        }

        string tag = node.TargetTag?.Trim() ?? string.Empty;
        return tag.Length == 0 ? [] : [.. study.Devices.Where(d => string.Equals(d.TypeTag?.Trim(), tag, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// True when the node's pick list decides its candidates: it has picks and at least one still names a device (device
    /// nodes) or zone (zone nodes and sinks) in the study. False for nodes chosen by type / tag, including pick lists
    /// that match nothing here (D-349).
    /// </summary>
    public static bool UsesPicks(Study study, JourneyNode node) =>
        node.HasPicks && node.HasCandidates
        && (node.Kind == JourneyNodeKind.Device ? PickedDevices(study, node).Count > 0 : JourneyZones.PickedZones(study, node).Count > 0);

    /// <summary>
    /// True when the node has picks but none of them is in the study, so it uses its type / tag instead (D-349; CHECK
    /// notes it).
    /// </summary>
    public static bool PicksFallBack(Study study, JourneyNode node) => node.HasPicks && node.HasCandidates && !UsesPicks(study, node);

    /// <summary>
    /// A candidate's weight (D-321, D-326): its pick's weight (zero or more), or 1 for nodes chosen by type / tag and
    /// for ids the pick list doesn't hold.
    /// </summary>
    public static double WeightOf(JourneyNode node, Guid id)
    {
        if (node.HasPicks && node.Picks!.FirstOrDefault(p => p is not null && p.Id == id) is { } pick)
        {
            return double.IsFinite(pick.Weight) ? Math.Max(0, pick.Weight) : 0;
        }

        return 1;
    }

    /// <summary>Ids in the pick list that no longer name a device (device nodes) or zone (zone nodes and sinks).</summary>
    public static IReadOnlyList<Guid> MissingPicks(Study study, JourneyNode node)
    {
        if (!node.HasPicks)
        {
            return [];
        }

        var present = node.Kind == JourneyNodeKind.Device
            ? PickedDevices(study, node).Select(d => d.Id).ToHashSet()
            : JourneyZones.PickedZones(study, node).Select(z => z.Id).ToHashSet();
        return [.. node.Picks!.Where(p => p is not null && !present.Contains(p.Id)).Select(p => p.Id)];
    }

    /// <summary>The picked devices that still exist, in pick order.</summary>
    private static List<Device> PickedDevices(Study study, JourneyNode node)
    {
        var picked = new List<Device>();
        foreach (JourneyPick pick in node.Picks ?? [])
        {
            if (pick is not null && study.FindDevice(pick.Id) is { } device && !picked.Contains(device))
            {
                picked.Add(device);
            }
        }

        return picked;
    }
}
