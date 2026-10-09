using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Services;
namespace FluentEmojiKitchen.Helpers;

public sealed record DownloadProgress(long Bytes, long? Total)
{
    public double Fraction => Total is > 0 ? Math.Min(1, (double)Bytes / Total.Value) : 0;
    public string Description => Total is > 0
        ? $"{Bytes / 1048576d:F1} / {Total.Value / 1048576d:F1} MB"
        : $"{Bytes / 1048576d:F1} MB downloaded";
}

public static class DownloadHelper
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    // Disk streaming and atomic promotion: an interrupted transfer cannot destroy the old cache.
    public static async Task DownloadFileAsync(string url, string downloadPath,
        Action<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var token = deadline.Token;
        Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
        string temporary = CacheFiles.Temporary(downloadPath);
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            long bytes = 0;
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[65536];
                long lastReport = Environment.TickCount64;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    bytes += read;
                    if (Environment.TickCount64 - lastReport >= 100)
                    {
                        progress?.Invoke(new(bytes, total));
                        lastReport = Environment.TickCount64;
                    }
                }
                if (total.HasValue && total.Value != bytes) throw new IOException("Incomplete download.");
                await target.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, downloadPath, true);
            progress?.Invoke(new(bytes, total));
        }
        finally { CacheFiles.DeleteTemporary(temporary); }
    }
}
