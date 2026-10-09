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
    private readonly OnlineKitchenService _images;
    public StartupDataLoader(KitchenMetadataCache cache, OnlineKitchenService images)
    { _cache = cache; _images = images; }
    public Task<StartupData> LoadAsync(IProgress<StartupProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var catalog = await EmojiDataService.CreateAsync(
                new CallbackProgress<StartupProgress>(p => progress.Report(new(p.Message, p.Percent * .25))),
                cancellationToken).ConfigureAwait(false);
            var index = await _cache.LoadAsync(
                new CallbackProgress<StartupProgress>(p => progress.Report(new(p.Message, 25 + p.Percent * .6))),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var compatibility = new KitchenCompatibilityService(index);
            _images.Initialize(compatibility);
            return new StartupData(catalog, compatibility);
        }, cancellationToken);
}

// Synchronous forwarding; only the top-level Progress<T> dispatches to the UI.
internal sealed class CallbackProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;
    public CallbackProgress(Action<T> report) => _report = report;
    public void Report(T value) => _report(value);
}
