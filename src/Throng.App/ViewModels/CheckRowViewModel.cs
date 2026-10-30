using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Validation;

namespace Throng.App.ViewModels;

/// <summary>
/// One row of the CHECK tab (round 016, D-271, D-272): a finding with its severity and rule; clicking it goes to the
/// object (or place) it's about, and IGNORE / UNIGNORE keeps it out of the counts for this study.
/// </summary>
public sealed class CheckRowViewModel : ObservableObject
{
    private bool _isIgnored;

    /// <summary>Creates a row.</summary>
    public CheckRowViewModel(ValidationMessage finding, bool isIgnored, Action<CheckRowViewModel> go, Action<CheckRowViewModel> toggleIgnore)
    {
        Finding = finding;
        _isIgnored = isIgnored;
        Rule = ValidationRules.Find(finding.Rule);
        GoCommand = new RelayCommand(() => go(this));
        ToggleIgnoreCommand = new RelayCommand(() => toggleIgnore(this));
    }

    /// <summary>The finding.</summary>
    public ValidationMessage Finding { get; }

    /// <summary>The rule that found it.</summary>
    public ValidationRule Rule { get; }

    /// <summary>ERROR, WARNING or INFO.</summary>
    public string SeverityText => Finding.Severity switch
    {
        ValidationSeverity.Error => "ERROR",
        ValidationSeverity.Warning => "WARN",
        _ => "INFO",
    };

    /// <summary>Severity for styling.</summary>
    public ValidationSeverity Severity => Finding.Severity;

    /// <summary>The rule's category and name, e.g. "Capacity · Static capacity".</summary>
    public string RuleText => $"{Rule.Category} · {Rule.Name}";

    /// <summary>The explanation.</summary>
    public string Message => Finding.Message;

    /// <summary>True when the finding points at an object or place CHECK can zoom to.</summary>
    public bool CanGo => Finding.ObjectId is not null || Finding.At is not null;

    /// <summary>Ignored for this study (D-272).</summary>
    public bool IsIgnored
    {
        get => _isIgnored;
        set
        {
            if (SetProperty(ref _isIgnored, value))
            {
                OnPropertyChanged(nameof(IgnoreText));
            }
        }
    }

    /// <summary>Button label.</summary>
    public string IgnoreText => _isIgnored ? "UNIGNORE" : "IGNORE";

    /// <summary>Go to the object or place.</summary>
    public RelayCommand GoCommand { get; }

    /// <summary>Ignore or unignore for this study.</summary>
    public RelayCommand ToggleIgnoreCommand { get; }
}
