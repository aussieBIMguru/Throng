using System.Windows.Media;

namespace Throng.App.Views;

/// <summary>A horizontal band behind the graph's lines, e.g. a Fruin LoS grade (round 011c, D-221).</summary>
/// <param name="From">Lower value.</param>
/// <param name="To">Upper value (infinity runs to the top).</param>
/// <param name="Colour">Fill colour (drawn faint).</param>
/// <param name="Label">Short label at the right edge, e.g. "E".</param>
public sealed record GraphBand(double From, double To, Color Colour, string Label);
