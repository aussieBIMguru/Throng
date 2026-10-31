using System.Globalization;
using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Your library (MINE, round 020, D-340–D-351): agent profiles, device definitions and journeys kept across studies in
/// one <see cref="TemplateLibrary"/>. Studies keep their own embedded copies (D-016); a MINE item is embedded under its
/// library id the first time a study uses it (D-346), so UPDATE FROM MY LIBRARY can match it by id, and two copies
/// "differ" when their values differ (D-351). Nothing here reads or writes files or touches a study's undo stack: the
/// app's store saves the library (with a backup, D-348) and study edits go through the undo stack as usual.
/// </summary>
public static class MyLibrary
{
    /// <summary>True for the kinds your library holds (D-341).</summary>
    public static bool Holds(TemplateBase template) => template is AgentProfileTemplate or DeviceTemplate or JourneyTemplate;

    /// <summary>The item with this id in your library (library kinds only), or null.</summary>
    public static TemplateBase? Find(TemplateLibrary? mine, Guid id) =>
        mine?.Find(id) is { } found && Holds(found) ? found : null;

    /// <summary>
    /// True when the study holds a copy of one of your items with different values (D-344, D-351): SELECTION shows
    /// DIFFERS FROM MY LIBRARY. False when either side is missing.
    /// </summary>
    public static bool Differs(Study study, TemplateLibrary? mine, Guid id) =>
        Find(mine, id) is { } saved && study.Library.Find(id) is { } copy && !StudyDefinitions.SameValues(copy, saved);

    /// <summary>
    /// Embeds an item the study is about to use (D-346): your library's copy when it holds the id, else the built-in,
    /// under the same id and marked Embedded; a profile from your library brings its default journey from there too.
    /// Nothing happens when the study already holds the id. Returns what was added (outside the undo stack, as placing a
    /// built-in always has).
    /// </summary>
    public static IReadOnlyList<TemplateBase> EmbedForUse(Study study, TemplateLibrary? mine, Guid id)
    {
        var added = new List<TemplateBase>();
        Embed(study, mine, id, fromMineOnly: false, added);
        return added;
    }

    /// <summary>
    /// Safety net (D-346): embeds every item of your library the study refers to but doesn't hold yet — mix line
    /// profiles and journeys, profiles' default journeys, placed devices' definitions — so the saved file and the
    /// simulation never depend on your library. Built-ins are left to their own first-use paths. Returns what was added.
    /// </summary>
    public static IReadOnlyList<TemplateBase> EmbedReferenced(Study study, TemplateLibrary? mine)
    {
        var added = new List<TemplateBase>();
        if (mine is null || mine.Count == 0)
        {
            return added;
        }

        foreach (Region region in study.Regions)
        {
            foreach (EmissionRule rule in region.EmissionRules ?? [])
            {
                foreach (MixLine line in rule?.Mix ?? [])
                {
                    if (line is null)
                    {
                        continue;
                    }

                    Embed(study, mine, line.ProfileId, fromMineOnly: true, added);
                    if (line.JourneyId is { } journeyId)
                    {
                        Embed(study, mine, journeyId, fromMineOnly: true, added);
                    }
                }
            }
        }

        foreach (AgentProfileTemplate profile in study.Library.AgentProfiles.ToList())
        {
            if (profile?.DefaultJourneyId is { } journeyId)
            {
                Embed(study, mine, journeyId, fromMineOnly: true, added);
            }
        }

        foreach (Device device in study.Devices)
        {
            if (device.TemplateId is { } templateId)
            {
                Embed(study, mine, templateId, fromMineOnly: true, added);
            }
        }

        return added;
    }

    /// <summary>
    /// What SAVE TO MY LIBRARY (D-342) would write for <paramref name="template"/> as the study sees it: a copy of it,
    /// plus a profile's default journey (D-350) unless that's an unchanged built-in (built-ins are everywhere already).
    /// Items your library already holds with the same values are left out; the plan lists those it would replace and the
    /// journeys whose nodes pick specific devices or zones (D-349), so the app can ask first.
    /// </summary>
    /// <param name="study">The open study.</param>
    /// <param name="mine">Your library.</param>
    /// <param name="template">The definition shown in SELECTION (the study's copy, yours, or a built-in).</param>
    public static LibrarySavePlan PlanSave(Study study, TemplateLibrary mine, TemplateBase template)
    {
        var items = new List<TemplateBase>();
        var replaces = new List<TemplateBase>();
        int unchanged = 0;

        void Take(TemplateBase source)
        {
            TemplateBase copy = TemplateCloner.Clone(source);
            copy.Source = TemplateSource.User;
            if (copy is JourneyTemplate journey)
            {
                JourneyGraph.Ensure(journey);
            }

            if (Find(mine, copy.Id) is { } existing)
            {
                if (StudyDefinitions.SameValues(existing, copy))
                {
                    unchanged++;
                    return;
                }

                replaces.Add(existing);
            }

            items.Add(copy);
        }

        Take(template);
        if (template is AgentProfileTemplate { DefaultJourneyId: { } journeyId }
            && StudyTemplates.FindJourney(study, journeyId, mine) is { } defaultJourney
            && !IsPristineBuiltIn(defaultJourney))
        {
            Take(defaultJourney);
        }

        List<JourneyTemplate> withPicks = [.. items.OfType<JourneyTemplate>().Where(j => (j.Nodes ?? []).Any(n => n is { HasPicks: true }))];
        return new LibrarySavePlan(items, replaces, withPicks, unchanged);
    }

    /// <summary>Writes the plan's items into your library, replacing items with the same id in place.</summary>
    public static void ApplySave(TemplateLibrary mine, LibrarySavePlan plan)
    {
        foreach (TemplateBase item in plan.Items)
        {
            Put(mine, item);
        }
    }

    /// <summary>Adds <paramref name="item"/> to your library, or replaces the item with its id where it is.</summary>
    public static void Put(TemplateLibrary mine, TemplateBase item)
    {
        switch (item)
        {
            case AgentProfileTemplate profile:
                PutIn(mine.AgentProfiles, profile);
                break;
            case DeviceTemplate device:
                PutIn(mine.Devices, device);
                break;
            case JourneyTemplate journey:
                PutIn(mine.Journeys ??= [], journey);
                break;
        }
    }

    /// <summary>Removes the item with this id from your library (D-345); false when it isn't there.</summary>
    public static bool Remove(TemplateLibrary mine, Guid id) =>
        mine.AgentProfiles.RemoveAll(t => t?.Id == id)
        + mine.Devices.RemoveAll(t => t?.Id == id)
        + (mine.Journeys?.RemoveAll(t => t?.Id == id) ?? 0) > 0;

    /// <summary>Renames the item with this id in your library (D-345); false when it isn't there or the name is blank.</summary>
    public static bool Rename(TemplateLibrary mine, Guid id, string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || Find(mine, id) is not { } item)
        {
            return false;
        }

        item.Name = trimmed;
        return true;
    }

    /// <summary>
    /// The .thronglib content for a ticked set of your items (D-347): copies of them, plus each ticked profile's default
    /// journey when your library holds it (so the profile works where it's imported).
    /// </summary>
    public static TemplateLibrary BuildExport(TemplateLibrary mine, IEnumerable<Guid> ids)
    {
        var export = new TemplateLibrary();
        var wanted = new List<Guid>(ids);
        foreach (Guid id in wanted.ToList())
        {
            if (Find(mine, id) is AgentProfileTemplate { DefaultJourneyId: { } journeyId } && Find(mine, journeyId) is JourneyTemplate && !wanted.Contains(journeyId))
            {
                wanted.Add(journeyId);
            }
        }

        foreach (Guid id in wanted)
        {
            if (Find(mine, id) is { } item && export.Find(id) is null)
            {
                export.Add(TemplateCloner.Clone(item));
            }
        }

        return export;
    }

    /// <summary>
    /// The incoming items (library kinds) whose ids your library holds with different values, in the order the import
    /// dialog lists them (profiles, devices, journeys): one <see cref="ReconcileKind.LibraryItem"/> each (D-347).
    /// Items you hold with the same values, and new ids, need no answer.
    /// </summary>
    public static List<ReconcileItem> FindImportClashes(TemplateLibrary mine, TemplateLibrary incoming)
    {
        var items = new List<ReconcileItem>();
        foreach (TemplateBase item in LibraryItems(incoming))
        {
            if (Find(mine, item.Id) is { } existing && !StudyDefinitions.SameValues(existing, item))
            {
                string usedBy = string.Equals(existing.Name, item.Name, StringComparison.Ordinal)
                    ? $"{KindName(item)} · yours has different values"
                    : $"{KindName(item)} · yours is '{existing.Name}', with different values";
                items.Add(new ReconcileItem(ReconcileKind.LibraryItem, item.Name, usedBy, []));
            }
        }

        return items;
    }

    /// <summary>
    /// Imports a .thronglib into your library (D-347): new ids are added, items you hold with the same values are left,
    /// and each clash follows its answer (one per <see cref="FindImportClashes"/> item, in order; missing answers keep
    /// both). Keeping both adds the incoming item as a copy with a new id, key and "(imported)" name; a profile whose
    /// default journey came in as such a copy follows it.
    /// </summary>
    public static LibraryImportResult ApplyImport(TemplateLibrary mine, TemplateLibrary incoming, IReadOnlyList<ReconcileChoice> choices)
    {
        // Answers by id, in the order FindImportClashes listed the clashes.
        var answers = new Dictionary<Guid, ReconcileChoice>();
        int k = 0;
        foreach (TemplateBase item in LibraryItems(incoming))
        {
            if (Find(mine, item.Id) is { } existing && !StudyDefinitions.SameValues(existing, item))
            {
                answers[item.Id] = k < choices.Count ? choices[k] : ReconcileChoice.Keep;
                k++;
            }
        }

        int added = 0, replaced = 0, skipped = 0, unchanged = 0;
        var newIds = new Dictionary<Guid, Guid>();

        // Journeys first, so profiles can follow a journey that came in as a copy.
        IEnumerable<TemplateBase> ordered = LibraryItems(incoming).OfType<JourneyTemplate>().Cast<TemplateBase>()
            .Concat(LibraryItems(incoming).Where(t => t is not JourneyTemplate));
        foreach (TemplateBase source in ordered)
        {
            TemplateBase item = TemplateCloner.Clone(source);
            item.Source = TemplateSource.User;
            if (item is JourneyTemplate journey)
            {
                JourneyGraph.Ensure(journey);
            }

            if (item is AgentProfileTemplate { DefaultJourneyId: { } journeyId } profile && newIds.TryGetValue(journeyId, out Guid moved))
            {
                profile.DefaultJourneyId = moved;
            }

            if (Find(mine, item.Id) is null)
            {
                Put(mine, item);
                added++;
                continue;
            }

            if (!answers.TryGetValue(item.Id, out ReconcileChoice? answer))
            {
                unchanged++;
                continue;
            }

            switch (answer.Action)
            {
                case ReconcileAction.Drop:
                    skipped++;
                    break;

                case ReconcileAction.Replace:
                    Put(mine, item);
                    replaced++;
                    break;

                default:
                    Guid oldId = item.Id;
                    Rekey(mine, item);
                    newIds[oldId] = item.Id;
                    Put(mine, item);
                    added++;
                    break;
            }
        }

        return new LibraryImportResult(added, replaced, skipped, unchanged);
    }

    /// <summary>"Agent profile", "Device" or "Journey", for dialogs.</summary>
    public static string KindName(TemplateBase template) => template switch
    {
        AgentProfileTemplate => "Agent profile",
        DeviceTemplate => "Device",
        JourneyTemplate => "Journey",
        _ => "Item",
    };

    /// <summary>True for a journey that is a built-in, or the study's unchanged copy of one.</summary>
    private static bool IsPristineBuiltIn(JourneyTemplate journey) =>
        BuiltInLibrary.Instance.Find(journey.Id) is JourneyTemplate builtIn && StudyDefinitions.SameValues(journey, builtIn);

    /// <summary>The library kinds of <paramref name="library"/>, in dialog order: profiles, devices, journeys.</summary>
    private static IEnumerable<TemplateBase> LibraryItems(TemplateLibrary library) =>
        library.AgentProfiles.Where(t => t is not null).Cast<TemplateBase>()
            .Concat(library.Devices.Where(t => t is not null))
            .Concat((library.Journeys ?? []).Where(t => t is not null));

    private static void Embed(Study study, TemplateLibrary? mine, Guid id, bool fromMineOnly, List<TemplateBase> added)
    {
        if (id == Guid.Empty || study.Library.Find(id) is not null)
        {
            return;
        }

        TemplateBase? fromMine = Find(mine, id);
        TemplateBase? source = fromMine ?? (fromMineOnly ? null : BuiltInLibrary.Instance.Find(id));
        if (source is null)
        {
            return;
        }

        if (source is JourneyTemplate)
        {
            study.Library.Journeys ??= [];
        }

        added.Add(study.Library.AddCopy(source, TemplateSource.Embedded));

        // A profile from your library brings its default journey from there too (D-350), so it works as saved.
        if (fromMine is AgentProfileTemplate { DefaultJourneyId: { } journeyId })
        {
            Embed(study, mine, journeyId, fromMineOnly: true, added);
        }
    }

    private static void PutIn<T>(List<T> list, T item)
        where T : TemplateBase
    {
        int index = list.FindIndex(t => t?.Id == item.Id);
        if (index >= 0)
        {
            list[index] = item;
        }
        else
        {
            list.Add(item);
        }
    }

    /// <summary>A new id and key, and a name ending "(imported)" that your library doesn't use for that kind.</summary>
    private static void Rekey(TemplateLibrary mine, TemplateBase item)
    {
        string prefix = item switch
        {
            AgentProfileTemplate => "agent",
            DeviceTemplate => "device",
            _ => "journey",
        };
        Guid id = Guid.NewGuid();
        item.Id = id;
        item.Key = $"{prefix}.mine.{id:N}";

        IEnumerable<string> names = item switch
        {
            AgentProfileTemplate => mine.AgentProfiles.Select(t => t.Name),
            DeviceTemplate => mine.Devices.Select(t => t.Name),
            _ => (mine.Journeys ?? []).Select(t => t.Name),
        };
        var taken = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string baseName = $"{item.Name} (imported)";
        string name = baseName;
        for (int n = 2; taken.Contains(name); n++)
        {
            name = $"{item.Name} (imported {n.ToString(CultureInfo.InvariantCulture)})";
        }

        item.Name = name;
    }
}
