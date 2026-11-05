namespace Throng.Core.Model;

/// <summary>How a lift door is used at one level (D-068, D-237).</summary>
public enum LiftDoorUse
{
    /// <summary>No door at this level on this side.</summary>
    Closed,

    /// <summary>People get on and off here.</summary>
    Both,

    /// <summary>People only get on here.</summary>
    Entry,

    /// <summary>People only get off here.</summary>
    Exit,
}
