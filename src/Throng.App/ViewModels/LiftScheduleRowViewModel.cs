using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>One row of a lift's dispatch schedule in SELECTION (round 013, D-238): from a time of day, a mode.</summary>
public sealed class LiftScheduleRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<LabelledOption<LiftDispatch>> Choices =
        [new(LiftDispatch.Collective, "Collective"), new(LiftDispatch.UpPeak, "Up-peak"), new(LiftDispatch.DestinationDispatch, "Destination")];

    private readonly Action<TimeSpan, LiftDispatch> _set;
    private readonly MainViewModel _owner;

    /// <summary>Creates a row over a schedule entry.</summary>
    /// <param name="entry">The entry (read only; edits go through <paramref name="set"/>).</param>
    /// <param name="set">Writes the row (undoable).</param>
    /// <param name="remove">Removes the row (undoable).</param>
    /// <param name="owner">For ticker messages.</param>
    public LiftScheduleRowViewModel(LiftScheduleEntry entry, Action<TimeSpan, LiftDispatch> set, Action remove, MainViewModel owner)
    {
        From = entry.From;
        Dispatch = entry.Dispatch is LiftDispatch.UpPeak or LiftDispatch.DestinationDispatch ? entry.Dispatch : LiftDispatch.Collective;
        _set = set;
        _owner = owner;
        RemoveCommand = new RelayCommand(remove);
    }

    /// <summary>Start time of the row.</summary>
    public TimeSpan From { get; }

    /// <summary>Mode choices (Shuttle is a lift's fixed mode, so it isn't offered here).</summary>
    public IReadOnlyList<LabelledOption<LiftDispatch>> Options => Choices;

    /// <summary>Start time as HH:mm (24 h).</summary>
    public string FromText
    {
        get => From.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        set
        {
            if (!TimeSpan.TryParseExact((value ?? string.Empty).Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out TimeSpan from) || from >= TimeSpan.FromDays(1))
            {
                _owner.Ticker = "Times are 24 h, e.g. 07:30.";
                OnPropertyChanged();
                return;
            }

            _set(from, Dispatch);
        }
    }

    /// <summary>Mode from that time on.</summary>
    public LiftDispatch Dispatch { get; private set; }

    /// <summary>Mode, bound to the combo.</summary>
    public LiftDispatch DispatchChoice
    {
        get => Dispatch;
        set
        {
            if (value != Dispatch)
            {
                _set(From, value);
            }
        }
    }

    /// <summary>Removes the row.</summary>
    public RelayCommand RemoveCommand { get; }
}
