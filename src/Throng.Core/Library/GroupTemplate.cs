using System.Globalization;
using System.Text.Json.Serialization;

namespace Throng.Core.Library;

/// <summary>
/// One row of a group template (D-382): people of a profile, between <see cref="Min"/> and <see cref="Max"/> of them
/// per group (a uniform roll each time a group spawns).
/// </summary>
/// <remarks>Immutable: edits replace the row (and the template's list) so undo restores the exact value.</remarks>
public sealed record GroupMember
{
    /// <summary>Agent profile template id (study library first, then the built-in catalogue).</summary>
    public Guid ProfileId { get; init; }

    /// <summary>Fewest people of this row in a group (0 = optional).</summary>
    public int Min { get; init; } = 1;

    /// <summary>Most people of this row in a group.</summary>
    public int Max { get; init; } = 1;

    /// <summary>A row of <paramref name="min"/>–<paramref name="max"/> people of a profile.</summary>
    public static GroupMember Of(Guid profileId, int min, int max) => new() { ProfileId = profileId, Min = min, Max = max };
}

/// <summary>
/// A group template (round 024, D-382–D-389): people who walk together (a family, friends, a carer and the person they
/// look after), made of rows of profiles with a count range each, and a default journey for mix lines set to
/// "(profile default)". The first row is the lead: their profile's soft preferences steer the group's choices, while
/// hard needs (accessible devices, lift only) of any member apply to everyone (D-387).
/// </summary>
public sealed class GroupTemplate : TemplateBase
{
    /// <summary>Fewest people a group can have.</summary>
    public const int MinPeople = 2;

    /// <summary>Most people a group can have.</summary>
    public const int MaxPeople = 8;

    /// <summary>Member rows; the first is the lead (D-387).</summary>
    public List<GroupMember> Members { get; set; } = [];

    /// <summary>Journey for mix lines set to "(profile default)" (D-389), or null to walk straight to a sink.</summary>
    public Guid? DefaultJourneyId { get; set; }

    /// <summary>Fewest people a spawned group of this template has (after the 2–8 limits).</summary>
    [JsonIgnore]
    public int MinSize => Math.Clamp(Rows().Sum(m => Math.Max(0, m.Min)), 1, MaxPeople);

    /// <summary>Most people a spawned group of this template has (after the 2–8 limits).</summary>
    [JsonIgnore]
    public int MaxSize => Math.Clamp(Rows().Sum(m => Math.Max(Math.Max(0, m.Min), m.Max)), 1, MaxPeople);

    /// <summary>The rows that can be used (not null).</summary>
    public IEnumerable<GroupMember> Rows() => (Members ?? []).Where(m => m is not null);

    /// <summary>
    /// People per row for one group (D-382): each row's count rolled between its min and max with
    /// <paramref name="next"/> (returns a double in [0, 1)), then trimmed from the last rows down to
    /// <see cref="MaxPeople"/> and topped up from the first rows to <see cref="MinPeople"/> where a row allows it.
    /// Always at least one person (the first row's) when there's a row.
    /// </summary>
    public static int[] Roll(IReadOnlyList<(int Min, int Max)> rows, Func<double> next)
    {
        var counts = new int[rows.Count];
        int total = 0;
        for (int k = 0; k < rows.Count; k++)
        {
            int min = Math.Max(0, rows[k].Min);
            int max = Math.Max(min, rows[k].Max);
            counts[k] = max > min ? min + Math.Min(max - min, (int)(next() * (max - min + 1))) : min;
            total += counts[k];
        }

        for (int k = rows.Count - 1; k >= 0 && total > MaxPeople; k--)
        {
            int cut = Math.Min(counts[k] - (k == 0 ? 1 : 0), total - MaxPeople);
            if (cut > 0)
            {
                counts[k] -= cut;
                total -= cut;
            }
        }

        for (int k = 0; k < rows.Count && total < MinPeople; k++)
        {
            int max = Math.Max(Math.Max(0, rows[k].Min), rows[k].Max);
            int add = Math.Min(max - counts[k], MinPeople - total);
            if (add > 0)
            {
                counts[k] += add;
                total += add;
            }
        }

        if (total == 0 && counts.Length > 0)
        {
            counts[0] = 1;
        }

        return counts;
    }

    /// <summary>Mean people per group, by the same roll without the limits' corner cases (for spawn shares, D-386).</summary>
    public static double MeanSize(IReadOnlyList<(int Min, int Max)> rows)
    {
        double mean = 0;
        foreach ((int min, int max) in rows)
        {
            int lo = Math.Max(0, min);
            mean += (lo + Math.Max(lo, max)) / 2.0;
        }

        return Math.Clamp(mean, 1, MaxPeople);
    }

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        IEnumerable<string> rows = Rows().Select(m =>
        {
            string name = (BuiltInLibrary.Instance.Find(m.ProfileId)?.Name ?? "profile").ToLowerInvariant();
            int max = Math.Max(Math.Max(0, m.Min), m.Max);
            string count = m.Min == max ? m.Min.ToString(c) : $"{Math.Max(0, m.Min).ToString(c)}–{max.ToString(c)}";
            return $"{count} {name}";
        });
        string size = MinSize == MaxSize ? MinSize.ToString(c) : $"{MinSize.ToString(c)}–{MaxSize.ToString(c)}";
        return $"{size} people · {string.Join(", ", rows)}";
    }
}
