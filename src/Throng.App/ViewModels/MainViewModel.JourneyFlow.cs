using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// The run overlay on the JOURNEY canvas (round 019, D-331–D-338): the latest results copy and the compiled model
/// the predicted shares come from (D-334), kept for the selected journey's view model. Actual counts live until RESET
/// or the next run (D-331); the prediction lives until the study changes.
/// </summary>
public sealed partial class MainViewModel
{
    private SimModel? _predictionModel;
    private string _predictionSource = string.Empty;

    /// <summary>The model the predicted shares come from (the last PLAY or CHECK), or null.</summary>
    internal SimModel? PredictionModel => _predictionModel;

    /// <summary>Where the prediction came from, for the strip: "PLAY" or "CHECK".</summary>
    internal string PredictionSource => _predictionSource;

    /// <summary>The results copy the overlay shows (empty after RESET).</summary>
    internal RunResults OverlayResults => _results;

    /// <summary>The runner whose results the overlay shows, or null.</summary>
    internal SimRunner? OverlayRunner => _resultsRunner;

    /// <summary>Strip text for the run (D-331).</summary>
    internal string OverlayRunState => _runner is not { } runner
        ? "NO RUN YET"
        : runner.IsPlaying
            ? "RUNNING"
            : runner.IsFinished ? "AFTER THE RUN" : "PAUSED";

    /// <summary>Keeps (or drops, with null) the model for predicted shares and redraws the overlay.</summary>
    private void SetPredictionModel(SimModel? model, string source)
    {
        if (ReferenceEquals(model, _predictionModel))
        {
            return;
        }

        _predictionModel = model;
        _predictionSource = model is null ? string.Empty : source;
        _selectedJourney?.RefreshOverlay();
    }

    /// <summary>Redraws the selected journey's overlay when anything it shows changed (results tick, run state).</summary>
    private void RefreshJourneyOverlay() => _selectedJourney?.RefreshOverlay();
}
