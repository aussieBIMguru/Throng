namespace Throng.Core.Library;

/// <summary>What a profile import refers to that the study may not have (D-275).</summary>
public enum ReconcileKind
{
    /// <summary>A device type tag or preset key (journey device steps, device preferences).</summary>
    DeviceType,

    /// <summary>A zone tag (pass-zone and dwell steps).</summary>
    ZoneTag,

    /// <summary>A train service tag (board-train steps).</summary>
    TrainTag,

    /// <summary>A person tag a step needs or lacks, granted by nothing in the study.</summary>
    Tag,

    /// <summary>
    /// An imported device definition's own type tag that no journey step or placed device in the study uses (round 017,
    /// D-290): keep it as a new type, or use one the study has. Dropping keeps it too.
    /// </summary>
    ImportedDeviceType,

    /// <summary>
    /// A .thronglib item whose id your library already holds with different values (round 020, D-347): keep both
    /// (the incoming one as a copy), skip it, or replace yours.
    /// </summary>
    LibraryItem,
}
