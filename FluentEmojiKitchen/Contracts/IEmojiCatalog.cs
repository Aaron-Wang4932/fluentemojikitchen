using System.Collections.Generic;
using FluentEmojiKitchen.Models;

namespace FluentEmojiKitchen.Contracts;

public interface IEmojiCatalog
{
    IReadOnlyList<EmojiModel> Emojis { get; }
    IReadOnlyList<EmojiModel> SupportedFusionEmojis { get; }
    IReadOnlyList<string> RecentCharacters { get; }
    void AddRecent(string character);
}
