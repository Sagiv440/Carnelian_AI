using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IAttachmentService"/>: extracts text from documents (PDF via PdfPig, DOCX via its
/// zip XML, anything else read as plain text) and encodes images as base64. All reads are best-effort —
/// failures return an empty string rather than throwing, so a bad file never aborts a send.
/// </summary>
public sealed class AttachmentService : IAttachmentService
{
    public Task<string> ExtractTextAsync(string path, int maxChars, CancellationToken ct = default)
    {
        // Text extraction can be CPU-bound (PDF/DOCX) or blocking IO; run off the UI thread.
        return Task.Run(() =>
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var text = ext switch
                {
                    ".pdf" => ExtractPdf(path, maxChars, ct),
                    ".docx" => ExtractDocx(path),
                    ".odt" => ExtractOdt(path),
                    _ => File.ReadAllText(path) // txt, md, csv, json, code, etc.
                };

                text = text.Trim();
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

    private static string ExtractPdf(string path, int maxChars, CancellationToken ct)
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
        return sb.ToString();
    }

    /// <summary>Reads a .docx (a zip) and strips the XML of word/document.xml down to readable text.</summary>
    private static string ExtractDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
            return "";

        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();

        // Paragraphs and breaks become newlines; everything else (the tags) is removed.
        xml = Regex.Replace(xml, "</w:p>", "\n");
        xml = Regex.Replace(xml, "<w:br[^>]*/>", "\n");
        xml = Regex.Replace(xml, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(xml);
    }

    /// <summary>Reads an .odt (OpenDocument Text, a zip) and strips content.xml down to readable text.</summary>
    private static string ExtractOdt(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml");
        if (entry is null)
            return "";

        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();

        // Paragraphs/headings and breaks become newlines, tabs become tabs, then tags are removed.
        xml = Regex.Replace(xml, "</text:p>", "\n");
        xml = Regex.Replace(xml, "</text:h>", "\n");
        xml = Regex.Replace(xml, "<text:line-break[^>]*/>", "\n");
        xml = Regex.Replace(xml, "<text:tab[^>]*/>", "\t");
        xml = Regex.Replace(xml, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(xml);
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
