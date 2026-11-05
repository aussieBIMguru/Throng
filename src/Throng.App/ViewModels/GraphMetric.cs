namespace Throng.App.ViewModels;

/// <summary>What the RESULTS graph plots (round 011c, D-221); one at a time, since their units differ.</summary>
public enum GraphMetric
{
    /// <summary>People in the model.</summary>
    People,

    /// <summary>Queue length per device (ticked rows, else the five longest, D-226).</summary>
    Queues,

    /// <summary>Peak density anywhere, over the walkway Fruin bands.</summary>
    Density,

    /// <summary>People arriving and leaving per minute.</summary>
    Flow,

    /// <summary>People in each measurement zone (ticked rows, else all; round 012, D-232).</summary>
    Zones,
}
