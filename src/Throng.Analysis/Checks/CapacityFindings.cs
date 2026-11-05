using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Analysis.Checks;

/// <summary>
/// The capacity check's findings while it runs (round 017, D-296): Info lines carry how busy what they're about is
/// (people a minute at the peak), so <see cref="Trimmed"/> can keep only the busiest few per level. Warnings and
/// errors are always kept.
/// </summary>
internal sealed class CapacityFindings : List<ValidationMessage>
{
    private readonly Dictionary<ValidationMessage, double> _infoScores = new(ReferenceEqualityComparer.Instance);

    /// <summary>Adds an Info line with how busy its object is.</summary>
    public void AddInfo(double score, ValidationMessage finding)
    {
        Add(finding);
        _infoScores[finding] = double.IsFinite(score) ? score : 0;
    }

    /// <summary>Every finding except the Info lines beyond the busiest <paramref name="perLevel"/> on each level.</summary>
    public IReadOnlyList<ValidationMessage> Trimmed(Study study, int perLevel)
    {
        var keep = new HashSet<ValidationMessage>(ReferenceEqualityComparer.Instance);
        foreach (IGrouping<Guid, KeyValuePair<ValidationMessage, double>> level in _infoScores.GroupBy(pair => LevelOf(study, pair.Key)))
        {
            foreach (KeyValuePair<ValidationMessage, double> pair in level.OrderByDescending(p => p.Value).Take(Math.Max(0, perLevel)))
            {
                keep.Add(pair.Key);
            }
        }

        return [.. this.Where(f => !_infoScores.ContainsKey(f) || keep.Contains(f))];
    }

    /// <summary>The level a finding is about: its own, else its object's (device, connector bottom, shaft base).</summary>
    private static Guid LevelOf(Study study, ValidationMessage finding)
    {
        if (finding.LevelId is { } own)
        {
            return own;
        }

        if (finding.ObjectId is not { } id)
        {
            return Guid.Empty;
        }

        return study.Devices.FirstOrDefault(d => d.Id == id)?.LevelId
            ?? study.Connectors.FirstOrDefault(c => c.Id == id)?.BottomLevelId
            ?? study.Shafts.FirstOrDefault(s => s.Id == id)?.BaseLevelId
            ?? Guid.Empty;
    }
}
