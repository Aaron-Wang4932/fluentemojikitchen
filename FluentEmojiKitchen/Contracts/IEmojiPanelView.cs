using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Contracts;

public interface IEmojiPanelView
{
    event Action<string>? SearchChanged;
    event Action<EmojiItem>? EmojiChosen;
    void SetSections(IEnumerable<EmojiSection> sections);
    void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching);
    void ResetView();
    void FocusSearch();
    void SetReady();
    void ShowError(string message);
    Task WarmUpAsync(Action<double> reportProgress);
}
