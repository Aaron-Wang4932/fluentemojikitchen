using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Models;

namespace FluentEmojiKitchen.Contracts;

public interface IKitchenService
{
    Task<KitchenResult> CreateAsync(EmojiModel left, EmojiModel right, int size, CancellationToken cancellationToken);
    Task CopyAsync(KitchenResult result, CancellationToken cancellationToken);
}
