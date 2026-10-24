namespace Throng.Sim;

/// <summary>
/// Small, fast, deterministic random generator (SplitMix64). Each agent gets its own stream derived from the
/// study seed and its index, so results stay reproducible even when the update is parallelised later (§17).
/// </summary>
/// <remarks>
/// System.Random is avoided because its algorithm isn't guaranteed stable across .NET versions.
/// </remarks>
public sealed class DeterministicRandom
{
    private ulong _state;

    /// <summary>Creates a generator from a seed.</summary>
    public DeterministicRandom(ulong seed) => _state = seed;

    /// <summary>Derives an independent stream seed from a study seed and a stream index (e.g. agent index).</summary>
    public static ulong DeriveSeed(ulong studySeed, ulong streamIndex)
    {
        ulong x = studySeed ^ (streamIndex * 0x9E3779B97F4A7C15UL);
        return Mix(x + 0xD1B54A32D192ED03UL);
    }

    /// <summary>Next raw 64-bit value.</summary>
    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform double in [min, max).</summary>
    public double NextRange(double min, double max) => min + ((max - min) * NextDouble());

    /// <summary>Uniform integer in [0, exclusiveMax).</summary>
    public int NextInt(int exclusiveMax) => exclusiveMax <= 0 ? 0 : (int)(NextUInt64() % (ulong)exclusiveMax);

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
