using System;
using System.IO;
using System.Security.Cryptography;
using FluentEmojiKitchen.Contracts;
namespace FluentEmojiKitchen.Services;

// One current snapshot, not a history. Forced downloads still run; unchanged content
// reuses the large compatibility graph and the catalogue already bound to the UI.
public sealed class StartupSnapshotCache
{
    private string? _catalogRevision, _compatibilityRevision;
    private IEmojiCatalog? _catalog;
    private IKitchenCompatibility? _compatibility;
    public static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public StartupData Update(string catalogRevision, Func<IEmojiCatalog> catalog,
        string compatibilityRevision, Func<IKitchenCompatibility> compatibility, string fontPath)
    {
        var nextCatalog = _catalog is not null && catalogRevision == _catalogRevision ? _catalog : catalog();
        var nextCompatibility = _compatibility is not null && compatibilityRevision == _compatibilityRevision
            ? _compatibility : compatibility();
        _catalog = nextCatalog; _compatibility = nextCompatibility;
        _catalogRevision = catalogRevision; _compatibilityRevision = compatibilityRevision;
        return new(nextCatalog, nextCompatibility, fontPath);
    }
}
