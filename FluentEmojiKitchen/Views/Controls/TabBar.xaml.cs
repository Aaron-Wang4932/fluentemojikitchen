using System;
using FluentEmojiKitchen.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentEmojiKitchen.Views.Controls;

public sealed partial class TabBar : UserControl
{
    public event Action<PanelTab>? TabRequested;
    public event Action? CloseRequested;
    public TabBar() => InitializeComponent();
    public void ShowTab(PanelTab tab)
    {
        EmojiTabButton.IsChecked = tab == PanelTab.Emoji;
        KitchenTabButton.IsChecked = tab == PanelTab.Kitchen;
    }
    public void SetTabsEnabled(bool enabled) => EmojiTabButton.IsEnabled = KitchenTabButton.IsEnabled = enabled;
    private void OnEmojiTabClick(object sender, RoutedEventArgs e) => TabRequested?.Invoke(PanelTab.Emoji);
    private void OnKitchenTabClick(object sender, RoutedEventArgs e) => TabRequested?.Invoke(PanelTab.Kitchen);
    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
