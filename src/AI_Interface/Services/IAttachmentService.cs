using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>Reads attached files into forms the model can consume.</summary>
public interface IAttachmentService
{
    /// <summary>
    /// Extracts readable text from a document or text file (PDF, DOCX, or any plain-text/code file),
    /// truncated to <paramref name="maxChars"/>. Returns "" on failure.
    /// </summary>
    Task<string> ExtractTextAsync(string path, int maxChars, CancellationToken ct = default);

    /// <summary>Reads an image file as a base64 string for a vision model. Returns "" on failure.</summary>
    Task<string> ReadImageBase64Async(string path, CancellationToken ct = default);
}
