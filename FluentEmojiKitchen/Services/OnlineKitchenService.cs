using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Helpers;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

// Retrieves original PNGs on demand. Presentation size is independent of the source URL.
public sealed class OnlineKitchenService : IKitchenService, IDisposable
{
    private readonly string _directory;
    private readonly KitchenImageCache _cache;
    private readonly object _gate = new();
    private IDisposable? _preview;
    private bool _disposed;
    private IKitchenCompatibility? _compatibility;
    private readonly IKitchenImageClipboard? _clipboard;
    public OnlineKitchenService(string directory, IKitchenImageClipboard? clipboard = null)
    { _directory = directory; _clipboard = clipboard; _cache = new KitchenImageCache(directory); }
    public void Initialize(IKitchenCompatibility compatibility) => _compatibility = compatibility;
    public async Task<KitchenResult> CreateAsync(EmojiModel left, EmojiModel right, int size, CancellationToken cancellationToken)
    {
        var urls = _compatibility?.GetLatestUrls(left, right) ?? Array.Empty<string>();
        if (urls.Count == 0) return new(null, "No current image is available for this combination.");
        string url = urls[0]; // Stable choice if upstream marks multiple images latest. All remain in the index.
        string filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".png";
        string path = Path.Combine(_directory, filename);
        using var downloadLease = _cache.Acquire(path);
        await Task.Run(async () =>
        {
            Directory.CreateDirectory(_directory);
            if (!ValidPng(path))
            {
                await DownloadHelper.DownloadFileAsync(url, path, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!ValidPng(path))
                {
                    CacheFiles.DeleteTemporary(path);
                    throw new InvalidDataException("The server did not return a PNG image.");
                }
            }
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var lease = _cache.Acquire(path);
            _preview?.Dispose();
            _preview = lease; // Keep the current source available even across a long idle session.
        }
        return new(new Uri(Path.GetFullPath(path)), null, Math.Clamp(size, 16, 512), _clipboard is not null);
    }
    private static bool ValidPng(string path)
    {
        if (!File.Exists(path)) return false;
        using var file = File.OpenRead(path);
        if (file.Length < 45) return false;
        Span<byte> header = stackalloc byte[8];
        if (file.Read(header) != 8 || !header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        file.Seek(-12, SeekOrigin.End);
        Span<byte> end = stackalloc byte[12];
        return file.Read(end) == 12 && end.SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 });
    }
    // Export scaling is separate from downloading and preview rendering.
    public async Task CopyAsync(KitchenResult result, CancellationToken cancellationToken)
    {
        if (result.ImageUri is null || _clipboard is null)
            throw new InvalidOperationException("No image is available to copy.");
        using var lease = _cache.Acquire(result.ImageUri.LocalPath);
        await _clipboard.CopyAsync(result.ImageUri, result.CopySize, cancellationToken);
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _preview?.Dispose();
            _preview = null;
            _cache.Dispose();
        }
    }
}
