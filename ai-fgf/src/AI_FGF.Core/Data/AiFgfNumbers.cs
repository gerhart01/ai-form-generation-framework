using System.Globalization;

namespace AI_FGF;

/// <summary>
/// Numbers typed by people and numbers stored in settings, read the same in every culture.
/// "1.5" is always one and a half; "1,5" is one and a half where the comma is the decimal mark
/// (ru-RU, de-DE…) — never fifteen, which a plain invariant <see cref="NumberStyles.Number"/>
/// parse makes of it (the comma counts as a thousands separator there).
/// </summary>
public static class AiFgfNumbers
{
    private const NumberStyles Plain = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
                                     | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Parse <paramref name="text"/>: a plain invariant number first ("-1234.5"), then the current
    /// culture with its group separators ("1 234,5" in ru-RU, "1,234.5" in en-US), then invariant
    /// with thousands ("1,234.5" anywhere).
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        string t = (text ?? "").Trim();
        return decimal.TryParse(t, Plain, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The invariant text of <paramref name="value"/> with exactly <paramref name="decimals"/> decimals, as the field shows it.</summary>
    public static string Format(decimal value, int decimals)
        => Math.Round(value, Math.Clamp(decimals, 0, 28), MidpointRounding.AwayFromZero)
               .ToString("F" + Math.Clamp(decimals, 0, 28).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>True when both texts are plain invariant numbers with the same value ("1.5" and "1.50"; not "1,000" and "1000").</summary>
    public static bool SameNumber(string? a, string? b)
        => decimal.TryParse(a, Plain, CultureInfo.InvariantCulture, out decimal x)
        && decimal.TryParse(b, Plain, CultureInfo.InvariantCulture, out decimal y) && x == y;
}
