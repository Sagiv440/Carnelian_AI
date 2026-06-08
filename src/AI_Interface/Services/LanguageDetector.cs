using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AI_Interface.Services;

/// <summary>
/// Lightweight, dependency-free language guesser. First it looks at the writing system (decisive for
/// non-Latin scripts: Cyrillic, CJK, Arabic, …); for Latin text it scores a handful of common
/// stop-words per language and picks the best match. Returns Piper family codes; defaults to "en".
/// Good enough to route TTS to the right voice — not a full NLP detector.
/// </summary>
public sealed class LanguageDetector : ILanguageDetector
{
    // Common short words per Latin-script language. Hits are counted as whole words.
    private static readonly Dictionary<string, string[]> Stopwords = new()
    {
        ["en"] = new[] { "the", "and", "is", "are", "you", "that", "this", "with", "for", "have", "not", "it" },
        ["es"] = new[] { "el", "la", "los", "las", "y", "que", "de", "es", "un", "una", "por", "con", "para", "no" },
        ["fr"] = new[] { "le", "la", "les", "et", "que", "des", "un", "une", "est", "pas", "pour", "vous", "dans" },
        ["de"] = new[] { "der", "die", "das", "und", "ist", "nicht", "ein", "eine", "mit", "auch", "für", "den", "ich" },
        ["it"] = new[] { "il", "la", "che", "di", "e", "un", "una", "per", "non", "sono", "con", "questo", "gli" },
        ["pt"] = new[] { "o", "a", "os", "as", "que", "de", "um", "uma", "não", "para", "com", "isso", "você" },
        ["nl"] = new[] { "de", "het", "een", "en", "is", "niet", "dat", "van", "voor", "met", "ik", "je" },
        ["pl"] = new[] { "i", "nie", "to", "jest", "się", "na", "że", "w", "z", "do", "co", "jak" },
        ["sv"] = new[] { "och", "att", "det", "som", "en", "är", "för", "med", "inte", "jag", "du" },
        ["tr"] = new[] { "ve", "bir", "bu", "için", "ile", "değil", "çok", "ne", "var", "evet" },
    };

    // Ukrainian-distinctive letters (absent from Russian) — to split Cyrillic into uk vs ru.
    private const string UkrainianMarkers = "іїєґІЇЄҐ"; // і ї є ґ І Ї Є Ґ

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        // 1) Writing system — unambiguous for non-Latin scripts.
        var script = DetectByScript(text);
        if (script is not null)
            return script;

        // 2) Latin script — score stop-words.
        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}+")
            .Select(m => m.Value)
            .ToHashSet();
        if (words.Count == 0)
            return "en";

        var best = "en";
        var bestScore = 0;
        foreach (var (lang, list) in Stopwords)
        {
            var score = list.Count(words.Contains);
            if (score > bestScore)
            {
                bestScore = score;
                best = lang;
            }
        }
        return best;
    }

    /// <summary>Decide the language from Unicode script when the text isn't Latin-only.</summary>
    private static string? DetectByScript(string text)
    {
        int cyrillic = 0, han = 0, kana = 0, hangul = 0, arabic = 0, hebrew = 0, greek = 0, devanagari = 0, thai = 0;
        var ukrainian = false;

        foreach (var ch in text)
        {
            if (ch is >= 'Ѐ' and <= 'ӿ')
            {
                cyrillic++;
                if (UkrainianMarkers.IndexOf(ch) >= 0)
                    ukrainian = true;
            }
            else if ((ch is >= '぀' and <= 'ゟ') || (ch is >= '゠' and <= 'ヿ')) kana++;
            else if (ch is >= '가' and <= '힣') hangul++;
            else if (ch is >= '一' and <= '鿿') han++;
            else if (ch is >= '؀' and <= 'ۿ') arabic++;
            else if (ch is >= '֐' and <= '׿') hebrew++;
            else if (ch is >= 'Ͱ' and <= 'Ͽ') greek++;
            else if (ch is >= 'ऀ' and <= 'ॿ') devanagari++;
            else if (ch is >= '฀' and <= '๿') thai++;
        }

        if (kana > 0) return "ja";       // kana before han: Japanese mixes both
        if (hangul > 0) return "ko";
        if (han > 0) return "zh";
        if (cyrillic > 0) return ukrainian ? "uk" : "ru";
        if (arabic > 0) return "ar";
        if (hebrew > 0) return "he";
        if (greek > 0) return "el";
        if (devanagari > 0) return "hi";
        if (thai > 0) return "th";
        return null;
    }
}
