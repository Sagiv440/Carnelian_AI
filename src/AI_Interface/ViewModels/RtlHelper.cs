namespace AI_Interface.ViewModels;

internal static class RtlHelper
{
    /// <summary>
    /// Returns true when the first strong directional letter in <paramref name="text"/> belongs to a
    /// right-to-left script (Hebrew, Arabic, Syriac, Thaana, NKo, etc.).  Neutral characters (spaces,
    /// punctuation, digits) are skipped until a letter is found.
    /// </summary>
    internal static bool IsRtl(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch) && ch != 0x200F) continue; // skip neutrals
            int c = ch;
            return (c >= 0x0590 && c <= 0x05FF)   // Hebrew
                || (c >= 0x0600 && c <= 0x06FF)   // Arabic, Persian, Urdu
                || (c >= 0x0700 && c <= 0x074F)   // Syriac
                || (c >= 0x0750 && c <= 0x077F)   // Arabic Supplement
                || (c >= 0x0780 && c <= 0x07FF)   // Thaana, NKo
                || (c >= 0x0800 && c <= 0x08FF)   // Samaritan, Mandaic, Arabic Extended-A
                || c == 0x200F;                    // RTL mark
        }
        return false;
    }
}
