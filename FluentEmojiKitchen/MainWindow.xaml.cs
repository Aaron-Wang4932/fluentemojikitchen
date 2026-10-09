using System;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluentEmojiKitchen;

// The shell only composes controls and renders presenter requests.
public sealed partial class MainWindow : Window, IMainView
{
    public event Action<PanelTab>? TabRequested;
    public event Action? CloseRequested;
    public EmojiPanelView EmojiView => EmojiPanel;
    public KitchenPanelView KitchenView => KitchenPanel;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Tabs);
        Tabs.TabRequested += tab => TabRequested?.Invoke(tab);
        Tabs.CloseRequested += () => CloseRequested?.Invoke();
    }

    public void ShowTab(PanelTab tab)
    {
        Tabs.ShowTab(tab);
        EmojiPanel.Visibility = tab == PanelTab.Emoji ? Visibility.Visible : Visibility.Collapsed;
        KitchenPanel.Visibility = tab == PanelTab.Kitchen ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetLoading(bool loading, string message, double progress)
    {
        Tabs.SetTabsEnabled(!loading);
        EmojiPanel.IsHitTestVisible = !loading;
        KitchenPanel.IsHitTestVisible = !loading;
        if (loading) StartupOverlay.Show(message, progress);
        else StartupOverlay.Visibility = Visibility.Collapsed;
    }

    public void ShowInitializationError(string message) => StartupOverlay.ShowError(message);
}
