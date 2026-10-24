using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.Validation;

/// <summary>Severity of a validation finding (§10).</summary>
public enum ValidationSeverity
{
    /// <summary>Useful to know; nothing wrong.</summary>
    Info,

    /// <summary>Probably unintended; the study can still run.</summary>
    Warning,

    /// <summary>The study can't run correctly until fixed.</summary>
    Error,
}

/// <summary>One validation finding, optionally pointing at the offending object.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="ObjectId">Id of the object to zoom to, if any.</param>
public sealed record ValidationMessage(ValidationSeverity Severity, string Message, Guid? ObjectId = null);

/// <summary>
/// Structural checks on a study. M0 covers model integrity; the full rule set (reachability, capacity checks,
/// irrational setups) grows in M7.
/// </summary>
public static class StudyValidator
{
    private const double MinAreaM2 = 1e-4;

    /// <summary>Runs all checks and returns the findings (empty when clean).</summary>
    public static IReadOnlyList<ValidationMessage> Validate(Study study)
    {
        var messages = new List<ValidationMessage>();

        if (study.Levels.Count == 0)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, "The study has no levels."));
        }

        foreach (IGrouping<Guid, Level> duplicate in study.Levels.GroupBy(l => l.Id).Where(g => g.Count() > 1))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Level id {duplicate.Key} is used by more than one level.", duplicate.Key));
        }

        foreach (Level level in study.Levels)
        {
            if (level.HeightM <= 0)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Level '{level.Name}' has a height of zero or less.", level.Id));
            }

            if (!study.RegionsOn(level.Id).Any(r => r.Kind == RegionKind.Walkable))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Level '{level.Name}' has no walkable area.", level.Id));
            }
        }

        var levelIds = study.Levels.Select(l => l.Id).ToHashSet();
        foreach (Region region in study.Regions)
        {
            if (!levelIds.Contains(region.LevelId))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Region '{region.Name}' is on a level that doesn't exist.", region.Id));
                continue;
            }

            if (region.Kind == RegionKind.Wall)
            {
                if (region.Outline.Count < 2)
                {
                    messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Wall '{region.Name}' needs at least two points.", region.Id));
                }

                continue;
            }

            if (region.Outline.Count < 3 || PolygonMath.Area(region.Outline) < MinAreaM2)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Region '{region.Name}' has no area.", region.Id));
            }

            if (region.Kind == RegionKind.Zone && region.ZoneKind == ZoneKind.None)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Info, $"Zone '{region.Name}' has no purpose set.", region.Id));
            }
        }

        return messages;
    }

    /// <summary>True when the findings contain no errors.</summary>
    public static bool IsRunnable(IEnumerable<ValidationMessage> messages) =>
        messages.All(m => m.Severity != ValidationSeverity.Error);
}
