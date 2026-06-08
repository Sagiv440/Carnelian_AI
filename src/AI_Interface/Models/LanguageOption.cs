namespace AI_Interface.Models;

/// <summary>
/// An entry in the voice-browser language dropdown. <see cref="Family"/> empty means "all languages".
/// </summary>
/// <param name="Family">Language family code (e.g. <c>en</c>), or "" for the all-languages option.</param>
/// <param name="Name">Display label (e.g. <c>English</c>, or <c>All languages</c>).</param>
public sealed record LanguageOption(string Family, string Name)
{
    public bool IsAll => string.IsNullOrEmpty(Family);
}
