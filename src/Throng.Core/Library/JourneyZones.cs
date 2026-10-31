using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>Which zones a journey's zone node can use (D-149, D-303), shared by validation and the simulation set-up.</summary>
public static class JourneyZones
{
    /// <summary>
    /// The zones a PassZone or Dwell step may send people to: its pinned zone; otherwise every zone whose tag matches
    /// <see cref="JourneyStep.TargetTag"/> (case-insensitive); otherwise, with no tag, every dwell zone (Dwell) or
    /// every zone (PassZone). Spawn and sink zones are only used when pinned or tagged.
    /// </summary>
    public static IReadOnlyList<Region> Matching(Study study, JourneyStep step) => Matching(study, step.Kind, step.TargetTag, step.ZoneId);

    /// <summary>
    /// The zones a zone node may send people to (as for steps), or for a zone Sink (D-303) the sink zones it may
    /// leave at: the pinned one, else sink zones with the tag, else every sink zone. A pick list (D-318) overrides
    /// all of that: the picked zones that still exist (sinks only for a zone Sink); when none of them is in the study,
    /// the node falls back to its pin / tag (round 020, D-349). Other nodes match none.
    /// </summary>
    public static IReadOnlyList<Region> Matching(Study study, JourneyNode node) =>
        node.HasPicks && node.HasCandidates && PickedZones(study, node) is { Count: > 0 } picked
            ? picked
            : ByTag(study, node);

    /// <summary>The picked zones that still exist, in pick order (sink zones only for a zone Sink); empty without picks.</summary>
    public static List<Region> PickedZones(Study study, JourneyNode node) => node.HasPicks ? Picked(study, node) : [];

    private static IReadOnlyList<Region> ByTag(Study study, JourneyNode node) => node.Kind switch
    {
        JourneyNodeKind.PassZone => Matching(study, JourneyStepKind.PassZone, node.TargetTag, node.ZoneId),
        JourneyNodeKind.Dwell => Matching(study, JourneyStepKind.Dwell, node.TargetTag, node.ZoneId),
        JourneyNodeKind.Sink when node.SinkKind == JourneySinkKind.Zone => MatchingSinks(study, node.TargetTag, node.ZoneId),
        _ => [],
    };

    /// <summary>The picked zones that still exist, in pick order (sink zones only for a zone Sink).</summary>
    private static List<Region> Picked(Study study, JourneyNode node)
    {
        var zones = new List<Region>();
        foreach (JourneyPick pick in node.Picks!)
        {
            if (pick is not null && study.FindRegion(pick.Id) is { Kind: RegionKind.Zone } region && region.Outline.Count >= 3
                && (node.Kind != JourneyNodeKind.Sink || region.IsSink) && !zones.Contains(region))
            {
                zones.Add(region);
            }
        }

        return zones;
    }

    private static List<Region> Matching(Study study, JourneyStepKind kind, string? tag, Guid? zoneId)
    {
        var zones = new List<Region>();
        foreach (Region region in study.Regions)
        {
            if (region.Kind != RegionKind.Zone || region.Outline.Count < 3)
            {
                continue;
            }

            bool match = zoneId is { } id
                ? region.Id == id
                : !string.IsNullOrEmpty(tag)
                    ? string.Equals(region.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase)
                    : kind == JourneyStepKind.Dwell ? region.ZoneKind == ZoneKind.Dwell : !region.IsSpawn && !region.IsSink;
            if (match)
            {
                zones.Add(region);
            }
        }

        return zones;
    }

    private static List<Region> MatchingSinks(Study study, string? tag, Guid? zoneId)
    {
        var zones = new List<Region>();
        foreach (Region region in study.Regions)
        {
            if (region.Kind != RegionKind.Zone || region.Outline.Count < 3 || !region.IsSink)
            {
                continue;
            }

            bool match = zoneId is { } id
                ? region.Id == id
                : string.IsNullOrWhiteSpace(tag) || string.Equals(region.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase);
            if (match)
            {
                zones.Add(region);
            }
        }

        return zones;
    }
}
