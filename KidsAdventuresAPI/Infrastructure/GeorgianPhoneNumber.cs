namespace AdventurePacks.Api.Infrastructure;

/// <summary>
/// Georgian mobile numbers, normalised to E.164.
///
/// Parents type the same number a dozen ways — "555 12 34 56", "+995 555 123456",
/// "0555 123 456" — and every one of them has to resolve to the same account.
/// Normalising at the edge is what makes the filtered unique index on
/// <c>Users.PhoneNumber</c> and the OTP challenge lookup trustworthy.
/// </summary>
public static class GeorgianPhoneNumber
{
    private const string CountryCode = "995";
    private const int NationalLength = 9;

    /// <summary>Mobile ranges are the only ones that can receive an SMS.</summary>
    private const char MobilePrefix = '5';

    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Span<char> digits = stackalloc char[24];
        var length = 0;
        foreach (var ch in input)
        {
            if (!char.IsAsciiDigit(ch))
            {
                continue;
            }

            if (length == digits.Length)
            {
                return false;
            }

            digits[length++] = ch;
        }

        var national = new string(digits[..length]);

        if (national.StartsWith(CountryCode, StringComparison.Ordinal) &&
            national.Length == CountryCode.Length + NationalLength)
        {
            national = national[CountryCode.Length..];
        }
        else if (national.Length == NationalLength + 1 && national[0] == '0')
        {
            // Domestic trunk prefix.
            national = national[1..];
        }

        if (national.Length != NationalLength || national[0] != MobilePrefix)
        {
            return false;
        }

        e164 = $"+{CountryCode}{national}";
        return true;
    }

    public static string NormalizeOrThrow(string? input) =>
        TryNormalize(input, out var e164)
            ? e164
            : throw new InvalidOperationException("ტელეფონის ნომერი არასწორია. შეიყვანეთ ქართული მობილურის ნომერი, მაგალითად 555 12 34 56.");

    /// <summary>Keeps enough of the number to recognise it, not enough to leak it into a log.</summary>
    public static string Mask(string e164)
    {
        if (string.IsNullOrWhiteSpace(e164) || e164.Length < 6)
        {
            return "***";
        }

        return string.Concat(e164.AsSpan(0, 7), "***", e164.AsSpan(e164.Length - 2));
    }
}
