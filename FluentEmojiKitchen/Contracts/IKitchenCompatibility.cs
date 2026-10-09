using System.Collections.Generic;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Contracts;

public interface IKitchenCompatibility
{
    bool Supports(EmojiModel emoji);
    bool CanCombine(EmojiModel first, EmojiModel second);
    IReadOnlyList<string> GetLatestUrls(EmojiModel first, EmojiModel second);
}
