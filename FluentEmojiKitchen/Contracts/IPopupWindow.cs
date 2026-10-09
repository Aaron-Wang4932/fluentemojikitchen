using System;

namespace FluentEmojiKitchen.Contracts;

public interface IPopupWindow
{
    event Action? Hidden;
    event Action? Shown;
    void Toggle();
    void Hide();
}
