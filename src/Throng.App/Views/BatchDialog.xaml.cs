using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Throng.App.Services;
using Throng.Sim.Batch;

namespace Throng.App.Views;

/// <summary>
/// Themed modal BATCH dialog (round 021, D-354, D-356, D-361, D-363): runs (default 5, 2–20), the seeds they'll use and
/// how many run at once, and minutes per run when the study runs until stopped. Use <see cref="Show"/>.
/// </summary>
public partial class BatchDialog : Window
{
    private int _baseSeed;
    private double? _fixedMinutes;
    private BatchRequest? _request;

    private BatchDialog() => InitializeComponent();

    /// <summary>Shows the dialog modally; what to run, or null when cancelled.</summary>
    /// <param name="owner">Owner window for centring and modality; may be null.</param>
    /// <param name="baseSeed">The study's seed (the first run's).</param>
    /// <param name="fixedDurationMinutes">The study's duration, or null when it runs until stopped (asked here, D-361).</param>
    public static BatchRequest? Show(Window? owner, int baseSeed, double? fixedDurationMinutes)
    {
        var dialog = new BatchDialog { Title = "Batch runs", _baseSeed = baseSeed, _fixedMinutes = fixedDurationMinutes };
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.RunsBox.Text = BatchOptions.DefaultRuns.ToString(CultureInfo.InvariantCulture);
        dialog.DurationPanel.Visibility = fixedDurationMinutes is null ? Visibility.Visible : Visibility.Collapsed;
        dialog.DurationBox.Text = "60";
        dialog.RunsBox.TextChanged += (_, _) => dialog.Refresh();
        dialog.DurationBox.TextChanged += (_, _) => dialog.Refresh();
        dialog.Loaded += (_, _) =>
        {
            dialog.RunsBox.Focus();
            dialog.RunsBox.SelectAll();
        };
        dialog.StartButton.Click += (_, _) =>
        {
            if (dialog.TryRead(out BatchRequest? request, out _))
            {
                dialog._request = request;
                dialog.Close();
            }
        };
        dialog.CancelButton.Click += (_, _) => dialog.Close();
        dialog.TitleStrip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                dialog.DragMove();
            }
        };

        dialog.Refresh();
        dialog.ShowDialog();
        return dialog._request;
    }

    /// <summary>Updates the seeds line and the problem line from what's typed.</summary>
    private void Refresh()
    {
        bool ok = TryRead(out BatchRequest? request, out string problem);
        StartButton.IsEnabled = ok;
        ProblemText.Text = problem;
        ProblemText.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (request is not null)
        {
            var options = new BatchOptions(_baseSeed, request.Runs, request.DurationMinutes * 60);
            CultureInfo c = CultureInfo.InvariantCulture;
            int atOnce = options.EffectiveParallelism;
            SeedsText.Text = string.Create(c,
                $"SEEDS {options.SeedOf(0)}–{options.SeedOf(request.Runs - 1)} · {request.DurationMinutes:0.#} MIN EACH · {atOnce} AT ONCE ({Environment.ProcessorCount} CORES)");
        }
    }

    /// <summary>Reads and checks the boxes.</summary>
    private bool TryRead(out BatchRequest? request, out string problem)
    {
        request = null;
        problem = string.Empty;
        CultureInfo c = CultureInfo.InvariantCulture;
        if (!int.TryParse(RunsBox.Text.Trim(), NumberStyles.Integer, c, out int runs) || runs < BatchOptions.MinRuns || runs > BatchOptions.MaxRuns)
        {
            problem = string.Create(c, $"Runs must be a whole number from {BatchOptions.MinRuns} to {BatchOptions.MaxRuns}.");
            return false;
        }

        double minutes;
        if (_fixedMinutes is { } fixedMinutes)
        {
            minutes = fixedMinutes;
        }
        else if (!double.TryParse(DurationBox.Text.Trim(), NumberStyles.Float, c, out minutes) || !(minutes >= 1) || minutes > 24 * 60)
        {
            problem = "Minutes per run must be from 1 to 1440.";
            return false;
        }

        request = new BatchRequest(runs, minutes);
        return true;
    }
}
