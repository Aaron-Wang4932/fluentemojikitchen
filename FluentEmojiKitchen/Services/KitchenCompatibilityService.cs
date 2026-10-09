using System;
using System.Collections.Generic;
using System.Linq;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

public sealed class KitchenCompatibilityService : IKitchenCompatibility
{
    private readonly Dictionary<string, HashSet<string>> _partners;
    private readonly Dictionary<string, string[]> _latest;
    public KitchenCompatibilityService(KitchenIndex index)
    {
        _partners = index.Compatible.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value, StringComparer.Ordinal));
        _latest = index.Latest.ToDictionary(p => p.Key, p => p.Value.Select(id => index.Urls[id]).ToArray());
    }
    private static string Key(EmojiModel emoji) => string.Join("-", emoji.Codepoints.Select(cp => cp.ToString("x")));
    public bool Supports(EmojiModel emoji) => _partners.ContainsKey(Key(emoji));
    public bool CanCombine(EmojiModel first, EmojiModel second) =>
        _partners.TryGetValue(Key(first), out var partners) && partners.Contains(Key(second));
    public IReadOnlyList<string> GetLatestUrls(EmojiModel first, EmojiModel second) =>
        _latest.TryGetValue(KitchenMetadataParser.PairKey(Key(first), Key(second)), out var urls)
            ? Array.AsReadOnly(urls) : Array.Empty<string>();
}
