namespace Throng.IO;

/// <summary>
/// manifest.json inside a .throng file: identifies the format version so older files can be migrated (§14.2).
/// </summary>
public sealed class ThrongManifest
{
    /// <summary>Marker so a renamed zip isn't mistaken for a study.</summary>
    public string Format { get; set; } = ThrongFile.FormatName;

    /// <summary>File format version the content was written with.</summary>
    public int FormatVersion { get; set; } = ThrongFile.CurrentFormatVersion;

    /// <summary>App version that wrote the file.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>When the study was first saved (UTC).</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>When the study was last saved (UTC).</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>Length unit for all coordinates.</summary>
    public string Units { get; set; } = "metres";

    /// <summary>RNG seed, duplicated here for quick inspection.</summary>
    public int Seed { get; set; }
}
