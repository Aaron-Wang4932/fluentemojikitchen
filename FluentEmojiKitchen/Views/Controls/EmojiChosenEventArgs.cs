using System;
using FluentEmojiKitchen.Presentation;
namespace FluentEmojiKitchen.Views.Controls;
public sealed class EmojiChosenEventArgs : EventArgs
{
    public EmojiItem Item { get; }
    public EmojiChosenEventArgs(EmojiItem item) => Item = item;
}
