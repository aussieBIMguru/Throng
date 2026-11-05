using System.Globalization;
using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Your library (MINE, round 020, D-340–D-351): agent profiles, device definitions, journeys and (round 024b, D-388)
/// group templates kept across studies in
/// one <see cref="TemplateLibrary"/>. Studies keep their own embedded copies (D-016); a MINE item is embedded under its
/// library id the first time a study uses it (D-346), so UPDATE FROM MY LIBRARY can match it by id, and two copies
/// "differ" when their values differ (D-351). Nothing here reads or writes files or touches a study's undo stack: the
/// app's store saves the library (with a backup, D-348) and study edits go through the undo stack as usual.
/// </summary>
public static class MyLibrary
{
    /// <summary>True for the kinds your library holds (D-341).</summary>
    public static bool Holds(TemplateBase template) => template is AgentProfileTemplate or DeviceTemplate or JourneyTemplate or GroupTemplate;

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
    /// profiles, groups (and their members) and journeys, profiles' and groups' default journeys, placed devices'
    /// definitions — so the saved file and the
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
                    if (line.GroupId is { } groupId)
                    {
                        Embed(study, mine, groupId, fromMineOnly: true, added);
                    }

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

        foreach (GroupTemplate group in (study.Library.Groups ?? []).ToList())
        {
            if (group is null)
            {
                continue;
            }

            // A group the study holds may list members or a journey that are only in your library (round 024b).
            foreach (GroupMember member in group.Rows())
            {
                Embed(study, mine, member.ProfileId, fromMineOnly: true, added);
            }

            if (group.DefaultJourneyId is { } journeyId)
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
    /// plus a profile's default journey (D-350) unless that's an unchanged built-in (built-ins are everywhere already);
    /// a group (round 024b, D-388) brings its member profiles, their default journeys and its own, on the same terms.
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

        var taken = new HashSet<Guid>();

        void TakeOnce(TemplateBase source)
        {
            if (taken.Add(source.Id))
            {
                Take(source);
            }
        }

        void TakeJourney(Guid? journeyId)
        {
            if (journeyId is { } id && StudyTemplates.FindJourney(study, id, mine) is { } journey && !IsPristineBuiltIn(journey))
            {
                TakeOnce(journey);
            }
        }

        TakeOnce(template);
        if (template is AgentProfileTemplate profile)
        {
            TakeJourney(profile.DefaultJourneyId);
        }

        if (template is GroupTemplate group)
        {
            foreach (GroupMember member in group.Rows())
            {
                if (StudyTemplates.FindProfile(study, member.ProfileId, mine) is { } memberProfile && !IsPristineBuiltIn(memberProfile))
                {
                    TakeOnce(memberProfile);
                    TakeJourney(memberProfile.DefaultJourneyId);
                }
            }

            TakeJourney(group.DefaultJourneyId);
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
            case GroupTemplate group:
                PutIn(mine.Groups ??= [], group);
                break;
        }
    }

    /// <summary>Removes the item with this id from your library (D-345); false when it isn't there.</summary>
    public static bool Remove(TemplateLibrary mine, Guid id) =>
        mine.AgentProfiles.RemoveAll(t => t?.Id == id)
        + mine.Devices.RemoveAll(t => t?.Id == id)
        + (mine.Journeys?.RemoveAll(t => t?.Id == id) ?? 0)
        + (mine.Groups?.RemoveAll(t => t?.Id == id) ?? 0) > 0;

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
    /// journey when your library holds it (so the profile works where it's imported); a ticked group (round 024b) brings
    /// the member profiles and journeys your library holds the same way.
    /// </summary>
    public static TemplateLibrary BuildExport(TemplateLibrary mine, IEnumerable<Guid> ids)
    {
        var export = new TemplateLibrary();
        var wanted = new List<Guid>(ids);

        void Want(Guid? id)
        {
            if (id is { } value && Find(mine, value) is not null && !wanted.Contains(value))
            {
                wanted.Add(value);
            }
        }

        // Groups first add their members, then every profile (ticked or brought) adds its journey.
        foreach (Guid id in wanted.ToList())
        {
            if (Find(mine, id) is GroupTemplate group)
            {
                foreach (GroupMember member in group.Rows())
                {
                    Want(member.ProfileId);
                }

                Want(group.DefaultJourneyId);
            }
        }

        foreach (Guid id in wanted.ToList())
        {
            if (Find(mine, id) is AgentProfileTemplate { DefaultJourneyId: { } journeyId } && Find(mine, journeyId) is JourneyTemplate)
            {
                Want(journeyId);
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
    /// dialog lists them (profiles, devices, journeys, groups): one <see cref="ReconcileKind.LibraryItem"/> each (D-347).
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
    /// default journey came in as such a copy follows it, and so does a group whose members or journey did (round 024b).
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

        // Journeys first, so profiles can follow a journey that came in as a copy; groups last, to follow both.
        IEnumerable<TemplateBase> ordered = LibraryItems(incoming).OfType<JourneyTemplate>().Cast<TemplateBase>()
            .Concat(LibraryItems(incoming).Where(t => t is not JourneyTemplate and not GroupTemplate))
            .Concat(LibraryItems(incoming).OfType<GroupTemplate>());
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

            if (item is GroupTemplate group)
            {
                group.Members = [.. group.Rows().Select(m => newIds.TryGetValue(m.ProfileId, out Guid to) ? m with { ProfileId = to } : m)];
                if (group.DefaultJourneyId is { } groupJourney && newIds.TryGetValue(groupJourney, out Guid movedJourney))
                {
                    group.DefaultJourneyId = movedJourney;
                }
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

    /// <summary>"Agent profile", "Device", "Journey" or "Group", for dialogs.</summary>
    public static string KindName(TemplateBase template) => template switch
    {
        AgentProfileTemplate => "Agent profile",
        DeviceTemplate => "Device",
        JourneyTemplate => "Journey",
        GroupTemplate => "Group",
        _ => "Item",
    };

    /// <summary>True for a built-in (journey or profile), or the study's unchanged copy of one.</summary>
    private static bool IsPristineBuiltIn(TemplateBase template) =>
        BuiltInLibrary.Instance.Find(template.Id) is { } builtIn && StudyDefinitions.SameValues(template, builtIn);

    /// <summary>The library kinds of <paramref name="library"/>, in dialog order: profiles, devices, journeys, groups.</summary>
    private static IEnumerable<TemplateBase> LibraryItems(TemplateLibrary library) =>
        library.AgentProfiles.Where(t => t is not null).Cast<TemplateBase>()
            .Concat(library.Devices.Where(t => t is not null))
            .Concat((library.Journeys ?? []).Where(t => t is not null))
            .Concat((library.Groups ?? []).Where(t => t is not null));

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

        if (source is GroupTemplate)
        {
            study.Library.Groups ??= [];
        }

        added.Add(study.Library.AddCopy(source, TemplateSource.Embedded));

        // A group brings its member profiles (round 024), from the same place a profile would come from.
        if (source is GroupTemplate group)
        {
            foreach (GroupMember member in group.Rows())
            {
                Embed(study, mine, member.ProfileId, fromMineOnly, added);
            }
        }

        // A profile or group from your library brings its default journey from there too (D-350), so it works as saved.
        if (fromMine is AgentProfileTemplate { DefaultJourneyId: { } journeyId })
        {
            Embed(study, mine, journeyId, fromMineOnly: true, added);
        }

        if (fromMine is GroupTemplate { DefaultJourneyId: { } groupJourneyId })
        {
            Embed(study, mine, groupJourneyId, fromMineOnly: true, added);
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
            GroupTemplate => "group",
            _ => "journey",
        };
        Guid id = Guid.NewGuid();
        item.Id = id;
        item.Key = $"{prefix}.mine.{id:N}";

        IEnumerable<string> names = item switch
        {
            AgentProfileTemplate => mine.AgentProfiles.Select(t => t.Name),
            DeviceTemplate => mine.Devices.Select(t => t.Name),
            GroupTemplate => (mine.Groups ?? []).Select(t => t.Name),
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
