using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Presentation;

public sealed record KitchenViewState(
    KitchenSlot ActiveSlot, EmojiItem? Left, EmojiItem? Right,
    bool IsLoading, KitchenResult? Result, bool IsCopying = false);
