namespace Throng.App.ViewModels;

/// <summary>Which table the RESULTS tab shows (round 012, D-232).</summary>
public enum ResultsTable
{
    /// <summary>One row per device (round 011c).</summary>
    Devices,

    /// <summary>One row per measurement zone (D-228).</summary>
    Zones,

    /// <summary>Profile × journey rows (D-229).</summary>
    Journeys,

    /// <summary>One row per lift bank (round 015, D-260).</summary>
    Lifts,
}
