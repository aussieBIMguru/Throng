using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.Core.Validation;

/// <summary>Severity of a validation finding (§10).</summary>
public enum ValidationSeverity
{
    /// <summary>Useful to know; nothing wrong.</summary>
    Info,

    /// <summary>Probably unintended; the study can still run.</summary>
    Warning,

    /// <summary>The study can't run correctly until fixed.</summary>
    Error,
}

/// <summary>One validation finding, optionally pointing at the offending object.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="ObjectId">Id of the object to zoom to, if any.</param>
public sealed record ValidationMessage(ValidationSeverity Severity, string Message, Guid? ObjectId = null);

/// <summary>
/// Structural checks on a study. M0 covers model integrity; round 004 adds connector and shaft checks (D-066);
/// the full rule set (reachability, capacity checks, irrational setups) grows in M7.
/// </summary>
public static class StudyValidator
{
    private const double MinAreaM2 = 1e-4;

    /// <summary>How far beyond a connector's end edge its landing is probed for walkable floor, in metres.</summary>
    public const double LandingProbeM = 0.25;

    /// <summary>Overlap area above which two connectors clash, in square metres.</summary>
    private const double OverlapM2 = 0.01;

    // Comfortable ranges (NCC-style) for the too steep / too shallow warnings (D-066).
    private const double MaxRiserM = 0.19, MinRiserM = 0.115, MaxGoingM = 0.355, MinGoingM = 0.25;
    private const double SteepestRamp = (1.0 / 14.0) + 1e-6, ShallowestRamp = 1.0 / 20.0;
    private const double SteepestEscalatorDeg = 35.0 + 1e-6, ShallowestEscalatorDeg = 27.3;

    /// <summary>Runs all checks and returns the findings (empty when clean).</summary>
    /// <param name="study">Study to check.</param>
    /// <param name="ops">Polygon library for the walkable-area checks; Clipper2 when null.</param>
    public static IReadOnlyList<ValidationMessage> Validate(Study study, IPolygonOps? ops = null)
    {
        var messages = new List<ValidationMessage>();

        if (study.Levels.Count == 0)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, "The study has no levels."));
        }

        foreach (IGrouping<Guid, Level> duplicate in study.Levels.GroupBy(l => l.Id).Where(g => g.Count() > 1))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Level id {duplicate.Key} is used by more than one level.", duplicate.Key));
        }

        foreach (Level level in study.Levels)
        {
            if (level.HeightM <= 0)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Level '{level.Name}' has a height of zero or less.", level.Id));
            }

            if (!study.RegionsOn(level.Id).Any(r => r.Kind == RegionKind.Walkable))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Level '{level.Name}' has no walkable area.", level.Id));
            }
        }

        var levelIds = study.Levels.Select(l => l.Id).ToHashSet();
        foreach (Region region in study.Regions)
        {
            if (!levelIds.Contains(region.LevelId))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Region '{region.Name}' is on a level that doesn't exist.", region.Id));
                continue;
            }

            if (region.CanSpan && region.TopLevelId is { } topId)
            {
                Level? top = study.FindLevel(topId);
                Level? bottom = study.FindLevel(region.LevelId);
                if (top is null)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error, $"'{region.Name}' spans to a level that doesn't exist.", region.Id));
                }
                else if (bottom is not null && top.ElevationM < bottom.ElevationM - 1e-6)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error, $"'{region.Name}' spans to a level below its own.", region.Id));
                }
            }

            if (region.Kind == RegionKind.Wall)
            {
                if (region.Outline.Count < 2)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Wall '{region.Name}' needs at least two points.", region.Id));
                }

                continue;
            }

            if (region.Outline.Count < 3 || PolygonMath.Area(region.Outline) < MinAreaM2)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Region '{region.Name}' has no area.", region.Id));
            }

            if (region.Kind == RegionKind.Zone && region.ZoneKind == ZoneKind.None)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Info, $"Zone '{region.Name}' has no purpose set.", region.Id));
            }
        }

        IPolygonOps polygons = ops ?? ClipperPolygonOps.Instance;
        ValidateVertical(study, polygons, levelIds, messages);
        ValidateDevicesAndUnderlays(study, polygons, levelIds, messages);
        ValidateDemand(study, messages);
        return messages;
    }

    /// <summary>True when the findings contain no errors.</summary>
    public static bool IsRunnable(IEnumerable<ValidationMessage> messages) =>
        messages.All(m => m.Severity != ValidationSeverity.Error);

    /// <summary>Connector and shaft checks (D-066).</summary>
    private static void ValidateVertical(Study study, IPolygonOps ops, HashSet<Guid> levelIds, List<ValidationMessage> messages)
    {
        var walkableCache = new Dictionary<Guid, IReadOnlyList<IReadOnlyList<Point2>>?>();
        IReadOnlyList<IReadOnlyList<Point2>>? NetWalkable(Guid levelId)
        {
            if (!walkableCache.TryGetValue(levelId, out IReadOnlyList<IReadOnlyList<Point2>>? net))
            {
                Result<IReadOnlyList<IReadOnlyList<Point2>>> result = WalkableArea.Compute(study, levelId, ops);
                net = result.IsSuccess ? result.Value : null;
                walkableCache[levelId] = net;
            }

            return net;
        }

        var placed = new List<(Connector Connector, ConnectorLayout Layout, double Bottom, double Top)>();
        foreach (Connector connector in study.Connectors)
        {
            Level? bottom = study.FindLevel(connector.BottomLevelId);
            Level? top = study.FindLevel(connector.TopLevelId);
            if (bottom is null || top is null)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Connector '{connector.Name}' links a level that doesn't exist.", connector.Id));
                continue;
            }

            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, study);
            if (!layout.IsSuccess || layout.Value is not { } l)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Connector '{connector.Name}': {layout.Error}", connector.Id));
                continue;
            }

            // A straight run through a level it doesn't serve, usually after a storey was split (D-143).
            if (!connector.IsTravelator && !connector.UsesCircuits)
            {
                foreach (Level passed in study.Levels)
                {
                    if (passed.ElevationM > bottom.ElevationM + 1e-6 && passed.ElevationM < top.ElevationM - 1e-6)
                    {
                        messages.Add(new ValidationMessage(ValidationSeverity.Warning,
                            $"Connector '{connector.Name}' passes level '{passed.Name}' without stopping; re-link it if it should serve that level.", connector.Id));
                    }
                }
            }

            // Travelators are flat: give them a token height range so the overlap test still sees them.
            placed.Add((connector, l, bottom.ElevationM, connector.IsTravelator ? bottom.ElevationM + 0.1 : top.ElevationM));
            if (l.Circuits.Count > 1)
            {
                // Per-level stairs (D-093): every circuit end on a served level needs floor; excluded levels have no exit.
                foreach (ConnectorCircuit circuit in l.Circuits)
                {
                    if (circuit.BottomLevelId is { } from && (circuit.Index == 0 || !connector.ExcludedLevelIds.Contains(from)))
                    {
                        CheckLanding(connector, NetWalkable(from), l.BottomLanding - (l.BottomAxis * LandingProbeM), "bottom", study.FindLevel(from)?.Name ?? "?", messages);
                    }

                    if (circuit.TopLevelId is { } to && (circuit.Index == l.Circuits.Count - 1 || !connector.ExcludedLevelIds.Contains(to)))
                    {
                        CheckLanding(connector, NetWalkable(to), circuit.TopLanding + (circuit.TopAxis * LandingProbeM), "top", study.FindLevel(to)?.Name ?? "?", messages);
                    }
                }
            }
            else
            {
                CheckLanding(connector, NetWalkable(bottom.Id), l.BottomLanding - (l.BottomAxis * LandingProbeM), connector.IsTravelator ? "start" : "bottom", bottom.Name, messages);
                CheckLanding(connector, NetWalkable(top.Id), l.TopLanding + (l.TopAxis * LandingProbeM), connector.IsTravelator ? "end" : "top", top.Name, messages);
            }

            CheckSlope(connector, l, messages);
        }

        // Overlaps: only connectors whose height ranges overlap can clash (stacked flights are fine).
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
            {
                var a = placed[i];
                var b = placed[j];
                if (a.Top <= b.Bottom + 1e-6 || b.Top <= a.Bottom + 1e-6)
                {
                    continue;
                }

                double overlap = WalkableArea.NetAreaM2(ops.Intersection([a.Layout.Footprint], [b.Layout.Footprint]));
                if (overlap > OverlapM2)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"Connectors '{a.Connector.Name}' and '{b.Connector.Name}' overlap by {overlap:0.00} m²."),
                        a.Connector.Id));
                }
            }
        }

        foreach (Shaft shaft in study.Shafts)
        {
            if (!levelIds.Contains(shaft.BaseLevelId) || !levelIds.Contains(shaft.TopLevelId))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Shaft '{shaft.Name}' spans a level that doesn't exist.", shaft.Id));
                continue;
            }

            if (study.FindLevel(shaft.TopLevelId)!.ElevationM < study.FindLevel(shaft.BaseLevelId)!.ElevationM - 1e-6)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Shaft '{shaft.Name}' has its top level below its base level.", shaft.Id));
            }

            if (shaft.Outline.Count < 3 || PolygonMath.Area(shaft.Outline) < MinAreaM2)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Shaft '{shaft.Name}' has no area.", shaft.Id));
            }
        }

        // A level people can stand on but can't leave vertically (only meaningful with more than one level).
        if (study.Levels.Count > 1)
        {
            foreach (Level level in study.Levels)
            {
                bool hasFloor = study.RegionsOn(level.Id).Any(r => r.Kind == RegionKind.Walkable);
                if (hasFloor && !study.Connectors.Any(c => study.ConnectorServes(c, level.Id)))
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Level '{level.Name}' has walkable area but no stair, escalator or ramp to another level.", level.Id));
                }
            }
        }
    }

    private static void CheckLanding(Connector connector, IReadOnlyList<IReadOnlyList<Point2>>? walkable, Point2 probe, string end, string levelName, List<ValidationMessage> messages)
    {
        if (walkable is null || !WalkableArea.Contains(walkable, probe))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Connector '{connector.Name}': the {end} landing isn't on walkable floor on '{levelName}'.", connector.Id));
        }
    }

    private static void CheckSlope(Connector connector, ConnectorLayout layout, List<ValidationMessage> messages)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string? problem = connector.Kind switch
        {
            ConnectorKind.Stair when layout.ActualRiserM > MaxRiserM + 1e-9 =>
                string.Create(c, $"risers of {layout.ActualRiserM * 1000:0} mm are too steep (over {MaxRiserM * 1000:0} mm)"),
            ConnectorKind.Stair when layout.MinRiserM < MinRiserM - 1e-9 =>
                string.Create(c, $"risers of {layout.MinRiserM * 1000:0} mm are very shallow (under {MinRiserM * 1000:0} mm)"),
            ConnectorKind.Stair when connector.GoingM > MaxGoingM + 1e-9 || connector.GoingM < MinGoingM - 1e-9 =>
                string.Create(c, $"a going of {connector.GoingM * 1000:0} mm is outside {MinGoingM * 1000:0}–{MaxGoingM * 1000:0} mm"),
            ConnectorKind.Ramp when layout.SteepestGradient > SteepestRamp =>
                string.Create(c, $"1:{1 / layout.SteepestGradient:0.#} is steeper than 1:14"),
            ConnectorKind.Ramp when layout.SteepestGradient < ShallowestRamp =>
                string.Create(c, $"1:{1 / layout.SteepestGradient:0.#} is shallower than 1:20 (could be a walkway)"),
            ConnectorKind.Escalator when layout.PitchDeg > SteepestEscalatorDeg =>
                string.Create(c, $"{layout.PitchDeg:0.#}° is steeper than 35°"),
            ConnectorKind.Escalator when layout.PitchDeg < ShallowestEscalatorDeg =>
                string.Create(c, $"{layout.PitchDeg:0.#}° is shallower than a standard escalator"),
            _ => null,
        };

        if (problem is not null)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Connector '{connector.Name}': {problem}.", connector.Id));
        }
    }

    /// <summary>Devices (D-089) and underlays (D-083).</summary>
    private static void ValidateDevicesAndUnderlays(Study study, IPolygonOps ops, HashSet<Guid> levelIds, List<ValidationMessage> messages)
    {
        var walkableCache = new Dictionary<Guid, IReadOnlyList<IReadOnlyList<Point2>>?>();
        foreach (Device device in study.Devices)
        {
            if (!levelIds.Contains(device.LevelId))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Device '{device.Name}' is on a level that doesn't exist.", device.Id));
                continue;
            }

            if (!walkableCache.TryGetValue(device.LevelId, out IReadOnlyList<IReadOnlyList<Point2>>? net))
            {
                Result<IReadOnlyList<IReadOnlyList<Point2>>> result = WalkableArea.Compute(study, device.LevelId, ops);
                net = result.IsSuccess ? result.Value : null;
                walkableCache[device.LevelId] = net;
            }

            // People are served from the front, so there must be floor just in front of the service side.
            Point2 front = DeviceGeometry.ServicePoint(device) + (device.Facing() * LandingProbeM);
            if (net is null || !WalkableArea.Contains(net, front))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Device '{device.Name}': its service side faces no walkable floor.", device.Id));
            }
        }

        foreach (Underlay underlay in study.Underlays)
        {
            if (!levelIds.Contains(underlay.LevelId))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Underlay '{underlay.Name}' is on a level that doesn't exist.", underlay.Id));
            }
            else if (!study.UnderlayImages.ContainsKey(underlay.Id))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Underlay '{underlay.Name}' has no image (it may be missing from the file).", underlay.Id));
            }
        }
    }

    /// <summary>
    /// Emission rules (D-096, D-099, D-106): a rule needs a sink to send people to; a zero rate or rules on a zone
    /// that isn't a spawn do nothing; an unknown profile falls back to Commuter.
    /// </summary>
    private static void ValidateDemand(Study study, List<ValidationMessage> messages)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var sinks = study.Regions.Where(r => r.IsSink).Select(r => r.Id).ToHashSet();
        foreach (Region region in study.Regions)
        {
            if (region.EmissionRules.Count == 0)
            {
                continue;
            }

            if (!region.IsSpawn)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning,
                    $"'{region.Name}' has emission rules but isn't a spawn zone, so it emits no one.", region.Id));
                continue;
            }

            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                EmissionRule rule = region.EmissionRules[i];
                string label = string.Create(c, $"'{region.Name}' rule {i + 1}");
                if (!(rule.RatePerMinute > 0))
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"{label} has a rate of zero, so it emits no one.", region.Id));
                }

                if (rule.RateCurve is { Count: > 0 } curve && curve.Any(p => p is null || !RateCurves.IsValid(p)))
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"{label} has rate curve points with a negative or missing minute or multiplier; they're ignored.", region.Id));
                }

                ValidateMix(study, region, label, rule, messages);

                if (rule.DestinationMode == DestinationMode.NearestSink)
                {
                    if (!sinks.Any(id => id != region.Id))
                    {
                        messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{label} sends people to the nearest sink, but the study has no other sink zone.", region.Id));
                    }

                    continue;
                }

                bool any = false;
                foreach (SinkWeight destination in rule.Destinations)
                {
                    if (!sinks.Contains(destination.SinkId))
                    {
                        messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{label} sends people to a sink that no longer exists or isn't a sink.", region.Id));
                    }
                    else if (destination.Weight > 0)
                    {
                        any = true;
                    }
                }

                if (!any)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{label} has no chosen sink with a weight above zero.", region.Id));
                }
            }
        }
    }

    /// <summary>
    /// Mix lines and their journeys (D-150–D-154): the shares must add up to more than zero; unknown profiles fall back
    /// to Commuter and unknown journeys to walking straight to a sink; every device type and zone a journey step asks
    /// for must exist. Tags are free text, so unknown tags are fine.
    /// </summary>
    private static void ValidateMix(Study study, Region region, string label, EmissionRule rule, List<ValidationMessage> messages)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        if (rule.Mix is not { } mix || !mix.Any(m => m is not null && m.Share > 0))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"{label} has no mix line with a share above zero, so it emits no one.", region.Id));
            return;
        }

        var deviceTags = study.Devices.Select(d => d.TypeTag).Where(t => !string.IsNullOrEmpty(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedJourneys = new HashSet<Guid>();
        foreach (MixLine line in mix)
        {
            if (line is null || !(line.Share > 0))
            {
                continue;
            }

            if (StudyTemplates.FindProfile(study, line.ProfileId) is null)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"{label} uses a profile that isn't in the library; Commuter is used instead.", region.Id));
            }

            if (line.JourneyId is not { } journeyId || !checkedJourneys.Add(journeyId))
            {
                continue;
            }

            if (StudyTemplates.FindJourney(study, journeyId) is not { } journey)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"{label} uses a journey that isn't in the library; those people walk straight to a sink.", region.Id));
                continue;
            }

            for (int s = 0; s < journey.Steps.Count; s++)
            {
                JourneyStep step = journey.Steps[s];
                string at = string.Create(c, $"{label}: journey '{journey.Name}' step {s + 1}");
                switch (step.Kind)
                {
                    case JourneyStepKind.GoToDevice when string.IsNullOrWhiteSpace(step.TargetTag):
                        messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{at} doesn't say which device type to use.", region.Id));
                        break;

                    case JourneyStepKind.GoToDevice when !deviceTags.Contains(step.TargetTag):
                        messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{at} needs a '{step.TargetTag}' device, but the study has none.", region.Id));
                        break;

                    case JourneyStepKind.PassZone or JourneyStepKind.Dwell when JourneyZones.Matching(study, step).Count == 0:
                        string zone = step.ZoneId is not null ? "its zone, which no longer exists" : string.IsNullOrEmpty(step.TargetTag) ? "a dwell zone" : $"a zone tagged '{step.TargetTag}'";
                        messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{at} needs {zone}, but the study has none.", region.Id));
                        break;
                }

                // Give-up pathway (D-165): a jump must land on a step of this journey.
                if (step.Kind == JourneyStepKind.GoToDevice && step.OnGiveUp == JourneyGiveUp.JumpTo
                    && (step.GiveUpStep < 0 || step.GiveUpStep >= journey.Steps.Count))
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error,
                        string.Create(c, $"{at} jumps to step {step.GiveUpStep + 1} when patience runs out, but the journey has {journey.Steps.Count} steps."), region.Id));
                }
            }
        }
    }
}
