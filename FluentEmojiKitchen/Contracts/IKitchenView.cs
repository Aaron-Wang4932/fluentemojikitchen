using System;
using System.Collections.Generic;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Contracts;

public interface IKitchenView
{
    event Action<KitchenSlot>? SlotTapped;
    event Action<KitchenSlot>? SlotCleared;
    event Action<EmojiItem>? EmojiChosen;
    event Action<string>? SearchChanged;
    event Action<int>? SizeChangedRequested;
    event Action? CopyRequested;
    event Action? CopyURLRequested;
    void SetSections(IEnumerable<EmojiSection> sections);
    void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching);
    void Render(KitchenViewState state);
    void ResetSearch();
}
