using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Throng.Core.Common;
using Throng.Render.Scene;

namespace Throng.App.Services;

/// <summary>
/// Decodes underlay images (PNG/JPEG) to 32-bit BGRA pixels for the renderer, once per image buffer (D-083).
/// Keyed by the byte array instance, so a replaced image decodes again and a dropped one is collected.
/// </summary>
public sealed class UnderlayImageCache
{
    /// <summary>Largest side accepted, in pixels (bigger images are refused with a message).</summary>
    public const int MaxSidePx = 8192;

    private readonly ConditionalWeakTable<byte[], UnderlayPixels> _cache = new();

    /// <summary>Decoded pixels for an image buffer, or null when it can't be decoded (logged once).</summary>
    public UnderlayPixels? Get(byte[] bytes)
    {
        if (_cache.TryGetValue(bytes, out UnderlayPixels? pixels))
        {
            return pixels;
        }

        Result<UnderlayPixels> decoded = Decode(bytes);
        if (!decoded.IsSuccess || decoded.Value is null)
        {
            AppLog.Warn(decoded.Error ?? "Underlay image couldn't be decoded.");
            return null;
        }

        _cache.AddOrUpdate(bytes, decoded.Value);
        return decoded.Value;
    }

    /// <summary>Remembers pixels already decoded for a buffer (on import).</summary>
    public void Put(byte[] bytes, UnderlayPixels pixels) => _cache.AddOrUpdate(bytes, pixels);

    /// <summary>Decodes PNG or JPEG bytes to BGRA. Never throws.</summary>
    public static Result<UnderlayPixels> Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            if (frame.PixelWidth > MaxSidePx || frame.PixelHeight > MaxSidePx)
            {
                return Result<UnderlayPixels>.Fail($"The image is {frame.PixelWidth} × {frame.PixelHeight} px; underlays can be at most {MaxSidePx} px on a side.");
            }

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return Result<UnderlayPixels>.Ok(new UnderlayPixels(converted.PixelWidth, converted.PixelHeight, pixels));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Result<UnderlayPixels>.Fail($"The image couldn't be read: {ex.Message}");
        }
    }
}
