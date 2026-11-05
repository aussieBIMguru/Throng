using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Throng.App.Services;

/// <summary>
/// Swaps the "RetroFont" resource to the embedded VT323 font when it has been added to Assets\Fonts.
/// The theme's default (Consolas) stays in place otherwise.
/// </summary>
public static class RetroFont
{
    /// <summary>Resource key used by the theme and views.</summary>
    public const string ResourceKey = "RetroFont";

    private const string FontFile = "pack://application:,,,/Assets/Fonts/VT323-Regular.ttf";

    /// <summary>Applies the font to the given resource dictionary if it is available.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        try
        {
            // GetResourceStream throws IOException when the resource isn't embedded.
            using Stream? stream = Application.GetResourceStream(new Uri(FontFile))?.Stream;
            if (stream is null)
            {
                return;
            }

            resources[ResourceKey] = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#VT323, Consolas");
            AppLog.Info("VT323 retro font loaded.");
        }
        catch (IOException)
        {
            AppLog.Info("VT323 not embedded; using Consolas. See Assets/Fonts/README.md.");
        }
    }
}
