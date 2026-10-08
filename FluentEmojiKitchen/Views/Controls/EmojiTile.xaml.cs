using System;
using FluentEmojiKitchen.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace FluentEmojiKitchen.Views.Controls;

public sealed partial class EmojiTile : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(EmojiItem), typeof(EmojiTile), new PropertyMetadata(null));
    public EmojiItem? Item
    {
        get => (EmojiItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
    public event EventHandler<EmojiChosenEventArgs>? Invoked;
    public EmojiTile() => InitializeComponent();
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => HoverLayer.Opacity = 1;
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HoverLayer.Opacity = 0;
    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (Item is not null) Invoked?.Invoke(this, new EmojiChosenEventArgs(Item));
    }
}
