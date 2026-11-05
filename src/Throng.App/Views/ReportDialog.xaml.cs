using System.Windows;
using System.Windows.Input;
using Throng.Analysis;
using Throng.App.ViewModels;

namespace Throng.App.Views;

/// <summary>
/// Themed modal for EXPORT PDF REPORT (round 025, D-403): pick the map every level plan shows. Use <see cref="Show"/>.
/// </summary>
public partial class ReportDialog : Window
{
    private bool _accepted;

    private ReportDialog() => InitializeComponent();

    /// <summary>Shows the dialog; the chosen map, or null when cancelled.</summary>
    /// <param name="owner">Owner window; may be null.</param>
    /// <param name="maps">The maps to offer, labelled.</param>
    /// <param name="current">The map to start on.</param>
    /// <param name="note">A line under the choice (what the report holds).</param>
    public static HeatmapMode? Show(Window? owner, IReadOnlyList<LabelledOption<HeatmapMode>> maps, HeatmapMode current, string note)
    {
        var dialog = new ReportDialog { Title = "Export PDF report" };
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = "EXPORT PDF REPORT";
        dialog.MessageText.Text = "Every level is drawn in plan with this map (A3 landscape, up to six plans on the first page).";
        dialog.NoteText.Text = note;
        dialog.OkButton.Content = "EXPORT…";
        dialog.MapBox.ItemsSource = maps;
        dialog.MapBox.SelectedItem = maps.FirstOrDefault(m => m.Value == current) ?? maps.FirstOrDefault();
        dialog.Loaded += (_, _) => dialog.MapBox.Focus();
        dialog.OkButton.Click += (_, _) =>
        {
            dialog._accepted = dialog.MapBox.SelectedItem is LabelledOption<HeatmapMode>;
            dialog.Close();
        };
        dialog.CancelButton.Click += (_, _) => dialog.Close();
        dialog.TitleStrip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                dialog.DragMove();
            }
        };

        dialog.ShowDialog();
        return dialog._accepted && dialog.MapBox.SelectedItem is LabelledOption<HeatmapMode> picked ? picked.Value : null;
    }
}
