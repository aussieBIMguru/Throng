using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Render.Gl;
using Throng.Sim;

namespace Throng.App.ViewModels;

/// <summary>
/// PNG export (round 012, D-234): FILE ▸ EXPORT PNG… and the RESULTS button capture the viewport at 2× its size and
/// add a legend and a title block (study, level, sim time, map, "Conceptual, not a compliance tool").
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Multiple of the viewport's on-screen size the PNG is made at (D-234).</summary>
    public const double PngScale = 2.0;

    private AsyncRelayCommand? _exportPngCommand;
    private Func<double, Task<Result<BitmapSource>>>? _captureViewport;

    /// <summary>Captures the viewport at a scale; set by the main window (the view owns the GL context).</summary>
    public Func<double, Task<Result<BitmapSource>>>? CaptureViewport
    {
        get => _captureViewport;
        set
        {
            _captureViewport = value;
            ExportPngCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>EXPORT PNG…: the view at 2× with legend and title block, to a file you pick (D-234).</summary>
    public AsyncRelayCommand ExportPngCommand => _exportPngCommand ??= new AsyncRelayCommand(ExportPngAsync, () => _captureViewport is not null);

    /// <summary>Asks for a file, captures the view, composes and writes it; problems are shown, never thrown.</summary>
    private async Task ExportPngAsync()
    {
        if (_captureViewport is not { } capture)
        {
            return;
        }

        string clock = RunClockText().Replace(":", string.Empty, StringComparison.Ordinal).Replace(" · ", "-", StringComparison.Ordinal).Replace(' ', '-');
        string suggested = SafeFileName(_study.Name + "-" + clock) + ".png";
        if (_dialogs.PickPngSavePath(suggested) is not { } path)
        {
            return;
        }

        // The info is read before the capture so the title block matches the frame (the run may be playing).
        PngExportInfo info = BuildPngInfo();
        Result<BitmapSource> view = await capture(PngScale);
        if (!view.IsSuccess || view.Value is null)
        {
            AppLog.Warn(view.Error ?? "PNG capture failed.");
            _dialogs.ShowError(view.Error ?? "The view couldn't be captured.");
            return;
        }

        Result written = PngExport.Write(view.Value, info, path);
        if (!written.IsSuccess)
        {
            AppLog.Warn(written.Error ?? "PNG export failed.");
            _dialogs.ShowError(written.Error ?? "The image couldn't be written.");
            return;
        }

        AppLog.Info($"PNG exported to {path}");
        Ticker = $"EXPORTED ▸ {System.IO.Path.GetFileName(path)} ({view.Value.PixelWidth} × {view.Value.PixelHeight})";
    }

    /// <summary>Title block and legend for the current view.</summary>
    private PngExportInfo BuildPngInfo()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string level = ViewMode switch
        {
            ViewMode.Plan => HudLevelText + " · PLAN",
            ViewMode.Iso => "ALL VISIBLE LEVELS · " + HudModeText,
            _ => "ALL VISIBLE LEVELS · 3D",
        };
        if (ActiveViewName is { } viewName)
        {
            // A saved view's name heads the title block (D-276).
            level = $"VIEW '{viewName}' · {level}";
        }
        string time = _isRunActive && _runner is not null
            ? string.Create(c, $"{RunClockText()} · {Clock(_stats.TimeS)} into the run")
            : RunClockText() + " · not running";

        if (ShowHeatLegend)
        {
            return new PngExportInfo
            {
                Study = _study.Name,
                Level = level,
                SimTime = time,
                Map = HeatLegendTitle,
                LegendTitle = HeatLegendTitle,
                Legend = [.. HeatLegend.Select(r => new PngLegendRow(r.Swatch is SolidColorBrush b ? b.Color : Colors.Gray, r.Label))],
                LegendNote = HeatLegendNote,
            };
        }

        string map = AgentColoring == AgentColoring.Stress ? "OFF · PEOPLE BY STRESS" : "OFF · PEOPLE BY PROFILE";
        List<PngLegendRow> legend = [];
        string title = string.Empty;
        if (_isRunActive && _runner is { } runner)
        {
            if (AgentColoring == AgentColoring.Stress)
            {
                // Same ramp as the agent shader (calm → tense → fed up, D-145).
                title = "STRESS";
                legend.Add(new PngLegendRow(Color.FromRgb(0x3B, 0x82, 0xF5), "Calm"));
                legend.Add(new PngLegendRow(Color.FromRgb(0xFA, 0xCC, 0x14), "Tense"));
                legend.Add(new PngLegendRow(Color.FromRgb(0xF0, 0x45, 0x45), "Fed up"));
            }
            else
            {
                title = "PROFILES";
                foreach (SimProfile profile in runner.Model.Profiles)
                {
                    legend.Add(new PngLegendRow(Color.FromRgb(ToByte(profile.R), ToByte(profile.G), ToByte(profile.B)), profile.Template.Name));
                }
            }
        }

        return new PngExportInfo { Study = _study.Name, Level = level, SimTime = time, Map = map, LegendTitle = title, Legend = legend };
    }

    /// <summary>The study name with characters Windows won't take in a file name replaced.</summary>
    private static string SafeFileName(string name)
    {
        char[] bad = System.IO.Path.GetInvalidFileNameChars();
        string source = string.IsNullOrWhiteSpace(name) ? "throng" : name.Trim();
        string cleaned = string.Concat(source.Select(ch => Array.IndexOf(bad, ch) >= 0 ? '_' : ch));
        return cleaned.Length > 0 ? cleaned : "throng";
    }
}
