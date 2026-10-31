using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Resolves the templates a study's objects refer to: the study's embedded library first, then the built-in
/// catalogue (§22.6). Used by validation and by the simulation set-up. The app also passes your library (MINE,
/// round 020, D-343, D-346), looked up between the two; the simulation never does, because a MINE item is embedded in
/// the study when it's first used.
/// </summary>
public static class StudyTemplates
{
    /// <summary>Key of the profile used when a rule's profile can't be found (D-099).</summary>
    public const string FallbackProfileKey = "agent.commuter";

    /// <summary>The agent profile with this id (the study's, then <paramref name="mine"/>'s, then the built-in), or null.</summary>
    public static AgentProfileTemplate? FindProfile(Study study, Guid id, TemplateLibrary? mine = null) =>
        study.Library.Find(id) as AgentProfileTemplate ?? mine?.Find(id) as AgentProfileTemplate ?? BuiltInLibrary.Instance.Find(id) as AgentProfileTemplate;

    /// <summary>The profile with this id, or the built-in Commuter when it can't be found.</summary>
    public static AgentProfileTemplate ProfileOrFallback(Study study, Guid id) =>
        FindProfile(study, id)
        ?? (BuiltInLibrary.Instance.FindByKey(FallbackProfileKey) as AgentProfileTemplate)
        ?? new AgentProfileTemplate { Key = FallbackProfileKey, Name = "Commuter" };

    /// <summary>
    /// The preset a connector was placed from: by template id in the study library, then the built-ins, then the
    /// built-in with the same preset key. Null for connectors with neither.
    /// </summary>
    public static ConnectorTemplate? FindConnectorTemplate(Study study, Connector connector)
    {
        if (connector.TemplateId is { } id
            && (study.Library.Find(id) ?? BuiltInLibrary.Instance.Find(id)) is ConnectorTemplate byId)
        {
            return byId;
        }

        return string.IsNullOrEmpty(connector.PresetKey)
            ? null
            : study.Library.FindByKey(connector.PresetKey) as ConnectorTemplate ?? BuiltInLibrary.Instance.FindByKey(connector.PresetKey) as ConnectorTemplate;
    }

    /// <summary>The journey with this id (study library first, then <paramref name="mine"/>, then the built-ins), or null.</summary>
    public static JourneyTemplate? FindJourney(Study study, Guid id, TemplateLibrary? mine = null) =>
        study.Library.Find(id) as JourneyTemplate ?? mine?.Find(id) as JourneyTemplate ?? FindJourneyCore(study, id);

    /// <summary>
    /// The journey a mix line follows (D-304): the profile's default journey when the line says "(profile default)",
    /// else the line's own; null = straight to a sink (also when the id isn't in either library).
    /// </summary>
    public static JourneyTemplate? JourneyFor(Study study, MixLine line) =>
        JourneyIdFor(study, line) is { } id ? FindJourney(study, id) : null;

    /// <summary>The journey id a mix line follows (D-304), before looking it up; null = none.</summary>
    public static Guid? JourneyIdFor(Study study, MixLine line) =>
        line.UseProfileJourney ? ProfileOrFallback(study, line.ProfileId).DefaultJourneyId : line.JourneyId;

    private static JourneyTemplate? FindJourneyCore(Study study, Guid id) =>
        study.Library.Find(id) as JourneyTemplate ?? BuiltInLibrary.Instance.Find(id) as JourneyTemplate;

    /// <summary>
    /// All journeys a study can use: its embedded ones, then <paramref name="mine"/>'s not embedded (D-343), then
    /// built-ins not in either.
    /// </summary>
    public static IReadOnlyList<JourneyTemplate> AvailableJourneys(Study study, TemplateLibrary? mine = null) =>
        Layered(study.Library.Journeys ?? [], mine?.Journeys, BuiltInLibrary.Instance.Journeys);

    /// <summary>
    /// The study's items, then <paramref name="mine"/>'s with ids the study doesn't hold, then built-ins with ids neither
    /// holds (D-343): one row per id, the nearest copy winning.
    /// </summary>
    public static List<T> Layered<T>(IEnumerable<T> study, IEnumerable<T>? mine, IEnumerable<T> builtIns)
        where T : TemplateBase
    {
        var items = new List<T>(study.Where(t => t is not null));
        var seen = items.Select(t => t.Id).ToHashSet();
        foreach (T item in (mine ?? []).Concat(builtIns))
        {
            if (item is not null && seen.Add(item.Id))
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// True when a journey can be edited (D-161): it lives in the study's library and isn't a copy of a built-in
    /// (built-ins, and the copies embedded when a mix line uses one, keep the built-in's id and stay read-only).
    /// </summary>
    public static bool IsEditableJourney(Study study, JourneyTemplate journey) =>
        journey.Source != TemplateSource.BuiltIn
        && BuiltInLibrary.Instance.Find(journey.Id) is null
        && (study.Library.Journeys ?? []).Contains(journey);

    /// <summary>
    /// True when a journey can be edited here (D-328): the study's own and imported ones, and built-ins, whose first
    /// edit makes the study's copy under the built-in's id (an override, like devices and profiles, D-287).
    /// </summary>
    public static bool CanEditJourney(Study study, JourneyTemplate journey, TemplateLibrary? mine = null) =>
        (study.Library.Journeys ?? []).Any(j => j.Id == journey.Id) || BuiltInLibrary.Instance.Find(journey.Id) is JourneyTemplate
        || mine?.Find(journey.Id) is JourneyTemplate;

    /// <summary>
    /// An editable copy of a journey (D-161): new id and key, the same steps, a name not used by another journey
    /// ("Ticket then gates (copy)", "(copy 2)"…). Not added to the library; the caller does that through the undo
    /// stack.
    /// </summary>
    public static JourneyTemplate CreateJourneyCopy(Study study, JourneyTemplate source)
    {
        var names = AvailableJourneys(study).Select(j => j.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string baseName = $"{source.Name} (copy)";
        string name = baseName;
        for (int n = 2; names.Contains(name); n++)
        {
            name = $"{source.Name} (copy {n.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        }

        Guid id = Guid.NewGuid();
        return new JourneyTemplate
        {
            Id = id,
            Key = $"journey.user.{id:N}",
            Name = name,
            Category = source.Category,
            Description = string.IsNullOrWhiteSpace(source.Description) ? $"Copy of {source.Name}." : source.Description,
            ColorHex = source.ColorHex,
            Source = TemplateSource.User,
            Version = 1,
            Nodes = [.. (source.Nodes ?? []).Where(n => n is not null)],
            Wires = [.. (source.Wires ?? []).Where(w => w is not null)],
            Steps = source.Steps is { Count: > 0 } && source.Nodes is not { Count: > 0 } ? new List<JourneyStep>(source.Steps.Where(s => s is not null)) : null,
        };
    }

    /// <summary>
    /// The preset a device was placed from: by template id in the study library, then the built-ins, then the
    /// built-in with the same preset key. Null when none is found (the simulation then uses neutral defaults).
    /// </summary>
    public static DeviceTemplate? FindDeviceTemplate(Study study, Device device)
    {
        if (device.TemplateId is { } id
            && (study.Library.Find(id) ?? BuiltInLibrary.Instance.Find(id)) is DeviceTemplate byId)
        {
            return byId;
        }

        return string.IsNullOrEmpty(device.PresetKey)
            ? null
            : study.Library.FindByKey(device.PresetKey) as DeviceTemplate ?? BuiltInLibrary.Instance.FindByKey(device.PresetKey) as DeviceTemplate;
    }

    /// <summary>
    /// The preset a lift was made from (round 013): by template id in the study library, then the built-ins, then
    /// the built-in with the same preset key. Null when none is found (the simulation then uses the class defaults).
    /// </summary>
    public static LiftTemplate? FindLiftTemplate(Study study, Lift lift)
    {
        if (lift.TemplateId is { } id
            && (study.Library.Find(id) ?? BuiltInLibrary.Instance.Find(id)) is LiftTemplate byId)
        {
            return byId;
        }

        return string.IsNullOrEmpty(lift.PresetKey)
            ? null
            : study.Library.FindByKey(lift.PresetKey) as LiftTemplate ?? BuiltInLibrary.Instance.FindByKey(lift.PresetKey) as LiftTemplate;
    }

    /// <summary>All lift presets a study can use: its embedded ones, then built-ins not already embedded.</summary>
    public static IReadOnlyList<LiftTemplate> AvailableLifts(Study study)
    {
        var own = new List<LiftTemplate>(study.Library.Lifts);
        var keys = own.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        own.AddRange(BuiltInLibrary.Instance.Lifts.Where(t => !keys.Contains(t.Key)));
        return own;
    }

    /// <summary>
    /// Whether a placed device is accessible (D-223): its own override, else its preset's flag, else not.
    /// </summary>
    public static bool IsAccessible(Study study, Device device) =>
        device.AccessibleOverride ?? FindDeviceTemplate(study, device)?.Accessible ?? false;

    /// <summary>
    /// All agent profiles a study can use: its embedded ones, then <paramref name="mine"/>'s not embedded (D-343), then
    /// built-ins not in either.
    /// </summary>
    public static IReadOnlyList<AgentProfileTemplate> AvailableProfiles(Study study, TemplateLibrary? mine = null) =>
        Layered(study.Library.AgentProfiles, mine?.AgentProfiles, BuiltInLibrary.Instance.AgentProfiles);
}
