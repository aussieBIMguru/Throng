using System.Text.Json;
using System.Text.Json.Serialization;
using Throng.Core.Serialization;

namespace Throng.Core.Library;

/// <summary>
/// A collection of templates, one typed list per template kind. The same shape is used for the built-in
/// catalogue, the user library, .thronglib exchange files and a study's embedded library.json.
/// </summary>
public sealed class TemplateLibrary
{
    /// <summary>Current library format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version of this library's JSON.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Agent profiles.</summary>
    public List<AgentProfileTemplate> AgentProfiles { get; set; } = [];

    /// <summary>Device (node) presets.</summary>
    public List<DeviceTemplate> Devices { get; set; } = [];

    /// <summary>Stairs, escalators, travelators, ramps.</summary>
    public List<ConnectorTemplate> Connectors { get; set; } = [];

    /// <summary>Lift presets.</summary>
    public List<LiftTemplate> Lifts { get; set; } = [];

    /// <summary>Schedule templates.</summary>
    public List<ScheduleTemplate> Schedules { get; set; } = [];

    /// <summary>Journeys (D-154). Libraries saved before round 009 have none.</summary>
    public List<JourneyTemplate> Journeys { get; set; } = [];

    /// <summary>Every template in the library, in category order.</summary>
    public IEnumerable<TemplateBase> All() =>
        AgentProfiles.Cast<TemplateBase>()
            .Concat(Devices)
            .Concat(Connectors)
            .Concat(Lifts)
            .Concat(Schedules)
            .Concat(Journeys ?? []);

    /// <summary>Total number of templates.</summary>
    [JsonIgnore]
    public int Count => AgentProfiles.Count + Devices.Count + Connectors.Count + Lifts.Count + Schedules.Count + (Journeys?.Count ?? 0);

    /// <summary>Finds a template by id.</summary>
    public TemplateBase? Find(Guid id) => All().FirstOrDefault(t => t.Id == id);

    /// <summary>Finds a template by key.</summary>
    public TemplateBase? FindByKey(string key) => All().FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>Adds a template to the matching list.</summary>
    public void Add(TemplateBase template)
    {
        switch (template)
        {
            case AgentProfileTemplate a: AgentProfiles.Add(a); break;
            case DeviceTemplate d: Devices.Add(d); break;
            case ConnectorTemplate c: Connectors.Add(c); break;
            case LiftTemplate l: Lifts.Add(l); break;
            case ScheduleTemplate s: Schedules.Add(s); break;
            case JourneyTemplate j: (Journeys ??= []).Add(j); break;
        }
    }

    /// <summary>
    /// Adds a deep copy of <paramref name="template"/> marked with <paramref name="source"/>, unless a template with
    /// the same id is already present. Returns the copy (or the existing entry).
    /// </summary>
    public TemplateBase AddCopy(TemplateBase template, TemplateSource source)
    {
        TemplateBase? existing = Find(template.Id);
        if (existing is not null)
        {
            return existing;
        }

        TemplateBase copy = TemplateCloner.Clone(template);
        copy.Source = source;
        Add(copy);
        return copy;
    }
}

/// <summary>Deep-copies templates via a JSON round trip (simple, and guaranteed to match the file format).</summary>
public static class TemplateCloner
{
    /// <summary>Returns a deep copy of a template of any concrete type.</summary>
    public static TemplateBase Clone(TemplateBase template)
    {
        Type type = template.GetType();
        string json = JsonSerializer.Serialize(template, type, JsonDefaults.Options);
        return (TemplateBase)JsonSerializer.Deserialize(json, type, JsonDefaults.Options)!;
    }
}
