using System;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Contracts;

public sealed record StartupData(IEmojiCatalog Catalog, IKitchenCompatibility Compatibility, string? FontPath = null);
public interface IStartupDataLoader
{
    bool NeedsRefresh() => false;
    Task<StartupData> LoadAsync(IProgress<StartupProgress> progress, CancellationToken cancellationToken, bool force = false);
}
