using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Throng.Core.Library;

namespace Throng.App.Views;

/// <summary>
/// Themed modal reconcile list for profile import (BRIEF §8.6, round 016, D-275): for each reference the study lacks,
/// keep it, map it to one the study has, or drop the step / preference. Use <see cref="Show"/>.
/// </summary>
public partial class ReconcileDialog : Window
{
    private readonly List<(ReconcileItem Item, ComboBox Box)> _rows = [];
    private bool _accepted;

    private ReconcileDialog() => InitializeComponent();

    /// <summary>Shows the dialog; one choice per item in order, or null when cancelled.</summary>
    public static IReadOnlyList<ReconcileChoice>? Show(Window? owner, string profileName, IReadOnlyList<ReconcileItem> items)
    {
        var dialog = new ReconcileDialog { Title = "Import profile" };
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = "IMPORT PROFILE";
        dialog.MessageText.Text =
            $"'{profileName}' and its journeys refer to things this study doesn't have. For each one, keep it (it works once the study has one), use one the study has, or drop the step.";
        dialog.Rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dialog.Rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        for (int k = 0; k < items.Count; k++)
        {
            ReconcileItem item = items[k];
            dialog.Rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            label.Inlines.Add(new System.Windows.Documents.Run($"{KindName(item.Kind)} '{item.Value}'") { FontWeight = FontWeights.SemiBold });
            label.Inlines.Add(new System.Windows.Documents.LineBreak());
            label.Inlines.Add(new System.Windows.Documents.Run(item.UsedBy) { Foreground = (System.Windows.Media.Brush)dialog.FindResource("TextDimBrush") });
            Grid.SetRow(label, k);

            var box = new ComboBox { Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            box.Items.Add($"Keep '{item.Value}'");
            box.Items.Add(item.Kind == ReconcileKind.Tag ? "Drop the tag" : "Drop the step");
            foreach (string option in item.MapOptions)
            {
                box.Items.Add($"Use '{option}'");
            }

            // Default: use the only alternative when there's exactly one, else keep.
            box.SelectedIndex = item.MapOptions.Count == 1 ? 2 : 0;
            Grid.SetRow(box, k);
            Grid.SetColumn(box, 1);
            dialog.Rows.Children.Add(label);
            dialog.Rows.Children.Add(box);
            dialog._rows.Add((item, box));
        }

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

        dialog.ShowDialog();
        if (!dialog._accepted)
        {
            return null;
        }

        return [.. dialog._rows.Select(r => r.Box.SelectedIndex switch
        {
            1 => new ReconcileChoice(ReconcileAction.Drop),
            >= 2 when r.Box.SelectedIndex - 2 < r.Item.MapOptions.Count => new ReconcileChoice(ReconcileAction.Map, r.Item.MapOptions[r.Box.SelectedIndex - 2]),
            _ => ReconcileChoice.Keep,
        })];
    }

    private static string KindName(ReconcileKind kind) => kind switch
    {
        ReconcileKind.DeviceType => "Device type",
        ReconcileKind.ZoneTag => "Zone tag",
        ReconcileKind.TrainTag => "Train tag",
        _ => "Person tag",
    };
}
