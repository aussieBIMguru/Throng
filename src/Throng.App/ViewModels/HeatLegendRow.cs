using System.Windows.Media;

namespace Throng.App.ViewModels;

/// <summary>One row of the viewport heatmap legend (D-204): a colour swatch and its label.</summary>
/// <param name="Swatch">Frozen brush of the band or ramp colour.</param>
/// <param name="Label">Band letter or density value.</param>
public sealed record HeatLegendRow(Brush Swatch, string Label);
