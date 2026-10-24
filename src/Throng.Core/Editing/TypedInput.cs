using System.Globalization;
using Throng.Core.Common;

namespace Throng.Core.Editing;

/// <summary>
/// Parses the typed-dimension field shown while drawing (D-038): one or two numbers in metres, separated by a
/// comma, semicolon, space or "x" (e.g. <c>12.5</c>, <c>12.5,8</c>, <c>12.5 x 8</c>). Decimal point only, so
/// input means the same on every machine.
/// </summary>
public static class TypedInput
{
    private static readonly char[] Separators = [',', ';', ' ', 'x', 'X', '\t'];

    /// <summary>Parses one or two finite numbers.</summary>
    public static Result<double[]> Parse(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Result<double[]>.Fail("TYPE A NUMBER");
        }

        string[] parts = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
        {
            return Result<double[]>.Fail("ONE OR TWO NUMBERS");
        }

        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].EndsWith('m') ? parts[i][..^1] : parts[i];
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                return Result<double[]>.Fail($"NOT A NUMBER: {parts[i]}");
            }

            values[i] = value;
        }

        return Result<double[]>.Ok(values);
    }
}
