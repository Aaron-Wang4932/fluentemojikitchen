using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Helpers;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

public sealed class KitchenMetadataCache
{
    private const string Source = "https://raw.githubusercontent.com/xsalazar/emoji-kitchen-backend/refs/heads/main/app/metadata.json";
    private readonly string _directory;
    private readonly KitchenMetadataParser _parser;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public KitchenMetadataCache(string directory, KitchenMetadataParser parser)
    { _directory = directory; _parser = parser; }

    // Call from the startup worker. Percent is local to this phase (0..100).
    public async Task<KitchenIndex> LoadAsync(IProgress<StartupProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        string raw = Path.Combine(_directory, "kitchen-metadata.json");
        string compact = Path.Combine(_directory, "kitchen-index.json.gz");
        KitchenIndex? cached = null;
        progress?.Report(new("Reading Kitchen cache...", 0));
        try
        {
            if (File.Exists(compact))
            {
                using var file = File.OpenRead(compact);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                cached = JsonSerializer.Deserialize(gzip, KitchenCacheJsonContext.Default.KitchenIndex);
                Validate(cached);
                if (DateTime.UtcNow - cached!.SourceUtc < MaxAge)
                {
                    progress?.Report(new("Kitchen cache ready", 100));
                    return cached;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
        { System.Diagnostics.Debug.WriteLine($"Rebuilding Kitchen cache: {ex.Message}"); cached = null; }
        token.ThrowIfCancellationRequested();
        string candidate = CacheFiles.Temporary(raw);
        try
        {
            KitchenIndex? result = null;
            if (File.Exists(raw) && DateTime.UtcNow - File.GetLastWriteTimeUtc(raw) < MaxAge)
            {
                try { result = Parse(raw, progress, token); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                { System.Diagnostics.Debug.WriteLine($"Redownloading invalid metadata: {ex.Message}"); }
            }
            if (result is null)
            {
                progress?.Report(new("Downloading Kitchen metadata...", 2));
                await DownloadHelper.DownloadFileAsync(Source, candidate,
                    p => progress?.Report(new($"Downloading Kitchen metadata: {p.Description}", 2 + 63 * p.Fraction)), token).ConfigureAwait(false);
                result = Parse(candidate, progress, token);
                token.ThrowIfCancellationRequested();
                File.Move(candidate, raw, true);
            }
            progress?.Report(new("Saving compact Kitchen index...", 94));
            Save(compact, result, token);
            progress?.Report(new("Kitchen cache ready", 100));
            return result;
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is System.Net.Http.HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException)
        {
            if (cached is not null)
            {
                progress?.Report(new("Offline or update unavailable — using saved Kitchen cache", 100));
                System.Diagnostics.Debug.WriteLine(ex);
                return cached;
            }
            if (File.Exists(raw))
            {
                // Missing compact cache can still be rebuilt offline from a valid, older raw file.
                var fallback = Parse(raw, progress, token);
                Save(compact, fallback, token);
                progress?.Report(new("Using saved Kitchen metadata", 100));
                return fallback;
            }
            throw;
        }
        finally { CacheFiles.DeleteTemporary(candidate); }
    }
    private KitchenIndex Parse(string path, IProgress<StartupProgress>? progress, CancellationToken token) =>
        _parser.Parse(path, p => progress?.Report(new($"Processing Kitchen combinations: {p:P0}", 65 + p * 28)), token);

    private static void Save(string path, KitchenIndex index, CancellationToken token)
    {
        string temporary = CacheFiles.Temporary(path);
        try
        {
            using (var file = File.Create(temporary))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                JsonSerializer.Serialize(gzip, index, KitchenCacheJsonContext.Default.KitchenIndex);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { CacheFiles.DeleteTemporary(temporary); }
    }
    private static void Validate(KitchenIndex? index)
    {
        if (index is null || index.SchemaVersion != 1 || index.SourceUtc == default ||
            index.Supported is null || index.Supported.Count == 0 || index.Compatible is null ||
            index.Latest is null || index.Urls is null || index.Urls.Count == 0 ||
            index.Urls.Any(u => !KitchenMetadataParser.IsImageUrl(u)))
            throw new InvalidDataException("Unsupported or empty Kitchen index.");
        foreach (var key in index.Supported)
            if (key is null || !index.Compatible.TryGetValue(key, out var partners) || partners is null ||
                partners.Any(p => p is null || !index.Compatible.ContainsKey(p)))
                throw new InvalidDataException("Invalid compatibility entry.");
        foreach (var entry in index.Latest)
        {
            var endpoints = entry.Key.Split('|');
            if (endpoints.Length != 2 || !index.Compatible.TryGetValue(endpoints[0], out var partners) ||
                !partners.Contains(endpoints[1]) || entry.Value is null || entry.Value.Length == 0 ||
                entry.Value.Any(id => id < 0 || id >= index.Urls.Count))
                throw new InvalidDataException("Invalid image reference.");
        }
    }
}
