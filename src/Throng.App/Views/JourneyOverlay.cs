namespace Throng.App.Views;

/// <summary>
/// What the JOURNEY canvas draws over the graph for a run (round 019, D-331–D-335): a label and thickness per node
/// output, a stats line per node, and the strip's state (ACTUAL / PREDICTED, the profile filter). Built by the host;
/// immutable.
/// </summary>
public sealed class JourneyOverlay
{
    /// <summary>Nothing to show (no journey).</summary>
    public static JourneyOverlay Empty { get; } = new() { StateText = string.Empty };

    /// <summary>Strip text: "NO RUN YET", "RUNNING", "PAUSED", "AFTER THE RUN"; for PREDICTED, where the prediction came from.</summary>
    public required string StateText { get; init; }

    /// <summary>True when PREDICTED is lit (else ACTUAL).</summary>
    public bool IsPredicted { get; init; }

    /// <summary>True when a compiled model (PLAY or CHECK) gives predicted shares.</summary>
    public bool HasPrediction { get; init; }

    /// <summary>The profile filter's text, e.g. "All profiles ▾".</summary>
    public string ProfileText { get; init; } = "All profiles ▾";

    /// <summary>Per node output (from node id, port): label and share; outputs with nothing to say are left out.</summary>
    public IReadOnlyDictionary<(Guid From, int Port), JourneyWireLabel> Wires { get; init; } = new Dictionary<(Guid, int), JourneyWireLabel>();

    /// <summary>Per node id: a short stats line under the header, e.g. "312 in · 18 s mean / 41 s p95 · 6 gave up".</summary>
    public IReadOnlyDictionary<Guid, string> NodeLines { get; init; } = new Dictionary<Guid, string>();

    /// <summary>True when there is anything to draw on the graph.</summary>
    public bool HasData => Wires.Count > 0 || NodeLines.Count > 0;
}
