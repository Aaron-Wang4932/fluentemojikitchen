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
    public event Action? SettingsFocusRequested;
    public event Action<int>? ColumnsRequested;
    public event Action? RebuildRequested;
    private PanelTab _currentTab;
    private bool _settingsOpen, _loading;
    public PanelTab BeginColumnLayout()
    {
        // Only resizing from Kitchen requires temporarily measuring the Emoji panel.
        if (_currentTab == PanelTab.Kitchen)
        {
            EmojiPanel.Opacity = 0;
            EmojiPanel.Visibility = Visibility.Visible;
        }
        return _currentTab;
    }
    public void EndColumnLayout(PanelTab prior)
    {
        if (_currentTab == PanelTab.Kitchen) EmojiPanel.Visibility = Visibility.Collapsed;
        EmojiPanel.Opacity = 1;
    }
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
        Tabs.SettingsRequested += () =>
        {
            _settingsOpen = !_settingsOpen;
            RenderSettings();
            if (_settingsOpen) SettingsFocusRequested?.Invoke();
        };
        Settings.ColumnsRequested += columns => ColumnsRequested?.Invoke(columns);
        Settings.RebuildRequested += () => { HideSettings(); RebuildRequested?.Invoke(); };
    }

    public void FocusSettings() => Settings.FocusInput();
    public void SetColumns(int value) => Settings.SetColumns(value);
    public void ShowSettingsStatus(string value) => Settings.ShowStatus(value);
    public void HideSettings()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        RenderSettings();
    }
    private void RenderSettings()
    {
        // Opening/closing Settings never collapses or remeasures the emoji catalogue.
        SettingsBlocker.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        EmojiPanel.IsHitTestVisible = KitchenPanel.IsHitTestVisible = !_settingsOpen && !_loading;
        Tabs.SetTabsEnabled(!_settingsOpen && !_loading);
        Tabs.ShowTab(_currentTab);
    }
    public void SetSettingsEnabled(bool enabled) { Tabs.SetSettingsEnabled(enabled); Settings.SetBusy(!enabled); }
    public void ApplyEmojiFont(string path)
    {
        var cache = new Services.EmojiFontCache(Services.CacheFiles.DirectoryPath);
        cache.PruneOldGenerations(path, Services.Windows.EmojiFontResources.IsInUse);
        Services.Windows.EmojiFontResources.Apply(path);
    }
    public void ShowTab(PanelTab tab)
    {
        HideSettings();
        if (_currentTab == tab) { Tabs.ShowTab(tab); return; }
        _currentTab = tab;
        Tabs.ShowTab(tab);
        EmojiPanel.Visibility = tab == PanelTab.Emoji ? Visibility.Visible : Visibility.Collapsed;
        KitchenPanel.Visibility = tab == PanelTab.Kitchen ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetLoading(bool loading, string message, double progress)
    {
        _loading = loading;
        Tabs.SetTabsEnabled(!loading && !_settingsOpen);
        EmojiPanel.IsHitTestVisible = KitchenPanel.IsHitTestVisible = !loading && !_settingsOpen;
        if (loading) StartupOverlay.Show(message, progress);
        else StartupOverlay.Visibility = Visibility.Collapsed;
    }

    public void ShowInitializationError(string message) => StartupOverlay.ShowError(message);
}
