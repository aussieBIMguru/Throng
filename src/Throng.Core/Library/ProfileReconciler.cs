using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Profile exchange (BRIEF §8.6, round 016, D-275): builds the <see cref="ProfileBundle"/> for a study's profile,
/// finds what an incoming bundle refers to that a study lacks (device types, zone tags, train tags, person tags), and
/// applies the user's keep / map / drop answers, giving fresh copies to add to the study library. Pinned zones
/// (<see cref="JourneyNode.ZoneId"/>) belong to the study they came from and are always unpinned on import. Journeys
/// are graphs (D-302): a dropped node is taken out and whatever was wired into it carries on along its first output
/// (a Tag check along "hasn't", since no one holds a tag the study doesn't use).
/// </summary>
public static class ProfileReconciler
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>The profile and every journey the study's mix lines use with it, as copies.</summary>
    public static ProfileBundle BuildBundle(Study study, AgentProfileTemplate profile)
    {
        var journeys = new List<JourneyTemplate>();
        var seen = new HashSet<Guid>();
        void Take(Guid? id)
        {
            if (id is { } journeyId && seen.Add(journeyId) && StudyTemplates.FindJourney(study, journeyId) is { } journey)
            {
                journeys.Add((JourneyTemplate)TemplateCloner.Clone(journey));
            }
        }

        // The profile's default journey (D-304) first, then each journey its mix lines follow.
        Take(profile.DefaultJourneyId);
        foreach (MixLine line in study.Regions.SelectMany(r => r.EmissionRules).SelectMany(rule => rule.Mix ?? []))
        {
            if (line?.ProfileId == profile.Id)
            {
                Take(StudyTemplates.JourneyIdFor(study, line));
            }
        }

        return new ProfileBundle { Profile = (AgentProfileTemplate)TemplateCloner.Clone(profile), Journeys = journeys };
    }

    /// <summary>Every reference in <paramref name="bundle"/> the study can't satisfy, one item per distinct value.</summary>
    public static List<ReconcileItem> Find(Study study, ProfileBundle bundle)
    {
        foreach (JourneyTemplate journey in bundle.Journeys)
        {
            JourneyGraph.Ensure(journey);
        }

        Known known = Known.Of(study, bundle);
        var items = new List<ReconcileItem>();
        var seen = new HashSet<(ReconcileKind, string)>();

        void Need(ReconcileKind kind, string? value, string usedBy)
        {
            string v = (value ?? string.Empty).Trim();
            if (v.Length == 0 || known.Has(kind, v) || !seen.Add((kind, v.ToUpperInvariant())))
            {
                return;
            }

            items.Add(new ReconcileItem(kind, v, usedBy, known.Options(kind)));
        }

        foreach (DevicePreference preference in bundle.Profile.DevicePreferences ?? [])
        {
            Need(ReconcileKind.DeviceType, preference?.Target, $"profile '{bundle.Profile.Name}', device preference");
        }

        foreach (JourneyTemplate journey in bundle.Journeys)
        {
            foreach (JourneyNode node in journey.Nodes)
            {
                string at = $"journey '{journey.Name}', node '{node.Title}'";
                if (TargetKind(node) is { } kind)
                {
                    Need(kind, node.TargetTag, at);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Copies of the bundle's profile and journeys with <paramref name="choices"/> (one per item of
    /// <paramref name="items"/>, in order) applied, marked Imported, with new ids, keys and names where the study or
    /// the built-ins already have them.
    /// </summary>
    public static (AgentProfileTemplate Profile, List<JourneyTemplate> Journeys) Apply(
        Study study, ProfileBundle bundle, IReadOnlyList<ReconcileItem> items, IReadOnlyList<ReconcileChoice> choices)
    {
        var answers = new Dictionary<(ReconcileKind, string), ReconcileChoice>();
        for (int k = 0; k < items.Count; k++)
        {
            answers[(items[k].Kind, items[k].Value.ToUpperInvariant())] = k < choices.Count ? choices[k] : ReconcileChoice.Keep;
        }

        ReconcileChoice Answer(ReconcileKind kind, string? value) =>
            string.IsNullOrWhiteSpace(value) ? ReconcileChoice.Keep
                : answers.GetValueOrDefault((kind, value.Trim().ToUpperInvariant()), ReconcileChoice.Keep);

        // Profile: drop or rename device preferences.
        var profile = (AgentProfileTemplate)TemplateCloner.Clone(bundle.Profile);
        var preferences = new List<DevicePreference>();
        foreach (DevicePreference preference in profile.DevicePreferences ?? [])
        {
            ReconcileChoice answer = Answer(ReconcileKind.DeviceType, preference.Target);
            if (answer.Action == ReconcileAction.Drop)
            {
                continue;
            }

            preferences.Add(answer.Action == ReconcileAction.Map && answer.MapTo is { } to ? preference with { Target = to } : preference);
        }

        profile.DevicePreferences = preferences;
        Rehome(study, profile, "agent");

        var journeys = new List<JourneyTemplate>();
        var newIds = new Dictionary<Guid, Guid>();
        foreach (JourneyTemplate source in bundle.Journeys)
        {
            var journey = (JourneyTemplate)TemplateCloner.Clone(source);
            JourneyGraph.Ensure(journey);
            var kept = new List<JourneyNode>();
            var dropped = new Dictionary<Guid, int>();
            foreach (JourneyNode original in journey.Nodes)
            {
                JourneyNode node = original with { ZoneId = null };
                if (TargetKind(node) is { } kind)
                {
                    ReconcileChoice answer = Answer(kind, node.TargetTag);
                    if (answer.Action == ReconcileAction.Drop && node.Kind != JourneyNodeKind.Spawn)
                    {
                        // Carry on along the first output; a Tag check along "hasn't".
                        dropped[node.Id] = node.Kind == JourneyNodeKind.TagCheck ? 1 : 0;
                        continue;
                    }

                    if (answer.Action == ReconcileAction.Map && answer.MapTo is { } to)
                    {
                        node = node with { TargetTag = to };
                    }
                }

                kept.Add(node);
            }

            journey.Nodes = kept;
            journey.Wires = Bypass(journey.Wires, dropped);
            Guid oldId = journey.Id;
            Rehome(study, journey, "journey");
            newIds[oldId] = journey.Id;
            journeys.Add(journey);
        }

        // The default journey follows its copy's new id.
        if (profile.DefaultJourneyId is { } defaultId && newIds.TryGetValue(defaultId, out Guid moved))
        {
            profile.DefaultJourneyId = moved;
        }

        return (profile, journeys);
    }

    /// <summary>What a node's <see cref="JourneyNode.TargetTag"/> refers to, or null when it refers to nothing to reconcile.</summary>
    private static ReconcileKind? TargetKind(JourneyNode node) => node.Kind switch
    {
        JourneyNodeKind.Device => ReconcileKind.DeviceType,
        JourneyNodeKind.PassZone or JourneyNodeKind.Dwell => ReconcileKind.ZoneTag,
        JourneyNodeKind.Sink when node.SinkKind == JourneySinkKind.Zone => ReconcileKind.ZoneTag,
        JourneyNodeKind.Sink when node.SinkKind == JourneySinkKind.BoardTrain => ReconcileKind.TrainTag,
        JourneyNodeKind.TagCheck => ReconcileKind.Tag,
        _ => null,
    };

    /// <summary>
    /// The wires with <paramref name="dropped"/> nodes taken out: a wire into a dropped node goes on to whatever the
    /// dropped node's carry-on output led to (through further dropped nodes), or is removed when that's nothing.
    /// </summary>
    private static List<JourneyWire> Bypass(List<JourneyWire> wires, Dictionary<Guid, int> dropped)
    {
        if (dropped.Count == 0)
        {
            return [.. wires];
        }

        Guid? Onward(Guid to)
        {
            var visited = new HashSet<Guid>();
            Guid current = to;
            while (dropped.TryGetValue(current, out int port))
            {
                if (!visited.Add(current) || wires.FirstOrDefault(w => w.From == current && w.Port == port) is not { } next)
                {
                    return null;
                }

                current = next.To;
            }

            return current;
        }

        var result = new List<JourneyWire>();
        foreach (JourneyWire wire in wires)
        {
            if (dropped.ContainsKey(wire.From))
            {
                continue;
            }

            if (Onward(wire.To) is { } to)
            {
                result.Add(wire with { To = to });
            }
        }

        return result;
    }

    /// <summary>Imported, and a new id, key and name when the study or the built-ins already hold this id.</summary>
    private static void Rehome(Study study, TemplateBase template, string prefix)
    {
        template.Source = TemplateSource.Imported;
        if (study.Library.Find(template.Id) is null && BuiltInLibrary.Instance.Find(template.Id) is null)
        {
            return;
        }

        Guid id = Guid.NewGuid();
        template.Id = id;
        template.Key = $"{prefix}.imported.{id:N}";
        template.Name = $"{template.Name} (imported)";
    }

    /// <summary>What the study (plus the bundle itself, for tags it grants) already has.</summary>
    private sealed class Known
    {
        private readonly Dictionary<ReconcileKind, HashSet<string>> _sets = new();
        private readonly Dictionary<ReconcileKind, List<string>> _options = new();

        public static Known Of(Study study, ProfileBundle bundle)
        {
            var known = new Known();
            known.Add(ReconcileKind.DeviceType, study.Devices.SelectMany(d => new[] { d.TypeTag, d.PresetKey }), study.Devices.Select(d => d.TypeTag));
            IEnumerable<string> zoneTags = study.Regions.Where(r => r.Kind == RegionKind.Zone).Select(r => r.Tag ?? string.Empty);
            known.Add(ReconcileKind.ZoneTag, zoneTags, zoneTags);
            IEnumerable<string> trainTags = study.TrainServices.Select(t => t.Tag);
            known.Add(ReconcileKind.TrainTag, trainTags, trainTags);
            IEnumerable<string> granted = StudyTemplates.AvailableJourneys(study).Concat(bundle.Journeys)
                .SelectMany(j => (j.Nodes ?? []).Where(n => n is not null)
                    .Select(n => n.Kind == JourneyNodeKind.SetTag && !n.RemoveTag ? n.TargetTag : n.GrantsTag))
                .Concat(study.Regions.SelectMany(r => r.EmissionRules).SelectMany(rule => rule.Mix ?? []).SelectMany(m => m?.SpawnTags ?? []).Select(t => t.Tag));
            known.Add(ReconcileKind.Tag, granted, granted);
            return known;
        }

        public bool Has(ReconcileKind kind, string value) => _sets.TryGetValue(kind, out HashSet<string>? set) && set.Contains(value);

        public IReadOnlyList<string> Options(ReconcileKind kind) => _options.TryGetValue(kind, out List<string>? list) ? list : [];

        private void Add(ReconcileKind kind, IEnumerable<string?> values, IEnumerable<string?> options)
        {
            _sets[kind] = new HashSet<string>(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()), Ci);
            _options[kind] = [.. options.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(Ci).Order(Ci)];
        }
    }
}
