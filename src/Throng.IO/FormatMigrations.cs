using Throng.Core.Model;

namespace Throng.IO;

/// <summary>
/// Upgrades studies loaded from older file format versions so old files always open (§14.2).
/// </summary>
/// <remarks>
/// When <see cref="ThrongFile.CurrentFormatVersion"/> is bumped, add a step here that upgrades from the previous
/// version. Steps run in order, from the file's version up to the current one. There are none yet (format 1).
/// </remarks>
internal static class FormatMigrations
{
    /// <summary>One upgrade step: from <c>FromVersion</c> to <c>FromVersion + 1</c>.</summary>
    private static readonly IReadOnlyList<(int FromVersion, Action<Study> Upgrade)> Steps = [];

    /// <summary>Applies every step needed to bring a study from <paramref name="fileVersion"/> to current.</summary>
    public static void Apply(Study study, int fileVersion)
    {
        foreach ((int fromVersion, Action<Study> upgrade) in Steps.OrderBy(s => s.FromVersion))
        {
            if (fromVersion >= fileVersion)
            {
                upgrade(study);
            }
        }
    }
}
