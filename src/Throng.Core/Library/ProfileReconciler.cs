using System.Globalization;
using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Profile exchange (BRIEF §8.6, round 016, D-275): builds the <see cref="ProfileBundle"/> for a study's profile,
/// finds what an incoming bundle refers to that a study lacks (device types, zone tags, train tags, person tags), and
/// applies the user's keep / map / drop answers, giving fresh copies to add to the study library. Pinned zones
/// (<see cref="JourneyStep.ZoneId"/>) belong to the study they came from and are always unpinned on import.
/// </summary>
public static class ProfileReconciler
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>The profile and every journey the study's mix lines use with it, as copies.</summary>
    public static ProfileBundle BuildBundle(Study study, AgentProfileTemplate profile)
    {
        var journeys = new List<JourneyTemplate>();
        var seen = new HashSet<Guid>();
        foreach (MixLine line in study.Regions.SelectMany(r => r.EmissionRules).SelectMany(rule => rule.Mix ?? []))
        {
            if (line?.ProfileId == profile.Id && line.JourneyId is { } id && seen.Add(id) && StudyTemplates.FindJourney(study, id) is { } journey)
            {
                journeys.Add((JourneyTemplate)TemplateCloner.Clone(journey));
            }
        }

        return new ProfileBundle { Profile = (AgentProfileTemplate)TemplateCloner.Clone(profile), Journeys = journeys };
    }

    /// <summary>Every reference in <paramref name="bundle"/> the study can't satisfy, one item per distinct value.</summary>
    public static List<ReconcileItem> Find(Study study, ProfileBundle bundle)
    {
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
            for (int s = 0; s < journey.Steps.Count; s++)
            {
                JourneyStep step = journey.Steps[s];
                string at = string.Create(CultureInfo.InvariantCulture, $"journey '{journey.Name}', step {s + 1}");
                switch (step.Kind)
                {
                    case JourneyStepKind.GoToDevice:
                        Need(ReconcileKind.DeviceType, step.TargetTag, at);
                        break;
                    case JourneyStepKind.PassZone or JourneyStepKind.Dwell:
                        Need(ReconcileKind.ZoneTag, step.TargetTag, at);
                        break;
                    case JourneyStepKind.BoardTrain:
                        Need(ReconcileKind.TrainTag, step.TargetTag, at);
                        break;
                }

                Need(ReconcileKind.Tag, step.NeedsTag, at + " (needs)");
                Need(ReconcileKind.Tag, step.LacksTag, at + " (lacks)");
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
        foreach (JourneyTemplate source in bundle.Journeys)
        {
            var journey = (JourneyTemplate)TemplateCloner.Clone(source);
            var steps = new List<JourneyStep>();
            foreach (JourneyStep original in journey.Steps.Where(s => s is not null))
            {
                ReconcileKind? targetKind = original.Kind switch
                {
                    JourneyStepKind.GoToDevice => ReconcileKind.DeviceType,
                    JourneyStepKind.PassZone or JourneyStepKind.Dwell => ReconcileKind.ZoneTag,
                    JourneyStepKind.BoardTrain => ReconcileKind.TrainTag,
                    _ => null,
                };
                JourneyStep step = original with { ZoneId = null };
                if (targetKind is { } kind)
                {
                    ReconcileChoice answer = Answer(kind, step.TargetTag);
                    if (answer.Action == ReconcileAction.Drop)
                    {
                        continue;
                    }

                    if (answer.Action == ReconcileAction.Map && answer.MapTo is { } to)
                    {
                        step = step with { TargetTag = to };
                    }
                }

                step = step with { NeedsTag = MapTag(Answer(ReconcileKind.Tag, step.NeedsTag), step.NeedsTag), LacksTag = MapTag(Answer(ReconcileKind.Tag, step.LacksTag), step.LacksTag) };
                steps.Add(step);
            }

            // Give-up targets point at step indices; drop any that no longer exist.
            journey.Steps = [.. steps.Select(s => s.GiveUpStep >= steps.Count ? s with { GiveUpStep = 0, OnGiveUp = JourneyGiveUp.KeepWaiting } : s)];
            Rehome(study, journey, "journey");
            journeys.Add(journey);
        }

        return (profile, journeys);
    }

    private static string MapTag(ReconcileChoice answer, string tag) => answer.Action switch
    {
        ReconcileAction.Drop => string.Empty,
        ReconcileAction.Map when answer.MapTo is { } to => to,
        _ => tag,
    };

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
                .SelectMany(j => j.Steps.Where(s => s is not null).Select(s => s.GrantsTag))
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
