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
}
