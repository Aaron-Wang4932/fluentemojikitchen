using System;
using System.Threading;
using System.Threading.Tasks;
namespace FluentEmojiKitchen.Contracts;

public interface IKitchenImageClipboard
{
    Task CopyAsync(Uri source, int pixelSize, CancellationToken cancellationToken);
}
