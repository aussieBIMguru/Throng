using System.Runtime.InteropServices;

namespace Throng.Sim.Replay;

/// <summary>
/// One person in a replay frame (round 023, D-373), packed to 23 bytes: the serial to match frames, plan position,
/// height and heading at half precision, stress and the stuck flag as bytes, the profile colour as an index into the
/// recording's palette, body radius (cm) and height (2 cm steps) as bytes, and the group hue (round 024, D-391; 0 for
/// people on their own).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PackedAgent
{
    public int Serial;
    public float X;
    public float Y;
    public Half Z;
    public Half Heading;
    public byte Stress;
    public byte Stuck;
    public ushort Colour;
    public byte RadiusCm;
    public byte Height2Cm;
    public byte GroupHue;
}
