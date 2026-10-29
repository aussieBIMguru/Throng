using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// One live heatmap layer to drape over a floor or a stair flight (D-202): a small BGRA texture, one texel per map
/// cell, stretched over the layer's four corners.
/// </summary>
/// <param name="Key">Stable id for the run (the renderer keeps one texture per key).</param>
/// <param name="Corners">Corners u0v0, u1v0, u1v1, u0v1 in 3D; texel row 0 runs along the u0v0–u1v0 edge.</param>
/// <param name="Width">Texels along u.</param>
/// <param name="Height">Texels along v.</param>
/// <param name="Bgra">Width × Height × 4 bytes.</param>
public sealed record HeatLayerDraw(int Key, IReadOnlyList<Point3> Corners, int Width, int Height, byte[] Bgra);
