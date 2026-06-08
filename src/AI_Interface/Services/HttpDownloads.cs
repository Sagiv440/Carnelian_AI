using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>Streamed HTTP-to-file download with progress, shared by the Piper installer and catalog.</summary>
internal static class HttpDownloads
{
    public static async Task ToFileAsync(
        HttpClient http, string url, string destPath, string label,
        IProgress<string>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        var lastPct = -1;
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            readTotal += read;

            if (total > 0)
            {
                var pct = (int)(readTotal * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    progress?.Report($"{label} {pct}%  ({readTotal / 1_048_576} / {total / 1_048_576} MB)");
                }
            }
            else
            {
                progress?.Report($"{label} {readTotal / 1_048_576} MB");
            }
        }
    }
}
