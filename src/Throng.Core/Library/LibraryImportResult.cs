namespace Throng.Core.Library;

/// <summary>What a .thronglib import did to your library (round 020, D-347).</summary>
/// <param name="Added">New items, including copies kept beside yours.</param>
/// <param name="Replaced">Your items replaced by the incoming ones.</param>
/// <param name="Skipped">Clashing items you chose to skip.</param>
/// <param name="Unchanged">Items you already had with the same values.</param>
public sealed record LibraryImportResult(int Added, int Replaced, int Skipped, int Unchanged)
{
    /// <summary>True when the import changed your library.</summary>
    public bool Changed => Added + Replaced > 0;
}
