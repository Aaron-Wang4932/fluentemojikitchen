using System;

namespace FluentEmojiKitchen.Contracts;

public interface IPopupWindow
{
    event Action? Hidden;
    void Toggle();
    void Hide();
}
