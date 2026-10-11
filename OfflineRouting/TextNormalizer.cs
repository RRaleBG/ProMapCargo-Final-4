using System.Globalization;
using System.Text;

namespace ProMapCargo.OfflineRouting;

/// <summary>
/// Search keys for Serbian place and street names: Cyrillic is transliterated to
/// Latin, diacritics and case are dropped, so "Нови Сад", "Novi Sad" and "novi sad"
/// match, as do "Čačak" and "cacak".
/// </summary>
public static class TextNormalizer
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['ђ'] = "dj", ['е'] = "e",
        ['ж'] = "z", ['з'] = "z", ['и'] = "i", ['ј'] = "j", ['к'] = "k", ['л'] = "l", ['љ'] = "lj",
        ['м'] = "m", ['н'] = "n", ['њ'] = "nj", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s",
        ['т'] = "t", ['ћ'] = "c", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "c",
        ['џ'] = "dz", ['ш'] = "s",
        // Russian/Macedonian letters seen in OSM names.
        ['й'] = "j", ['щ'] = "s", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "ju",
        ['я'] = "ja", ['ё'] = "e", ['ѓ'] = "dj", ['ќ'] = "c", ['ѕ'] = "dz",
    };

    private static readonly Dictionary<char, string> CyrillicToSerbianLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['ђ'] = "đ", ['е'] = "e",
        ['ж'] = "ž", ['з'] = "z", ['и'] = "i", ['ј'] = "j", ['к'] = "k", ['л'] = "l", ['љ'] = "lj",
        ['м'] = "m", ['н'] = "n", ['њ'] = "nj", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s",
        ['т'] = "t", ['ћ'] = "ć", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "č",
        ['џ'] = "dž", ['ш'] = "š",
    };

    /// <summary>Lowercase ASCII search key: "Бул. краља Александра" → "bul kralja aleksandra".</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.ToLowerInvariant())
        {
            if (Cyrillic.TryGetValue(raw, out var latin))
            {
                builder.Append(latin);
                continue;
            }

            if (raw == 'đ')
            {
                builder.Append("dj");
                continue;
            }

            builder.Append(raw);
        }

        // Strip remaining diacritics (č ć š ž ...).
        var decomposed = builder.ToString().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                result.Append(' ');
                lastWasSpace = true;
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>Serbian Cyrillic to Serbian Latin with diacritics, for display.</summary>
    public static string ToSerbianLatin(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var lower = char.ToLowerInvariant(c);
            if (CyrillicToSerbianLatin.TryGetValue(lower, out var latin))
            {
                builder.Append(char.IsUpper(c) && latin.Length > 0
                    ? char.ToUpperInvariant(latin[0]) + latin[1..]
                    : latin);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
