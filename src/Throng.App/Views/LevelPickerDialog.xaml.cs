using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Throng.App.ViewModels;

namespace Throng.App.Views;

/// <summary>
/// Themed modal checklist of levels (Edit ▸ Copy to Levels…, D-060). Use <see cref="Show"/>.
/// </summary>
public partial class LevelPickerDialog : Window
{
    private readonly List<(CheckBox Box, Guid Id)> _boxes = [];
    private bool _accepted;

    private LevelPickerDialog() => InitializeComponent();

    /// <summary>
    /// Shows the dialog modally. Returns the ticked level ids in display order, or null when cancelled. COPY stays
    /// disabled until at least one level is ticked.
    /// </summary>
    /// <param name="owner">Owner window for centring and modality; may be null.</param>
    /// <param name="title">Title strip text.</param>
    /// <param name="message">Explanation above the list.</param>
    /// <param name="levels">Levels to offer (or any items: the .thronglib export reuses it, round 020).</param>
    /// <param name="okText">Label of the confirming button (COPY by default).</param>
    public static IReadOnlyList<Guid>? Show(Window? owner, string title, string message, IReadOnlyList<LevelOption> levels, string? okText = null)
    {
        var dialog = new LevelPickerDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(okText))
        {
            dialog.OkButton.Content = okText;
        }

        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = title.ToUpperInvariant();
        dialog.MessageText.Text = message;
        foreach (LevelOption level in levels)
        {
            var box = new CheckBox { Content = level.Name, Margin = new Thickness(0, 3, 0, 3) };
            box.Checked += (_, _) => dialog.UpdateOk();
            box.Unchecked += (_, _) => dialog.UpdateOk();
            dialog._boxes.Add((box, level.Id));
            dialog.LevelList.Children.Add(box);
        }

        dialog.AllButton.Click += (_, _) => dialog.SetAll(true);
        dialog.NoneButton.Click += (_, _) => dialog.SetAll(false);
        dialog.OkButton.Click += (_, _) =>
        {
            dialog._accepted = true;
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

        dialog.UpdateOk();
        dialog.ShowDialog();
        return dialog._accepted ? [.. dialog._boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id)] : null;
    }

    private void SetAll(bool on)
    {
        foreach ((CheckBox box, _) in _boxes)
        {
            box.IsChecked = on;
        }

        UpdateOk();
    }

    private void UpdateOk() => OkButton.IsEnabled = _boxes.Any(b => b.Box.IsChecked == true);
}
