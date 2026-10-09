using System;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Contracts;

public sealed record StartupData(IEmojiCatalog Catalog, IKitchenCompatibility Compatibility);
public interface IStartupDataLoader
{
    Task<StartupData> LoadAsync(IProgress<StartupProgress> progress, CancellationToken cancellationToken);
}
