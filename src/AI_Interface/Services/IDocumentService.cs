namespace AI_Interface.Services;

/// <summary>
/// Creates and edits Office documents from text/markdown. Paths are absolute and already confined to the
/// project directory by the caller (<see cref="ProjectAgentService"/>); this service only does the file I/O.
/// </summary>
public interface IDocumentService
{
    /// <summary>Creates (or overwrites) a Word .docx from the given content. Returns the paragraph count.</summary>
    int CreateWord(string fullPath, string content);

    /// <summary>Appends the given content as new paragraphs to an existing .docx. Returns paragraphs added.</summary>
    int AppendWord(string fullPath, string content);

    /// <summary>Replaces every occurrence of <paramref name="find"/> with <paramref name="replace"/> in a
    /// .docx (within single text runs). Returns the number of occurrences replaced.</summary>
    int ReplaceInWord(string fullPath, string find, string replace);

    /// <summary>Creates (or overwrites) a PDF from the given content. Returns the rendered block count.</summary>
    int CreatePdf(string fullPath, string content);
}
