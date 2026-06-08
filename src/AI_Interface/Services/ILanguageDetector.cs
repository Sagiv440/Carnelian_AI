namespace AI_Interface.Services;

/// <summary>Guesses the language of a piece of text so the right voice can be chosen to read it.</summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Returns a Piper-style language family code (e.g. <c>en</c>, <c>es</c>, <c>ru</c>, <c>zh</c>),
    /// defaulting to <c>en</c> when unsure.
    /// </summary>
    string Detect(string text);
}
