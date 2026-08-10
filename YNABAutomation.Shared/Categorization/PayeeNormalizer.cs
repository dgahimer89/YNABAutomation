using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YNABAutomationConsole.Categorization;

public sealed class PayeeNormalizer
{
    private static readonly Regex NonAlphaNumeric = new("[^a-z0-9 ]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new("\\s+", RegexOptions.Compiled);

    public string? Normalize(string? payeeName)
    {
        if (string.IsNullOrWhiteSpace(payeeName))
        {
            return null;
        }

        var value = payeeName.Trim().ToLower(CultureInfo.InvariantCulture);
        value = RemoveDiacritics(value);
        value = NonAlphaNumeric.Replace(value, " ");
        value = Whitespace.Replace(value, " ").Trim();
        return value.Length == 0 ? null : value;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
