using System.IO;
using PdfSharp.Fonts;

namespace Throng.App.Services;

/// <summary>
/// Fonts for the PDF report (round 025 fix): PDFsharp's Core build finds no fonts by itself, and its built-in Windows
/// resolver knows only a few families (not Consolas). This one reads the report's two families straight from the
/// Windows fonts folder: Arial for text and Consolas for numbers, each in regular, bold, italic and bold italic. Any
/// other family, or a missing file, falls back to Arial (then to Segoe UI), so the report never fails on a font.
/// </summary>
public sealed class ReportFontResolver : IFontResolver
{
    /// <summary>Family of the report's text.</summary>
    public const string Text = "Arial";

    /// <summary>Family of the report's numbers.</summary>
    public const string Mono = "Consolas";

    // Face name → file names to try in the fonts folder, first found wins.
    private static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial#R"] = ["arial.ttf", "segoeui.ttf"],
        ["Arial#B"] = ["arialbd.ttf", "segoeuib.ttf", "arial.ttf"],
        ["Arial#I"] = ["ariali.ttf", "segoeuii.ttf", "arial.ttf"],
        ["Arial#BI"] = ["arialbi.ttf", "segoeuiz.ttf", "arialbd.ttf", "arial.ttf"],
        ["Consolas#R"] = ["consola.ttf", "cour.ttf", "arial.ttf"],
        ["Consolas#B"] = ["consolab.ttf", "courbd.ttf", "arialbd.ttf", "arial.ttf"],
        ["Consolas#I"] = ["consolai.ttf", "couri.ttf", "ariali.ttf", "arial.ttf"],
        ["Consolas#BI"] = ["consolaz.ttf", "courbi.ttf", "arialbi.ttf", "arial.ttf"],
    };

    private readonly Dictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        string family = string.Equals(familyName, Mono, StringComparison.OrdinalIgnoreCase) ? Mono : Text;
        string style = isBold && isItalic ? "BI" : isBold ? "B" : isItalic ? "I" : "R";
        return new FontResolverInfo($"{family}#{style}");
    }

    /// <inheritdoc />
    public byte[]? GetFont(string faceName)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(faceName, out byte[]? cached))
            {
                return cached;
            }

            byte[]? data = Load(faceName) ?? Load("Arial#R");
            _cache[faceName] = data;
            return data;
        }
    }

    private static byte[]? Load(string faceName)
    {
        if (!Files.TryGetValue(faceName, out string[]? names))
        {
            return null;
        }

        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (string name in names)
        {
            string path = Path.Combine(folder, name);
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next file.
            }
        }

        return null;
    }
}
