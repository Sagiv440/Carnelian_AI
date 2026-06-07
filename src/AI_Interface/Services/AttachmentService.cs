using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IAttachmentService"/>: PDF text extraction via PdfPig and image base64 encoding.
/// All reads are best-effort — failures return an empty string rather than throwing, so a bad file
/// never aborts a send.
/// </summary>
public sealed class AttachmentService : IAttachmentService
{
    public Task<string> ExtractPdfTextAsync(string path, int maxChars, CancellationToken ct = default)
    {
        // PdfPig is synchronous and CPU-bound; run off the UI thread.
        return Task.Run(() =>
        {
            try
            {
                using var doc = PdfDocument.Open(path);
                var sb = new StringBuilder();
                foreach (var page in doc.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    sb.AppendLine(ContentOrderTextExtractor.GetText(page));
                    if (sb.Length >= maxChars)
                        break;
                }

                var text = sb.ToString().Trim();
                return text.Length > maxChars ? text[..maxChars] : text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return "";
            }
        }, ct);
    }

    public async Task<string> ReadImageBase64Async(string path, CancellationToken ct = default)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return Convert.ToBase64String(bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "";
        }
    }
}
