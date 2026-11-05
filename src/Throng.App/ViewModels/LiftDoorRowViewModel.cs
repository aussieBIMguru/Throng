using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>One served level's lift doors in SELECTION (round 013, D-237): front and rear use.</summary>
public sealed class LiftDoorRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<LabelledOption<LiftDoorUse>> Choices =
        [new(LiftDoorUse.Both, "IN + OUT"), new(LiftDoorUse.Entry, "IN"), new(LiftDoorUse.Exit, "OUT"), new(LiftDoorUse.Closed, "—")];

    private readonly Func<LiftLevelDoors> _get;
    private readonly Action<LiftDoorUse, LiftDoorUse> _set;

    /// <summary>Creates a row.</summary>
    /// <param name="levelName">Level name.</param>
    /// <param name="hasRear">True when the shaft has a rear edge.</param>
    /// <param name="get">Reads the level's doors.</param>
    /// <param name="set">Writes front and rear (undoable).</param>
    public LiftDoorRowViewModel(string levelName, bool hasRear, Func<LiftLevelDoors> get, Action<LiftDoorUse, LiftDoorUse> set)
    {
        LevelName = levelName;
        HasRear = hasRear;
        _get = get;
        _set = set;
    }

    /// <summary>Level name.</summary>
    public string LevelName { get; }

    /// <summary>True when a rear door is possible.</summary>
    public bool HasRear { get; }

    /// <summary>Door use choices.</summary>
    public IReadOnlyList<LabelledOption<LiftDoorUse>> Options => Choices;

    /// <summary>Front door use.</summary>
    public LiftDoorUse Front
    {
        get => _get().Front;
        set
        {
            if (value != Front)
            {
                _set(value, Rear);
            }
        }
    }

    /// <summary>Rear door use.</summary>
    public LiftDoorUse Rear
    {
        get => _get().Rear;
        set
        {
            if (value != Rear)
            {
                _set(Front, value);
            }
        }
    }
}
