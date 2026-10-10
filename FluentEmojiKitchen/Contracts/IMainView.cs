using System;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Contracts;

public interface IMainView
{
    event Action<PanelTab>? TabRequested;
    event Action? CloseRequested;
    void ApplyEmojiFont(string path);
    void SetSettingsEnabled(bool enabled);
    void ShowTab(PanelTab tab);
    void SetLoading(bool loading, string message, double progress);
    void ShowInitializationError(string message);
}
