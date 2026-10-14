using System.Net.Http;

namespace LocaleGameHub.Services;

/// <summary>
/// Size-limited downloads. A wrong or malicious URL (e.g. a video dropped from a
/// browser) cannot fill the disk or memory, and a failed or cancelled download never
/// leaves a partial file behind.
/// </summary>
public static class DownloadGuard
{
    /// <summary>Covers: generous for 4K scans, far below anything abusive.</summary>
    public const long MaxImageBytes = 40L * 1024 * 1024;

    /// <summary>Locale Emulator release ZIP (about 1 MB today).</summary>
    public const long MaxArchiveBytes = 100L * 1024 * 1024;

    private const int BufferSize = 81920;

    /// <summary>Reads the whole body into memory, failing once it exceeds <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> ReadAllLimitedAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        RejectDeclaredSize(response, maxBytes);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        await CopyLimitedAsync(input, memory, maxBytes, cancellationToken);
        return memory.ToArray();
    }

    /// <summary>
    /// Streams the body to <paramref name="targetPath"/> (which must not exist).
    /// The file is deleted if the download fails, is too large or is cancelled.
    /// </summary>
    public static async Task CopyToNewFileLimitedAsync(HttpResponseMessage response, string targetPath, long maxBytes, CancellationToken cancellationToken)
    {
        RejectDeclaredSize(response, maxBytes);
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await CopyLimitedAsync(input, output, maxBytes, cancellationToken);
        }
        catch
        {
            TryDelete(targetPath);
            throw;
        }
    }

    /// <summary>Copies at most <paramref name="maxBytes"/>; throws as soon as the source has more.</summary>
    public static async Task CopyLimitedAsync(Stream input, Stream output, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes) throw TooLarge(maxBytes);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void RejectDeclaredSize(HttpResponseMessage response, long maxBytes)
    {
        // Fail before downloading anything when the server announces the size.
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw TooLarge(maxBytes);
    }

    private static InvalidOperationException TooLarge(long maxBytes)
    {
        var mb = maxBytes / (1024 * 1024);
        return new InvalidOperationException(LocalizationService.IsSpanish
            ? $"El archivo descargado supera el límite de {mb} MB, así que se descartó."
            : $"The downloaded file is larger than the {mb} MB limit, so it was discarded.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Nothing else to do; the covers cleanup removes leftovers later.
        }
    }
}
