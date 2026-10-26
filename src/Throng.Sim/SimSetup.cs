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
    /// <summary>How far out from the service points people wait for a lane (D-167), metres.</summary>
    public const double LaneHoldOutM = 2.0;

    /// <summary>How far past the end of the device's width people wait for a lane (D-167), metres.</summary>
    public const double LaneHoldAsideM = 1.0;

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
        var tags = new TagTable();

        // Profiles, journeys and rules first, so the step-free table is only built when needed.
        var profiles = new List<SimProfile>();
        var profileIndex = new Dictionary<Guid, int>();
        var journeyTemplates = new List<JourneyTemplate>();
        var journeyIndex = new Dictionary<Guid, int>();
        var pending = new List<(Region Region, int RuleNumber, EmissionRule Rule, List<PendingLine> Lines)>();
        foreach (Region region in study.Regions.Where(r => r.IsSpawn))
        {
            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                EmissionRule rule = region.EmissionRules[i];
                if (!(rule.RatePerMinute > 0))
                {
                    continue;
                }

                var lines = new List<PendingLine>();
                foreach (MixLine mix in rule.Mix ?? [])
                {
                    if (mix is null || !(mix.Share > 0))
                    {
                        continue;
                    }

                    AgentProfileTemplate template = StudyTemplates.ProfileOrFallback(study, mix.ProfileId);
                    if (!profileIndex.TryGetValue(template.Id, out int p))
                    {
                        p = profiles.Count;
                        profileIndex[template.Id] = p;
                        (float r, float g, float b) = ParseColour(template.ColorHex);
                        profiles.Add(new SimProfile { Index = p, Template = template, Mobility = NavRoutes.ClassOf(template), R = r, G = g, B = b });
                    }

                    int j = -1;
                    if (mix.JourneyId is { } journeyId && StudyTemplates.FindJourney(study, journeyId) is { } journey
                        && !journeyIndex.TryGetValue(journey.Id, out j))
                    {
                        j = journeyTemplates.Count;
                        journeyIndex[journey.Id] = j;
                        journeyTemplates.Add(journey);
                    }

                    var spawnTags = new List<(ulong, double)>();
                    foreach (SpawnTag tag in mix.SpawnTags ?? [])
                    {
                        if (tag is not null && tag.Percent > 0 && tags.Bit(tag.Tag, findings) is { } bit && bit != 0)
                        {
                            spawnTags.Add((bit, Math.Clamp(tag.Percent / 100.0, 0, 1)));
                        }
                    }

                    lines.Add(new PendingLine(p, j, mix.Share, [.. spawnTags]));
                }

                if (lines.Count > 0)
                {
                    pending.Add((region, i + 1, rule, lines));
                }
            }
        }

        // Devices and zones the journeys use become extra route targets (D-149, D-152).
        var extraTargets = new List<NavSink>();
        List<SimDevice> devices = BuildDevices(study, nav, journeyTemplates, extraTargets, findings);
        Dictionary<Guid, int> zoneTargets = BuildZoneTargets(study, nav, journeyTemplates, extraTargets, findings);
        foreach (SimProfile profile in profiles)
        {
            // Device preferences (D-166): seconds off each device's choice cost for this profile.
            profile.DeviceBonusS = [.. devices.Select(d => profile.Template.DeviceBonusS(d.PresetKey, d.TypeTag))];
        }

        if (extraTargets.Count > 0)
        {
            nav = nav.WithTargets(extraTargets);
        }

        // Train doors are exit sinks; a Board train step targets the doors of its matching services (D-186).
        var exitByRegion = new Dictionary<Guid, int>();
        foreach (NavSink sink in nav.Sinks)
        {
            if (sink.Role == NavTargetRole.Exit)
            {
                exitByRegion.TryAdd(sink.RegionId, sink.Index);
            }
        }

        int[] BoardTargets(string? tag) =>
            [.. TrainService.Matching(study, tag).SelectMany(t => t.DoorZoneIds).Select(id => exitByRegion.GetValueOrDefault(id, -1)).Where(s => s >= 0).Distinct()];

        List<SimJourney> journeys = [.. journeyTemplates.Select((t, index) => CompileJourney(study, t, index, devices, zoneTargets, BoardTargets, tags, findings))];

        cancel.ThrowIfCancellationRequested();
        NavRoutes routes = NavRoutes.Build(nav, profiles.Any(p => p.Mobility == MobilityClass.StepFree), cancel);

        var sinkByRegion = new Dictionary<Guid, int>();
        foreach (NavSink sink in nav.Sinks)
        {
            if (sink.Role == NavTargetRole.Exit)
            {
                sinkByRegion[sink.RegionId] = sink.Index;
            }
        }

        var emitters = new List<SimEmitter>();
        foreach ((Region region, int number, EmissionRule rule, List<PendingLine> lines) in pending)
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
                candidates.AddRange(nav.Sinks.Where(s => s.Role == NavTargetRole.Exit && s.RegionId != region.Id).Select(s => (s.Index, 1.0)));
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

            // Reachability from the middle of the zone, plain nominal costs, for each routing class in the mix. A sink
            // is kept when every class can reach it.
            Point2 from = nav.Surfaces[floor].CellCentre(cells[cells.Count / 2]);
            List<MobilityClass> classes = [.. lines.Select(l => profiles[l.Profile].Mobility).Distinct()];
            var reachable = new List<(int Sink, double Weight)>();
            var unreachable = new List<string>();
            foreach ((int s, double w) in candidates)
            {
                bool all = classes.All(mobility => double.IsFinite(RoutePlanner.Choose(routes, floor, from.X, from.Y, s, mobility, null, null, out _, out _)));
                if (all)
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
                string who = classes.Contains(MobilityClass.StepFree) ? " for step-free profiles" : string.Empty;
                findings.Add(new ValidationMessage(ValidationSeverity.Error, $"{label} can't reach any of its sinks{who}.", region.Id));
                continue;
            }

            if (unreachable.Count > 0 && rule.DestinationMode == DestinationMode.ChosenSinks)
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning,
                    $"{label} can't reach {string.Join(", ", unreachable.Select(n => $"'{n}'"))}; those sinks are skipped.", region.Id));
            }

            // Journey steps: some target of every step must be reachable from the zone (D-149).
            bool stepsOk = true;
            foreach (PendingLine line in lines.Where(l => l.Journey >= 0).DistinctBy(l => (l.Journey, l.Profile)))
            {
                MobilityClass mobility = profiles[line.Profile].Mobility;
                foreach (SimStep step in journeys[line.Journey].Steps)
                {
                    if (step.Kind == JourneyStepKind.Exit)
                    {
                        continue;
                    }

                    bool any = step.Targets.Any(t =>
                    {
                        int target = step.Kind == JourneyStepKind.GoToDevice ? devices[t].Target : t;
                        return double.IsFinite(RoutePlanner.Choose(routes, floor, from.X, from.Y, target, mobility, null, null, out _, out _));
                    });
                    if (!any)
                    {
                        string who = mobility == MobilityClass.StepFree ? " for step-free profiles" : string.Empty;
                        findings.Add(new ValidationMessage(ValidationSeverity.Error,
                            $"{label}: journey '{journeys[line.Journey].Name}' can't reach any '{step.Label}'{who}.", region.Id));
                        stepsOk = false;
                    }
                }
            }

            if (!stepsOk)
            {
                continue;
            }

            emitters.Add(new SimEmitter
            {
                Index = emitters.Count,
                RegionId = region.Id,
                Label = label,
                Rule = rule,
                Lines = [.. lines.Select(l => new SimMixLine { Profile = l.Profile, Journey = l.Journey, Share = l.Share, SpawnTags = l.SpawnTags })],
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

        List<SimTrain> trains = BuildTrains(study, nav, emitters, exitByRegion);

        StudySettings settings = study.Settings;
        var model = new SimModel
        {
            Routes = routes,
            Profiles = profiles,
            Emitters = emitters,
            Journeys = journeys,
            Devices = devices,
            Trains = trains,
            TagNames = tags.Names,
            Seed = settings.Seed,
            StartTimeOfDay = settings.StartTimeOfDay,
            DurationS = settings.EndCondition == StudyEndCondition.FixedDuration ? Math.Max(1, settings.DurationS) : null,
            TimeStepS = Math.Clamp(settings.TimeStepS, 0.02, 0.2),
            KeepSide = settings.KeepSide,
            Findings = findings,
        };
        return new SimSetupResult(model, findings);
    }

    /// <summary>A mix line before its emitter is built.</summary>
    private sealed record PendingLine(int Profile, int Journey, double Share, (ulong Bit, double Probability)[] SpawnTags);

    /// <summary>Tag names to bits (D-150): case-insensitive, at most 64 per run; extra tags are ignored with a warning.</summary>
    private sealed class TagTable
    {
        private readonly Dictionary<string, int> _bits = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _names = [];
        private bool _warned;

        /// <summary>Tag names by bit.</summary>
        public IReadOnlyList<string> Names => _names;

        /// <summary>The bit for a tag (allocating one), 0 for an empty tag, or null when the 64 bits are used up.</summary>
        public ulong? Bit(string? tag, List<ValidationMessage> findings)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return 0;
            }

            string key = tag.Trim();
            if (!_bits.TryGetValue(key, out int bit))
            {
                if (_names.Count >= 64)
                {
                    if (!_warned)
                    {
                        findings.Add(new ValidationMessage(ValidationSeverity.Warning, "More than 64 different tags are in use; the extra ones are ignored."));
                        _warned = true;
                    }

                    return null;
                }

                bit = _names.Count;
                _bits[key] = bit;
                _names.Add(key);
            }

            return 1UL << bit;
        }
    }

    /// <summary>
    /// The devices some journey step asks for (by type tag), with their service areas as route targets and their
    /// queue lines (D-152, D-153).
    /// </summary>
    private static List<SimDevice> BuildDevices(Study study, NavWorld nav, List<JourneyTemplate> journeys, List<NavSink> extraTargets, List<ValidationMessage> findings)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JourneyTemplate journey in journeys)
        {
            foreach (JourneyStep step in journey.Steps)
            {
                if (step is { Kind: JourneyStepKind.GoToDevice } && !string.IsNullOrWhiteSpace(step.TargetTag))
                {
                    wanted.Add(step.TargetTag.Trim());
                }
            }
        }

        var devices = new List<SimDevice>();
        var lanesByDevice = new List<(NavSink Target, int Floor, List<int> Lanes)>();
        int laneBase = 0, lineBase = 0;
        foreach (Device device in study.Devices)
        {
            if (!wanted.Contains(device.TypeTag ?? string.Empty) || !nav.FloorByLevel.TryGetValue(device.LevelId, out int floor))
            {
                continue;
            }

            NavSurface surface = nav.Surfaces[floor];
            DeviceTemplate? template = StudyTemplates.FindDeviceTemplate(study, device);
            Point2 facing = device.Facing();
            int lanes = Math.Max(1, device.Lanes);

            // Lane lines per side, plus the shared snake when a queue is drawn (D-153, D-163).
            QueueLineSet queue = QueueLines.Build(device, p => surface.IsWalkable(p.X, p.Y));
            Point2[][] slots = queue.Snake is { } snake ? [.. queue.Lines, snake] : queue.Lines;
            int snakeLine = queue.Snake is not null ? queue.Lines.Length : -1;

            // Target: the lanes of a pass-through device; the floor around the service points otherwise.
            List<int> cells = device.PassThrough
                ? NavWorldBuilder.CellsInside(surface, [DeviceGeometry.Footprint(device)], walkableOnly: true)
                : NavWorldBuilder.CellsInside(surface, queue.Lines.Where(l => l.Length > 0).Select(l => (IReadOnlyList<Point2>)DeviceGeometry.Footprint(l[0], facing, 0.9, 0.9)), walkableOnly: true);
            if (queue.Snake is { Length: 0 })
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning, $"Device '{device.Name}': its drawn queue starts off the floor, so people queue at the lanes instead. Redraw it or CLEAR QUEUE.", device.Id));
            }

            if (cells.Count == 0 || queue.Lines.All(l => l.Length == 0))
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning, $"Device '{device.Name}' has no walkable floor at its service side, so no one can use it.", device.Id));
                continue;
            }

            int target = nav.Sinks.Count + extraTargets.Count;
            var deviceTarget = new NavSink { Index = target, RegionId = device.Id, Name = device.Name, Floor = floor, Cells = [.. cells], Role = NavTargetRole.Device };
            extraTargets.Add(deviceTarget);
            lanesByDevice.Add((deviceTarget, floor, device.PassThrough ? cells : []));
            Distribution service = template?.ServiceTime ?? Distribution.Fixed(5);
            devices.Add(new SimDevice
            {
                Index = devices.Count,
                DeviceId = device.Id,
                Name = device.Name,
                TypeTag = device.TypeTag ?? string.Empty,
                PresetKey = device.PresetKey ?? string.Empty,
                WidthM = device.WidthM,
                Floor = floor,
                Target = target,
                Centre = device.Position,
                Facing = facing,
                DepthM = device.DepthM,
                Lanes = lanes,
                PassThrough = device.PassThrough,
                LaneSide = device.PassThrough
                    ? [.. Enumerable.Range(0, lanes).Select(l => device.DirectionOf(l) switch { GateLaneDirection.FromFront => 0, GateLaneDirection.FromBack => 1, _ => -1 })]
                    : [],
                ServiceTime = service,
                MeanServiceS = Math.Max(0.1, service.ExpectedValue),
                ChoicePenaltyS = -(template?.ChoiceWeight ?? 0),
                LaneBase = laneBase,
                LineBase = lineBase,
                Slots = slots,
                SnakeLine = snakeLine,
                HoldPoints = BuildHoldPoints(surface, device),
            });
            laneBase += lanes;
            lineBase += slots.Length;
        }

        // People heading for one device don't walk through another gate's lanes unserved: each device target's field
        // treats the other pass-through devices on its floor as closed (r009b). Exit and zone fields are unchanged, so
        // people without a gate step still cross gate lines as before.
        foreach ((NavSink deviceTarget, int floor, _) in lanesByDevice)
        {
            List<int> avoid = [.. lanesByDevice
                .Where(other => !ReferenceEquals(other.Target, deviceTarget) && other.Floor == floor)
                .SelectMany(other => other.Lanes)
                .Except(deviceTarget.Cells)];
            deviceTarget.AvoidCells = [.. avoid];
        }

        return devices;
    }

    /// <summary>
    /// Hold points for people waiting for a lane (D-167): on each side, 2 m out from the service points and 1 m past
    /// either end of the device's width, so they stand clear of the lane mouths and of people coming through. Only
    /// spots with floor around them are kept.
    /// </summary>
    private static Point2[][] BuildHoldPoints(NavSurface surface, Device device)
    {
        Point2 facing = device.Facing();
        Point2 across = facing.Perpendicular();
        int sides = device.PassThrough ? 2 : 1;
        var points = new Point2[sides][];
        for (int side = 0; side < sides; side++)
        {
            Point2 outward = side == 0 ? facing : facing * -1;
            Point2 front = device.Position + (outward * ((device.DepthM * 0.5) + SimDevice.ServiceOffsetM + LaneHoldOutM));
            var kept = new List<Point2>(2);
            foreach (double end in new[] { -1.0, 1.0 })
            {
                Point2 p = front + (across * (end * ((device.WidthM * 0.5) + LaneHoldAsideM)));
                if (QueueLines.Clear(q => surface.IsWalkable(q.X, q.Y), p, outward))
                {
                    kept.Add(p);
                }
            }

            points[side] = [.. kept];
        }

        return points;
    }

    /// <summary>Route targets for every zone a journey's zone step can use (D-149), keyed by region id.</summary>
    private static Dictionary<Guid, int> BuildZoneTargets(Study study, NavWorld nav, List<JourneyTemplate> journeys, List<NavSink> extraTargets, List<ValidationMessage> findings)
    {
        var targets = new Dictionary<Guid, int>();
        foreach (JourneyStep step in journeys.SelectMany(j => j.Steps).Where(s => s is { Kind: JourneyStepKind.PassZone or JourneyStepKind.Dwell }))
        {
            foreach (Region zone in JourneyZones.Matching(study, step))
            {
                if (targets.ContainsKey(zone.Id) || !nav.FloorByLevel.TryGetValue(zone.LevelId, out int floor))
                {
                    continue;
                }

                List<int> cells = NavWorldBuilder.CellsInside(nav.Surfaces[floor], [zone.Outline], walkableOnly: true);
                if (cells.Count == 0)
                {
                    findings.Add(new ValidationMessage(ValidationSeverity.Warning, $"Zone '{zone.Name}' has no walkable floor inside it, so journeys skip it.", zone.Id));
                    targets[zone.Id] = -1;
                    continue;
                }

                int target = nav.Sinks.Count + extraTargets.Count;
                extraTargets.Add(new NavSink { Index = target, RegionId = zone.Id, Name = zone.Name, Floor = floor, Cells = [.. cells], Role = NavTargetRole.Zone });
                targets[zone.Id] = target;
            }
        }

        return targets;
    }

    /// <summary>Compiles a journey's steps against the devices and zone targets; the result always ends in an exit.</summary>
    private static SimJourney CompileJourney(
        Study study, JourneyTemplate template, int index, List<SimDevice> devices, Dictionary<Guid, int> zoneTargets, Func<string?, int[]> boardTargets,
        TagTable tags, List<ValidationMessage> findings)
    {
        // Template step index → compiled index (null steps and anything after the first exit are dropped), so a
        // give-up jump (D-165) lands on the step the editor showed.
        var compiledIndex = new int[template.Steps.Count];
        int next = 0;
        bool ended = false;
        for (int k = 0; k < template.Steps.Count; k++)
        {
            JourneyStep? candidate = template.Steps[k];
            compiledIndex[k] = ended || candidate is null ? -1 : next++;
            ended |= candidate is { EndsJourney: true };
        }

        var steps = new List<SimStep>();
        foreach (JourneyStep step in template.Steps)
        {
            if (step is null)
            {
                continue;
            }

            int giveUpStep = step.OnGiveUp == JourneyGiveUp.JumpTo && step.GiveUpStep >= 0 && step.GiveUpStep < compiledIndex.Length
                ? compiledIndex[step.GiveUpStep]
                : -1;

            int[] targets = step.Kind switch
            {
                JourneyStepKind.GoToDevice => [.. devices.Where(d => string.Equals(d.TypeTag, step.TargetTag?.Trim(), StringComparison.OrdinalIgnoreCase)).Select(d => d.Index)],
                JourneyStepKind.PassZone or JourneyStepKind.Dwell => [.. JourneyZones.Matching(study, step).Select(z => zoneTargets.GetValueOrDefault(z.Id, -1)).Where(t => t >= 0)],
                JourneyStepKind.BoardTrain => boardTargets(step.TargetTag),
                _ => [],
            };
            string label = step.Kind switch
            {
                JourneyStepKind.GoToDevice => string.IsNullOrWhiteSpace(step.TargetTag) ? "device" : step.TargetTag.Trim(),
                JourneyStepKind.Exit => "exit",
                JourneyStepKind.BoardTrain => string.IsNullOrWhiteSpace(step.TargetTag) ? "train" : $"{step.TargetTag.Trim()} train",
                _ => string.IsNullOrWhiteSpace(step.TargetTag) ? "zone" : step.TargetTag.Trim(),
            };
            steps.Add(new SimStep
            {
                Kind = step.Kind,
                Targets = targets,
                Needs = tags.Bit(step.NeedsTag, findings) ?? 0,
                Lacks = tags.Bit(step.LacksTag, findings) ?? 0,
                Grants = tags.Bit(step.GrantsTag, findings) ?? 0,
                Dwell = step.DwellS ?? Distribution.Fixed(0),
                Label = label,
                OnGiveUp = step.Kind == JourneyStepKind.GoToDevice ? step.OnGiveUp : JourneyGiveUp.KeepWaiting,
                GiveUpStep = giveUpStep,
            });
            if (step.EndsJourney)
            {
                break;
            }
        }

        if (steps.Count == 0 || steps[^1].Kind is not (JourneyStepKind.Exit or JourneyStepKind.BoardTrain))
        {
            steps.Add(new SimStep { Kind = JourneyStepKind.Exit, Targets = [], Needs = 0, Lacks = 0, Grants = 0, Dwell = Distribution.Fixed(0), Label = "exit" });
        }

        return new SimJourney { Index = index, Name = template.Name, Steps = [.. steps] };
    }

    /// <summary>Depth of the drawn train box beyond the platform edge, and how far it runs past the end doors, metres.</summary>
    private const double TrainDepthM = 3.0, TrainOverhangM = 3.0;

    /// <summary>
    /// Train services ready to run (D-184): doors with an exit sink, their emitters marked as the service's (so they
    /// only release people at arrivals), the platform direction found by testing which side of the doors has floor,
    /// and the box drawn on the other (track) side.
    /// </summary>
    private static List<SimTrain> BuildTrains(Study study, NavWorld nav, List<SimEmitter> emitters, Dictionary<Guid, int> exitByRegion)
    {
        var trains = new List<SimTrain>();
        foreach (TrainService service in study.TrainServices)
        {
            if (service is null)
            {
                continue;
            }

            List<Region> doors = [.. service.DoorZoneIds.Select(study.FindRegion).OfType<Region>().Where(r => exitByRegion.ContainsKey(r.Id))];
            if (doors.Count == 0 || !nav.FloorByLevel.TryGetValue(doors[0].LevelId, out int floor))
            {
                continue;
            }

            doors = [.. doors.Where(d => d.LevelId == doors[0].LevelId)];
            NavSurface surface = nav.Surfaces[floor];
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (Point2 p in doors.SelectMany(d => d.Outline))
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            // Doors run along the longer side of their box; the platform is the side with floor just beyond it.
            bool alongX = maxX - minX >= maxY - minY;
            Point2 along = alongX ? new Point2(1, 0) : new Point2(0, 1);
            Point2 across = alongX ? new Point2(0, 1) : new Point2(1, 0);
            double midAlong = alongX ? (minX + maxX) / 2 : (minY + maxY) / 2;
            double lowSide = alongX ? minY : minX, highSide = alongX ? maxY : maxX;
            Point2 At(double a, double c) => alongX ? new Point2(a, c) : new Point2(c, a);
            bool floorHigh = surface.IsWalkable(At(midAlong, highSide + 1.5).X, At(midAlong, highSide + 1.5).Y);
            bool floorLow = surface.IsWalkable(At(midAlong, lowSide - 1.5).X, At(midAlong, lowSide - 1.5).Y);
            bool inwardHigh = floorHigh || !floorLow;
            Point2 inward = inwardHigh ? across : across * -1;
            double edge = inwardHigh ? lowSide : highSide;
            double outer = inwardHigh ? edge - TrainDepthM : edge + TrainDepthM;
            double a0 = (alongX ? minX : minY) - TrainOverhangM, a1 = (alongX ? maxX : maxY) + TrainOverhangM;

            var centres = new Point2[doors.Count];
            var halves = new double[doors.Count];
            var doorEmitters = new (int, double)[doors.Count][];
            for (int k = 0; k < doors.Count; k++)
            {
                IReadOnlyList<Point2> outline = doors[k].Outline;
                double dx0 = outline.Min(p => p.X), dx1 = outline.Max(p => p.X), dy0 = outline.Min(p => p.Y), dy1 = outline.Max(p => p.Y);
                centres[k] = new Point2((dx0 + dx1) / 2, (dy0 + dy1) / 2);
                halves[k] = (alongX ? dx1 - dx0 : dy1 - dy0) / 2;
                Guid id = doors[k].Id;
                doorEmitters[k] = [.. emitters.Where(e => e.RegionId == id).Select(e => (e.Index, Math.Max(1e-6, e.Rule.RatePerMinute)))];
                foreach (SimEmitter emitter in emitters.Where(e => e.RegionId == id))
                {
                    emitter.TrainService = trains.Count;
                }
            }

            (double[] minutes, double[] multipliers) = RateCurves.Compile(service.RateCurve);
            trains.Add(new SimTrain
            {
                Index = trains.Count,
                ServiceId = service.Id,
                Name = service.Name,
                LevelId = doors[0].LevelId,
                Floor = floor,
                DoorRegionIds = [.. doors.Select(d => d.Id)],
                DoorSinks = [.. doors.Select(d => exitByRegion[d.Id])],
                DoorCentres = centres,
                DoorHalfWidths = halves,
                DoorEmitters = doorEmitters,
                Inward = inward,
                Along = along,
                Box = [At(a0, edge), At(a1, edge), At(a1, outer), At(a0, outer)],
                HeadwayS = Math.Max(20, service.HeadwayS),
                OffsetS = Math.Max(0, service.OffsetS),
                DwellS = Math.Max(10, service.DwellS),
                AlightOverS = Math.Clamp(service.AlightOverS, 1, Math.Max(10, service.DwellS)),
                AlightPerTrain = Math.Max(0, service.AlightPerTrain),
                Capacity = Math.Max(0, service.Capacity),
                CurveMinutes = minutes,
                CurveMultipliers = multipliers,
            });
        }

        return trains;
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
