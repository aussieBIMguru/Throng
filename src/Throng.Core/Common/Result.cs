namespace Throng.Core.Common;

/// <summary>
/// Outcome of an operation that can fail without throwing. User-facing paths return results
/// instead of surfacing exceptions; exceptions are caught at the boundary and converted here.
/// </summary>
/// <param name="IsSuccess">True when the operation completed.</param>
/// <param name="Error">Human-readable reason when <see cref="IsSuccess"/> is false.</param>
public readonly record struct Result(bool IsSuccess, string? Error)
{
    /// <summary>A successful result.</summary>
    public static Result Ok() => new(true, null);

    /// <summary>A failed result with a human-readable message.</summary>
    public static Result Fail(string error) => new(false, error);
}

/// <summary>
/// Outcome of an operation that produces a value or fails without throwing.
/// </summary>
/// <typeparam name="T">Type of the produced value.</typeparam>
/// <param name="IsSuccess">True when <see cref="Value"/> is valid.</param>
/// <param name="Value">The produced value; default when the operation failed.</param>
/// <param name="Error">Human-readable reason when <see cref="IsSuccess"/> is false.</param>
public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(true, value, null);

    /// <summary>A failed result with a human-readable message.</summary>
    public static Result<T> Fail(string error) => new(false, default, error);
}
