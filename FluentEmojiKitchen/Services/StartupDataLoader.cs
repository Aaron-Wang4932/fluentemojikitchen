using System;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

// Owns startup sequencing. Blocking parsing/compression runs off the UI thread.
public sealed class StartupDataLoader : IStartupDataLoader
{
    private readonly KitchenMetadataCache _cache;
    private readonly StartupSnapshotCache _snapshots = new();
    private readonly OnlineKitchenService _images;
    private readonly Action? _refreshPlatformCaches;
    private readonly EmojiFontCache _fonts = new(CacheFiles.DirectoryPath);
    public StartupDataLoader(KitchenMetadataCache cache, OnlineKitchenService images, Action? refreshPlatformCaches = null)
    { _cache = cache; _images = images; _refreshPlatformCaches = refreshPlatformCaches; }
    public bool NeedsRefresh() => EmojiDataService.NeedsRefresh() || _fonts.NeedsRefresh() ||
        CachePolicy.MonthlyExpired(System.IO.Path.Combine(CacheFiles.DirectoryPath, "kitchen-metadata.json"));
    public Task<StartupData> LoadAsync(IProgress<StartupProgress> progress, CancellationToken cancellationToken, bool force = false) =>
        Task.Run(async () =>
        {
            string pending = System.IO.Path.Combine(CacheFiles.DirectoryPath, "force-rebuild.pending");
            force |= System.IO.File.Exists(pending);
            if (force)
            {
                System.IO.Directory.CreateDirectory(CacheFiles.DirectoryPath);
                CacheFiles.WriteText(pending, "A forced refresh must finish before cached data is trusted again.");
                _images.InvalidateCache();
                CacheFiles.DeleteTemporary(System.IO.Path.Combine(CacheFiles.DirectoryPath, "im-subkeys-cache.txt"));
                _refreshPlatformCaches?.Invoke();
            }
            string font = await _fonts.LoadAsync(new CallbackProgress<StartupProgress>(p =>
                progress.Report(new(p.Message, p.Percent * .15))), cancellationToken, force).ConfigureAwait(false);
            var catalog = await EmojiDataService.CreateAsync(
                new CallbackProgress<StartupProgress>(p => progress.Report(new(p.Message, 15 + p.Percent * .25))),
                cancellationToken, force).ConfigureAwait(false);
            var index = await _cache.LoadAsync(
                new CallbackProgress<StartupProgress>(p => progress.Report(new(p.Message, 40 + p.Percent * .45))),
                cancellationToken, force).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string catalogRevision = StartupSnapshotCache.Fingerprint(System.IO.Path.Combine(CacheFiles.DirectoryPath, "emoji-list.json"));
            // The index includes a changing SourceUtc timestamp; hash raw metadata instead.
            string raw = System.IO.Path.Combine(CacheFiles.DirectoryPath, "kitchen-metadata.json");
            string compatibilityRevision = StartupSnapshotCache.Fingerprint(System.IO.File.Exists(raw) ? raw :
                System.IO.Path.Combine(CacheFiles.DirectoryPath, "kitchen-index.json.gz"));
            var data = _snapshots.Update(catalogRevision, () => catalog, compatibilityRevision,
                () => new KitchenCompatibilityService(index), font);
            _images.Initialize(data.Compatibility);
            if (force) System.IO.File.Delete(pending);
            return data;
        }, cancellationToken);
}

// Synchronous forwarding; only the top-level Progress<T> dispatches to the UI.
internal sealed class CallbackProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;
    public CallbackProgress(Action<T> report) => _report = report;
    public void Report(T value) => _report(value);
}
