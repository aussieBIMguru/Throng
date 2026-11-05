using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.Views;

/// <summary>
/// PERF overlay (round 025, D-380): frame and render milliseconds measured here, and the simulation's step timings by
/// phase from <see cref="PerfSource"/>, refreshed twice a second while shown (F8).
/// </summary>
public partial class ViewportView
{
    private const double PerfRefreshS = 0.5;
    private readonly Stopwatch _perfClock = Stopwatch.StartNew();
    private double _perfLastS;
    private double _frameSumMs;
    private double _renderSumMs;
    private double _lastFrameS;
    private int _perfFrames;

    /// <summary>
    /// The simulation's PERF window while the overlay is on (<see cref="PerfSample.Empty"/> without a run); null hides
    /// the overlay.
    /// </summary>
    public Func<PerfSample?>? PerfSource { get; set; }

    /// <summary>Starts timing a frame's render; pass the result to <see cref="EndRenderTiming"/>.</summary>
    private long BeginRenderTiming() => Stopwatch.GetTimestamp();

    /// <summary>Notes a frame and its render time, and refreshes the overlay when due.</summary>
    private void EndRenderTiming(long started)
    {
        double now = _perfClock.Elapsed.TotalSeconds;
        _renderSumMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        if (_lastFrameS > 0)
        {
            _frameSumMs += (now - _lastFrameS) * 1000.0;
            _perfFrames++;
        }

        _lastFrameS = now;
        if (now - _perfLastS < PerfRefreshS)
        {
            return;
        }

        PerfSample? sim = PerfSource?.Invoke();
        if (sim is null)
        {
            if (PerfText.Visibility != Visibility.Collapsed)
            {
                PerfText.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            PerfText.Text = PerfReport(sim);
            PerfText.Visibility = Visibility.Visible;
        }

        _perfLastS = now;
        _frameSumMs = _renderSumMs = 0;
        _perfFrames = 0;
    }

    /// <summary>The overlay's text: frames, then the step and each phase.</summary>
    private string PerfReport(PerfSample sim)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        int frames = Math.Max(1, _perfFrames);
        double frameMs = _frameSumMs / frames;
        var text = new StringBuilder();
        text.Append(c, $"PERF  {(frameMs > 0 ? 1000 / frameMs : 0),5:0} fps  frame {frameMs,5:0.0} ms  render {_renderSumMs / frames,5:0.0} ms");
        text.Append('\n').Append(c, $"SIM   {sim.People,6:N0} people  step {sim.StepMs,6:0.00} ms  ×{sim.SpeedUp:0.0} real time");
        for (int p = 0; p < sim.PhaseMs.Length && p < SimEngine.PerfPhases.Count; p++)
        {
            text.Append('\n').Append(c, $"  {SimEngine.PerfPhases[p],-16}{sim.PhaseMs[p],7:0.00} ms");
        }

        return text.ToString();
    }
}
