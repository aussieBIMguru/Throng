using Throng.Core.Model;

namespace Throng.Render.Scene;

/// <summary>
/// Viewport colours. Dark base with bright primaries for objects (§13). Mirrors the WPF theme in Throng.App.
/// </summary>
public static class RenderPalette
{
    public static readonly ColorF Background = ColorF.FromHex("#0B0F17");

    public static readonly ColorF FloorActive = ColorF.FromHex("#1B2636");
    public static readonly ColorF FloorInactive = ColorF.FromHex("#141C29");
    public static readonly ColorF FloorOutlineActive = ColorF.FromHex("#22D3EE");
    public static readonly ColorF FloorOutlineInactive = ColorF.FromHex("#3B4A63");

    public static readonly ColorF ObstacleSide = ColorF.FromHex("#3A4558");
    public static readonly ColorF ObstacleTop = ColorF.FromHex("#56627A");
    public static readonly ColorF ObstacleOutline = ColorF.FromHex("#8A9BB8");

    public static readonly ColorF VoidFill = ColorF.FromHex("#06080D");
    public static readonly ColorF VoidEdge = ColorF.FromHex("#FB923C");

    public static readonly ColorF WallSide = ColorF.FromHex("#4B5568");
    public static readonly ColorF WallTop = ColorF.FromHex("#9AA7C0");

    /// <summary>Net walkable boundary (union of floors minus obstacles and walls, D-039).</summary>
    public static readonly ColorF WalkableNet = ColorF.FromHex("#4ADE80", 0.85f);

    public static readonly ColorF GridMinor = ColorF.FromHex("#1A2433");
    public static readonly ColorF GridMajor = ColorF.FromHex("#2B3A52");

    /// <summary>Fill alpha for translucent zones.</summary>
    public const float ZoneFillAlpha = 0.28f;

    /// <summary>Opaque colour for a zone kind.</summary>
    public static ColorF Zone(ZoneKind kind) => kind switch
    {
        ZoneKind.Spawn => ColorF.FromHex("#4ADE80"),
        ZoneKind.Sink => ColorF.FromHex("#F87171"),
        ZoneKind.SpawnSink => ColorF.FromHex("#2DD4BF"),
        ZoneKind.Queue => ColorF.FromHex("#E879F9"),
        ZoneKind.Measurement => ColorF.FromHex("#22D3EE"),
        ZoneKind.Dwell => ColorF.FromHex("#FACC15"),
        ZoneKind.Wait => ColorF.FromHex("#C084FC"),
        ZoneKind.Slow => ColorF.FromHex("#FB923C"),
        _ => ColorF.FromHex("#94A3B8"),
    };
}
