namespace Throng.App.Views;

/// <summary>The run overlay's text and weight for one node output (round 019, D-332).</summary>
/// <param name="Text">Label, e.g. "124 · 38%" (actual) or "38%" (predicted).</param>
/// <param name="Share">Share of the journey's people out of this output, 0–1 (the wire's thickness).</param>
public readonly record struct JourneyWireLabel(string Text, double Share);
