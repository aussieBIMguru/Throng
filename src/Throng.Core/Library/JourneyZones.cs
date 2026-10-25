using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>Which zones a journey's zone step can use (D-149), shared by validation and the simulation set-up.</summary>
public static class JourneyZones
{
    /// <summary>
    /// The zones a PassZone or Dwell step may send people to: its pinned zone; otherwise every zone whose tag matches
    /// <see cref="JourneyStep.TargetTag"/> (case-insensitive); otherwise, with no tag, every dwell zone (Dwell) or
    /// every zone (PassZone). Spawn and sink zones are only used when pinned or tagged.
    /// </summary>
    public static IReadOnlyList<Region> Matching(Study study, JourneyStep step)
    {
        var zones = new List<Region>();
        foreach (Region region in study.Regions)
        {
            if (region.Kind != RegionKind.Zone || region.Outline.Count < 3)
            {
                continue;
            }

            bool match = step.ZoneId is { } id
                ? region.Id == id
                : !string.IsNullOrEmpty(step.TargetTag)
                    ? string.Equals(region.Tag, step.TargetTag, StringComparison.OrdinalIgnoreCase)
                    : step.Kind == JourneyStepKind.Dwell ? region.ZoneKind == ZoneKind.Dwell : !region.IsSpawn && !region.IsSink;
            if (match)
            {
                zones.Add(region);
            }
        }

        return zones;
    }
}
