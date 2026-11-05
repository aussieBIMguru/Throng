using System.Windows.Media;

namespace Throng.App.Services;

/// <summary>One legend row on an exported PNG: a colour swatch and its label.</summary>
/// <param name="Colour">Swatch colour.</param>
/// <param name="Label">Label beside it.</param>
public sealed record PngLegendRow(Color Colour, string Label);
