using System.Text.RegularExpressions;

namespace transdb_geocoding.Services;

public class PostalCodeParseResult(string? postalCode, string? rest)
{
    public string? PostalCode { get; private set; } = postalCode;
    public string? Rest { get; private set; } = rest;
    public bool Success => this.PostalCode != null;
}

/// <summary>
/// Extracts postal codes from free-text input regardless of position or surrounding text.
/// Supports all common European postal code formats plus CA and UK.
/// </summary>
public static partial class PostalCodeParser
{
    private static readonly char[] Separators = [' ', ',', ';', '\t', '/'];

    /// <summary>
    /// Attempts to extract a postal code from a free-text string.
    /// Returns the extracted postal code or null if none is found.
    /// </summary>
    public static PostalCodeParseResult Extract(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new PostalCodeParseResult(null, null);

        var tokens = input.Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        
        // Single-token formats: postal code contains no spaces
        foreach (var token in tokens)
        {
            if (IsSingleToken(token))
            {
                return new PostalCodeParseResult(token, ParseRest(token, input));
            }
        }
            

        // Two-token formats: postal code contains one internal space (e.g. "113 47", "SW1A 1AA")
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var pair = $"{tokens[i]} {tokens[i + 1]}";
            if (IsPairToken(pair))
            {
                return new PostalCodeParseResult(pair, ParseRest(pair, input));
            }
        }

        return new PostalCodeParseResult(null, null);
    }

    private static string ParseRest(string postalCode, string input)
    {
        return input
            .Replace(postalCode, "", StringComparison.OrdinalIgnoreCase)
            .Trim(Separators);
    }

    // ── Single-token formats ─────────────────────────────────────────────────

    private static bool IsSingleToken(string t) =>
        Numeric4Or5().IsMatch(t)         // DE AT CH BE FR IT ES HU DK LU NO FI — "10115"
        || PolandFormat().IsMatch(t)     // PL                                   — "00-001"
        || PortugalFormat().IsMatch(t)   // PT                                   — "1000-001"
        || NetherlandsCompact().IsMatch(t); // NL (no space variant)             — "1234AB"

    // ── Two-token (space-separated) formats ──────────────────────────────────

    private static bool IsPairToken(string t) =>
        SwedenCzechFormat().IsMatch(t)   // SE CZ                                — "113 47"
        || NetherlandsFormat().IsMatch(t) // NL                                  — "1234 AB"
        || UnitedKingdomFormat().IsMatch(t) // GB                                — "SW1A 1AA"
        || CanadaFormat().IsMatch(t);    // CA                                   — "K1A 0A6"

    // ── Patterns ─────────────────────────────────────────────────────────────

    // 4–5 digits
    [GeneratedRegex(@"^\d{4,5}$")]
    private static partial Regex Numeric4Or5();

    // 2 digits, dash, 3 digits
    [GeneratedRegex(@"^\d{2}-\d{3}$")]
    private static partial Regex PolandFormat();

    // 4 digits, dash, 3 digits
    [GeneratedRegex(@"^\d{4}-\d{3}$")]
    private static partial Regex PortugalFormat();

    // 4 digits immediately followed by 2 letters (no space)
    [GeneratedRegex(@"^\d{4}[A-Za-z]{2}$")]
    private static partial Regex NetherlandsCompact();

    // 3 digits, space, 2 digits
    [GeneratedRegex(@"^\d{3} \d{2}$")]
    private static partial Regex SwedenCzechFormat();

    // 4 digits, space, 2 letters
    [GeneratedRegex(@"^\d{4} [A-Za-z]{2}$")]
    private static partial Regex NetherlandsFormat();

    // 1–2 letters, 1–2 digits, optional letter or digit, space, digit, 2 letters
    [GeneratedRegex(@"^[A-Za-z]{1,2}\d[A-Za-z\d]? \d[A-Za-z]{2}$")]
    private static partial Regex UnitedKingdomFormat();

    // Letter, digit, letter, space, digit, letter, digit
    [GeneratedRegex(@"^[A-Za-z]\d[A-Za-z] \d[A-Za-z]\d$")]
    private static partial Regex CanadaFormat();
}
