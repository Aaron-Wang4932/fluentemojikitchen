using System;
using FluentEmojiKitchen.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
namespace FluentEmojiKitchen.Views.Controls;

public sealed partial class KitchenSlotControl : UserControl
{
    public event EventHandler? SlotTapped;
    public event EventHandler? ClearRequested;
    public KitchenSlotControl() => InitializeComponent();
    public void Render(EmojiItem? item, bool active)
    {
        PlusText.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        EmojiText.Visibility = ClearButton.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        EmojiText.Text = item?.Character ?? "";
        SlotBorder.Stroke = (Brush)Application.Current.Resources[
            active ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush"];
    }
    private void OnTapped(object sender, TappedRoutedEventArgs e) => SlotTapped?.Invoke(this, EventArgs.Empty);
    private void OnClearTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }
}
