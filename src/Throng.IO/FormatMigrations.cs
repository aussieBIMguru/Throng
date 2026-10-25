using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Upgrades studies loaded from older file format versions so old files always open (§14.2).
/// </summary>
/// <remarks>
/// When <see cref="ThrongFile.CurrentFormatVersion"/> is bumped, add a step here that upgrades from the previous
/// version. Steps run in order, from the file's version up to the current one.
/// <list type="bullet">
/// <item>1 → 2 (round 004, D-064): adds connectors and shafts. v1 files have neither, so the lists start empty.</item>
/// <item>2 → 3 (round 005, D-086): adds region spans and height modes, exclusions, per-level stairs, devices and
/// underlays. Missing values take the old behaviour: this level only, fixed heights, single-run switchbacks.</item>
/// <item>3 → 4 (round 006, D-099, D-105, D-110): adds emission rules on regions, the keep side and staff-only
/// connectors. Older files have no rules (no one is emitted), keep left and public connectors.</item>
/// <item>4 → 5 (round 007, D-129): adds rate curves on emission rules. Older rules have none (a flat rate).</item>
/// </list>
/// </remarks>
internal static class FormatMigrations
{
    /// <summary>One upgrade step: from <c>FromVersion</c> to <c>FromVersion + 1</c>.</summary>
    private static readonly IReadOnlyList<(int FromVersion, Action<Study> Upgrade)> Steps =
    [
        (1, UpgradeV1ToV2),
        (2, UpgradeV2ToV3),
        (3, UpgradeV3ToV4),
        (4, UpgradeV4ToV5),
    ];

    /// <summary>Applies every step needed to bring a study from <paramref name="fileVersion"/> to current.</summary>
    public static void Apply(Study study, int fileVersion)
    {
        foreach ((int fromVersion, Action<Study> upgrade) in Steps.OrderBy(s => s.FromVersion))
        {
            if (fromVersion >= fileVersion)
            {
                upgrade(study);
            }
        }
    }

    /// <summary>
    /// v1 → v2: v1 has no connectors or shafts. The serializer may leave the lists null when the keys are
    /// missing from hand-edited files, so make sure both exist.
    /// </summary>
    private static void UpgradeV1ToV2(Study study)
    {
        study.Connectors ??= [];
        study.Shafts ??= [];
    }

    /// <summary>
    /// v2 → v3: the new lists may be null in hand-edited files; everything else defaults to the v2 behaviour
    /// (regions on one level with fixed heights, multi-storey switchbacks as one long run).
    /// </summary>
    private static void UpgradeV2ToV3(Study study)
    {
        study.Devices ??= [];
        study.Underlays ??= [];
        foreach (Region region in study.Regions)
        {
            region.ExcludedLevelIds ??= [];
        }

        foreach (Connector connector in study.Connectors)
        {
            connector.ExcludedLevelIds ??= [];
        }

        foreach (Shaft shaft in study.Shafts)
        {
            shaft.NoStopLevelIds ??= [];
        }
    }

    /// <summary>
    /// v3 → v4: regions gain emission rules (null in older or hand-edited files). Keep side and staff-only take
    /// their property defaults (keep left, public).
    /// </summary>
    private static void UpgradeV3ToV4(Study study)
    {
        foreach (Region region in study.Regions)
        {
            region.EmissionRules ??= [];
            region.EmissionRules.RemoveAll(r => r is null);
            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                // Rules are immutable records, so a missing destination list is fixed by replacing the rule.
                if (region.EmissionRules[i].Destinations is null)
                {
                    region.EmissionRules[i] = region.EmissionRules[i] with { Destinations = [] };
                }
            }
        }
    }

    /// <summary>
    /// v4 → v5: emission rules gain rate curves (null in older or hand-edited files, which means flat). Null points
    /// are dropped; points with bad values are kept and ignored by the simulation (the validator warns).
    /// </summary>
    private static void UpgradeV4ToV5(Study study)
    {
        foreach (Region region in study.Regions)
        {
            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                EmissionRule rule = region.EmissionRules[i];
                if (rule.RateCurve is null || rule.RateCurve.Any(p => p is null))
                {
                    region.EmissionRules[i] = rule with { RateCurve = rule.RateCurve?.Where(p => p is not null).ToList() ?? [] };
                }
            }
        }
    }
}
