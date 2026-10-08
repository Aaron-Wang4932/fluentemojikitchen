using System;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Contracts;

public interface IMainView
{
    event Action<PanelTab>? TabRequested;
    event Action? CloseRequested;
    void ShowTab(PanelTab tab);
    void SetLoading(bool loading, string message, double progress);
    void ShowInitializationError(string message);
}
