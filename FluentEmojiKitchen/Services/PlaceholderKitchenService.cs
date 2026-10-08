using System;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;

namespace FluentEmojiKitchen.Services;

// The uploaded project contains a simulated result load and an empty copy handler.
// Keep that unfinished feature explicit behind the interface used by the presenter.
public sealed class PlaceholderKitchenService : IKitchenService
{
    public async Task<KitchenResult> CreateAsync(EmojiModel left, EmojiModel right, int size, CancellationToken cancellationToken)
    {
        await Task.Delay(400, cancellationToken);
        return new KitchenResult(null, "Fusion images are not implemented yet.");
    }

    public Task CopyAsync(KitchenResult result, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("Fusion image copying is not implemented yet."));
}
