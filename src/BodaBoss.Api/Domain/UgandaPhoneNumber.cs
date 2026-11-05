using System.Text.RegularExpressions;

namespace BodaBoss.Api.Domain;

public static partial class UgandaPhoneNumber
{
    [GeneratedRegex(@"^256[37]\d{8}$")]
    private static partial Regex DigitsPattern();

    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!Regex.IsMatch(trimmed, @"^\+?[0-9\s()-]+$"))
        {
            return false;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length == 10)
        {
            digits = "256" + digits[1..];
        }
        else if (digits.Length == 9)
        {
            digits = "256" + digits;
        }

        if (!DigitsPattern().IsMatch(digits))
        {
            return false;
        }

        normalized = "+" + digits;
        return true;
    }
}
