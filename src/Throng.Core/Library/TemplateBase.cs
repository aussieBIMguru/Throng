namespace Throng.Core.Library;

/// <summary>Where a template came from.</summary>
public enum TemplateSource
{
    /// <summary>Shipped with the app; read-only.</summary>
    BuiltIn,

    /// <summary>Created or duplicated by the user.</summary>
    User,

    /// <summary>Imported from a .thronglib or .throngprofile file.</summary>
    Imported,

    /// <summary>A copy embedded in a study's library.json.</summary>
    Embedded,
}

/// <summary>
/// Common fields for every template type. A placed object copies a template's values and records
/// its <see cref="Id"/> and <see cref="Version"/> so it can later be reset or updated (§22.6).
/// </summary>
public abstract class TemplateBase
{
    /// <summary>Stable identifier. Built-ins derive it from <see cref="Key"/> (see <see cref="TemplateIds"/>).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short stable key, e.g. "agent.commuter". Unique within a library.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Library browser category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>What the template represents and how its defaults were chosen.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Incremented whenever the template's values change.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Origin of this template.</summary>
    public TemplateSource Source { get; set; } = TemplateSource.User;

    /// <summary>Display colour as #RRGGBB.</summary>
    public string ColorHex { get; set; } = "#FFFFFF";

    /// <summary>One-line summary of the key values, shown in the library browser.</summary>
    public abstract string Summarise();
}
