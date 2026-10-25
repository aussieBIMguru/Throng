using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Resolves the templates a study's objects refer to: the study's embedded library first, then the built-in
/// catalogue (§22.6). Used by validation and by the simulation set-up.
/// </summary>
public static class StudyTemplates
{
    /// <summary>Key of the profile used when a rule's profile can't be found (D-099).</summary>
    public const string FallbackProfileKey = "agent.commuter";

    /// <summary>The agent profile with this id, or null when neither library has it.</summary>
    public static AgentProfileTemplate? FindProfile(Study study, Guid id) =>
        study.Library.Find(id) as AgentProfileTemplate ?? BuiltInLibrary.Instance.Find(id) as AgentProfileTemplate;

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

    /// <summary>All agent profiles a study can use: its embedded ones, then built-ins not already embedded.</summary>
    public static IReadOnlyList<AgentProfileTemplate> AvailableProfiles(Study study)
    {
        var profiles = new List<AgentProfileTemplate>(study.Library.AgentProfiles);
        var seen = profiles.Select(p => p.Id).ToHashSet();
        profiles.AddRange(BuiltInLibrary.Instance.AgentProfiles.Where(p => !seen.Contains(p.Id)));
        return profiles;
    }
}
