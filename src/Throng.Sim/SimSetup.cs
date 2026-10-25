using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim.Nav;

namespace Throng.Sim;

/// <summary>Outcome of <see cref="SimSetup.Build"/>: a model, or the findings that block the run (D-106).</summary>
/// <param name="Model">The compiled model; null when blocked.</param>
/// <param name="Findings">Every finding (validation plus reachability); errors block.</param>
public sealed record SimSetupResult(SimModel? Model, IReadOnlyList<ValidationMessage> Findings)
{
    /// <summary>True when the run can start.</summary>
    public bool IsSuccess => Model is not null;
}

/// <summary>
/// Compiles a study into a <see cref="SimModel"/> (run on a worker thread by the app): validation errors block;
/// then the navigation world, fields and portal graph; then each emission rule is checked for a walkable spawn and
/// a reachable sink (errors block, D-106).
/// </summary>
/// <remarks>Reads the study only; the app keeps it locked while a run is active (D-097).</remarks>
public static class SimSetup
{
    /// <summary>Builds the model. Never throws for bad studies; exceptions from bugs are caught by the caller.</summary>
    public static SimSetupResult Build(Study study, IPolygonOps? ops = null, CancellationToken cancel = default)
    {
        ops ??= ClipperPolygonOps.Instance;
        var findings = new List<ValidationMessage>(StudyValidator.Validate(study, ops));
        if (findings.Any(f => f.Severity == ValidationSeverity.Error))
        {
            return new SimSetupResult(null, findings);
        }

        Result<NavWorld> world = NavWorldBuilder.Build(study, ops, findings);
        if (!world.IsSuccess || world.Value is null)
        {
            findings.Add(new ValidationMessage(ValidationSeverity.Error, world.Error ?? "The navigation world couldn't be built."));
            return new SimSetupResult(null, findings);
        }

        NavWorld nav = world.Value;

        // Profiles and rules first, so the step-free table is only built when needed.
        var profiles = new List<SimProfile>();
        var profileIndex = new Dictionary<Guid, int>();
        var pending = new List<(Region Region, int RuleNumber, EmissionRule Rule, int Profile)>();
        foreach (Region region in study.Regions.Where(r => r.IsSpawn))
        {
            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                EmissionRule rule = region.EmissionRules[i];
                if (!(rule.RatePerMinute > 0))
                {
                    continue;
                }

                AgentProfileTemplate template = StudyTemplates.ProfileOrFallback(study, rule.ProfileId);
                if (!profileIndex.TryGetValue(template.Id, out int p))
                {
                    p = profiles.Count;
                    profileIndex[template.Id] = p;
                    (float r, float g, float b) = ParseColour(template.ColorHex);
                    profiles.Add(new SimProfile { Index = p, Template = template, Mobility = NavRoutes.ClassOf(template), R = r, G = g, B = b });
                }

                pending.Add((region, i + 1, rule, p));
            }
        }

        cancel.ThrowIfCancellationRequested();
        NavRoutes routes = NavRoutes.Build(nav, profiles.Any(p => p.Mobility == MobilityClass.StepFree), cancel);

        var sinkByRegion = new Dictionary<Guid, int>();
        foreach (NavSink sink in nav.Sinks)
        {
            sinkByRegion[sink.RegionId] = sink.Index;
        }

        var emitters = new List<SimEmitter>();
        foreach ((Region region, int number, EmissionRule rule, int p) in pending)
        {
            string label = string.Create(CultureInfo.InvariantCulture, $"'{region.Name}' rule {number}");
            if (!nav.FloorByLevel.TryGetValue(region.LevelId, out int floor))
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Error, $"{label}: the zone's level has no walkable floor.", region.Id));
                continue;
            }

            List<int> cells = NavWorldBuilder.CellsInside(nav.Surfaces[floor], [region.Outline], walkableOnly: true);
            if (cells.Count == 0)
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Error, $"{label}: the spawn zone has no walkable floor inside it.", region.Id));
                continue;
            }

            // Candidates: chosen sinks with a positive weight, or every other sink for nearest-sink rules.
            var candidates = new List<(int Sink, double Weight)>();
            if (rule.DestinationMode == DestinationMode.NearestSink)
            {
                candidates.AddRange(nav.Sinks.Where(s => s.RegionId != region.Id).Select(s => (s.Index, 1.0)));
            }
            else
            {
                foreach (SinkWeight destination in rule.Destinations)
                {
                    if (destination.Weight > 0 && sinkByRegion.TryGetValue(destination.SinkId, out int s))
                    {
                        candidates.Add((s, destination.Weight));
                    }
                }
            }

            // Reachability from the middle of the zone, plain nominal costs.
            Point2 from = nav.Surfaces[floor].CellCentre(cells[cells.Count / 2]);
            MobilityClass mobility = profiles[p].Mobility;
            var reachable = new List<(int Sink, double Weight)>();
            var unreachable = new List<string>();
            foreach ((int s, double w) in candidates)
            {
                double cost = RoutePlanner.Choose(routes, floor, from.X, from.Y, s, mobility, null, null, out _, out _);
                if (double.IsFinite(cost))
                {
                    reachable.Add((s, w));
                }
                else
                {
                    unreachable.Add(nav.Sinks[s].Name);
                }
            }

            if (reachable.Count == 0)
            {
                string who = mobility == MobilityClass.StepFree ? " for step-free profiles" : string.Empty;
                findings.Add(new ValidationMessage(ValidationSeverity.Error, $"{label} can't reach any of its sinks{who}.", region.Id));
                continue;
            }

            if (unreachable.Count > 0 && rule.DestinationMode == DestinationMode.ChosenSinks)
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning,
                    $"{label} can't reach {string.Join(", ", unreachable.Select(n => $"'{n}'"))}; those sinks are skipped.", region.Id));
            }

            emitters.Add(new SimEmitter
            {
                Index = emitters.Count,
                RegionId = region.Id,
                Label = label,
                Rule = rule,
                Profile = p,
                Floor = floor,
                Cells = [.. cells],
                Nearest = rule.DestinationMode == DestinationMode.NearestSink,
                Destinations = [.. reachable],
            });
        }

        if (findings.Any(f => f.Severity == ValidationSeverity.Error))
        {
            return new SimSetupResult(null, findings);
        }

        if (emitters.Count == 0)
        {
            findings.Add(new ValidationMessage(ValidationSeverity.Info, "No spawn zone has an emission rule, so no one will appear. Add rules in SELECTION on a spawn zone."));
        }

        StudySettings settings = study.Settings;
        var model = new SimModel
        {
            Routes = routes,
            Profiles = profiles,
            Emitters = emitters,
            Seed = settings.Seed,
            StartTimeOfDay = settings.StartTimeOfDay,
            DurationS = settings.EndCondition == StudyEndCondition.FixedDuration ? Math.Max(1, settings.DurationS) : null,
            TimeStepS = Math.Clamp(settings.TimeStepS, 0.02, 0.2),
            KeepSide = settings.KeepSide,
            Findings = findings,
        };
        return new SimSetupResult(model, findings);
    }

    /// <summary>Parses #RRGGBB to 0..1 components (white when malformed).</summary>
    public static (float R, float G, float B) ParseColour(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
        }

        return (1f, 1f, 1f);
    }
}
